using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;

namespace WinUIDesigner.XamlDom
{
	/// <summary>
	/// Writes a live WinRT value's own properties into the <see cref="XamlObject"/> that carries
	/// it, for the types on an explicit allow-list.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="XamlDocument.CreatePropertyValue"/> emits an element for any value with no
	/// attribute text form, and before this class that element was always empty: a skewed control
	/// saved as <c>&lt;SkewTransform /&gt;</c> and lost both angles on every save. WPF filled the
	/// same hole with <c>System.Windows.Markup.XamlWriter.Save</c>; WinUI 3 ships no
	/// <c>XamlWriter</c> of any kind, only <c>XamlReader</c>, so the machinery is written here.
	/// </para>
	/// <para>
	/// Two separate gates. The allow-list bounds which <b>types</b> are attempted, so a
	/// <c>Storyboard</c> or a <c>BitmapImage</c> appearing in a document keeps today's behaviour
	/// rather than meeting untested code. The property filter - writable, and backed by a
	/// <see cref="DependencyProperty"/> - bounds which <b>members</b> of a listed type are
	/// considered; measured, it excludes exactly the members XAML must not carry
	/// (<c>Inverse</c>, <c>Bounds</c>, <c>Value</c>, <c>Dispatcher</c>, <c>DispatcherQueue</c>)
	/// and nothing that is wanted.
	/// </para>
	/// <para>
	/// Nothing here throws. <c>PlacementOperation</c> never calls <c>Abort()</c> around
	/// <c>SetPlacement</c> (PlacementOperation.cs:114) and <c>src/</c> installs no
	/// <c>Application.UnhandledException</c> handler, so a throw from a write during a drag
	/// strands the transaction and ends the process. Every refusal is a <c>false</c> return.
	/// </para>
	/// </remarks>
	internal static class XamlInstanceWriter
	{
		/// <summary>
		/// How deep the walk may go. A cycle is caught by <see cref="InProgress"/> first; this is
		/// the backstop for a deep-but-acyclic graph, and for a cycle that somehow evades
		/// reference identity.
		/// </summary>
		const int MaxDepth = 12;

		/// <summary>
		/// The instances currently being written, innermost last. Recursion re-enters this class
		/// through <see cref="XamlDocument.CreatePropertyValue"/> rather than through a direct
		/// call, so the depth and the cycle set cannot be parameters - they live here.
		/// Thread-static because every WinUI object is thread-affine and each dispatcher thread
		/// walks its own graph.
		/// </summary>
		[ThreadStatic]
		static List<object> InProgress;

		static readonly HashSet<Type> Listed = new HashSet<Type> {
			typeof(Microsoft.UI.Xaml.Media.SkewTransform),
			typeof(Microsoft.UI.Xaml.Media.RotateTransform),
			typeof(Microsoft.UI.Xaml.Media.ScaleTransform),
			typeof(Microsoft.UI.Xaml.Media.TranslateTransform),
			typeof(Microsoft.UI.Xaml.Media.RectangleGeometry),
			typeof(Microsoft.UI.Xaml.Media.EllipseGeometry),
			typeof(Microsoft.UI.Xaml.Media.LineGeometry),
			typeof(Microsoft.UI.Xaml.Media.GradientStop),
			typeof(Microsoft.UI.Xaml.Media.TransformGroup),
			typeof(Microsoft.UI.Xaml.Media.GeometryGroup),
			typeof(Microsoft.UI.Xaml.Media.LinearGradientBrush),
		};

		/// <summary>
		/// The declared property types worth reporting about when the writer refuses. Kept
		/// separate from <see cref="Listed"/>: a nested control under a property declared
		/// <c>object</c> is not this phase's business and must not produce a diagnostic.
		/// </summary>
		static readonly Type[] WarnFor = {
			typeof(Microsoft.UI.Xaml.Media.Transform),
			typeof(Microsoft.UI.Xaml.Media.Brush),
			typeof(Microsoft.UI.Xaml.Media.Geometry),
		};

		/// <summary>Gets whether values of <paramref name="type"/> are serialized by this class.</summary>
		public static bool HasEntry(Type type)
		{
			return type != null && Listed.Contains(type);
		}

		/// <summary>
		/// Gets whether a value the writer refused, under a property declared
		/// <paramref name="declaredType"/>, is worth reporting through <see cref="IXamlErrorSink"/>.
		/// </summary>
		public static bool ShouldWarnFor(Type declaredType)
		{
			if (declaredType == null)
				return false;
			foreach (var t in WarnFor) {
				if (t.IsAssignableFrom(declaredType))
					return true;
			}
			return false;
		}

		/// <summary>
		/// Writes <paramref name="target"/>'s instance properties into its element. Returns
		/// whether anything was written; a <c>false</c> return leaves the element as it was
		/// found, which is the empty form this codebase emitted before Phase C.
		/// </summary>
		public static bool TryWrite(XamlObject target)
		{
			if (target == null || target.Instance == null || !HasEntry(target.ElementType))
				return false;

			// Both guards below refuse to write an element this writer has an entry for, which
			// means the value is saved truncated. CreatePropertyValue's own "cannot save as
			// attribute text" report is suppressed for every listed type (it asks HasEntry), so
			// without these two calls the truncation is completely silent - the same silence
			// Phase C exists to end. Reachable: ADeeplyNestedGraphStopsAtTheDepthCapWithoutThrowing
			// drives the depth cap with a 20-deep TransformGroup nest.
			var stack = InProgress ?? (InProgress = new List<object>());
			if (stack.Count >= MaxDepth) {
				ReportTruncated(target, "the writer's depth limit of " + MaxDepth + " nested values was reached");
				return false;
			}
			for (int i = 0; i < stack.Count; i++) {
				if (ReferenceEquals(stack[i], target.Instance)) {
					ReportTruncated(target, "the value contains itself, and writing it would not terminate");
					return false;
				}
			}

			stack.Add(target.Instance);
			try {
				return Write(target);
			} catch (Exception ex) {
				// Rule: never throw from the write path. A partially written element is a
				// degraded save; an exception here is a lost document and, mid-drag, a dead
				// process. But "never throw" is not "never say anything" - this used to be a
				// bare `return false`, which meant the failure reached the user (if at all) as
				// CreatePropertyValue's "has no XAML attribute form" sink message, a wrong
				// diagnosis of a thrown error. Report it properly instead, the same way
				// XamlProperty.ReportValueFailure does for the equivalent read-path failure.
				ReportWriteFailure(target, ex);
				return false;
			} finally {
				stack.RemoveAt(stack.Count - 1);
			}
		}

		/// <summary>
		/// Reports an exception thrown while writing <paramref name="target"/>'s properties.
		/// Never throws itself - a failure while reporting a failure must still leave the write
		/// path's "never throw" rule intact.
		/// </summary>
		static void ReportWriteFailure(XamlObject target, Exception ex)
		{
			Debug.WriteLine("XamlInstanceWriter.TryWrite() failed - Exception:" + ex.Message);
			var typeName = target != null && target.ElementType != null ? target.ElementType.FullName : "value";
			ReportToSink(target,
				"Failed to write " + typeName + "'s properties into the saved element: " +
				ex.Message + ". The element was saved without them and will not survive a reload.");
		}

		/// <summary>
		/// Reports that a guard refused to write <paramref name="target"/>, so the element is
		/// saved empty. Distinct wording from <see cref="ReportWriteFailure"/>: nothing went
		/// wrong here, the writer declined on purpose, and the user still loses the value.
		/// </summary>
		static void ReportTruncated(XamlObject target, string reason)
		{
			var typeName = target != null && target.ElementType != null ? target.ElementType.FullName : "value";
			ReportToSink(target,
				"Did not write " + typeName + "'s properties into the saved element because " +
				reason + ". The element was saved empty and will not survive a reload.");
		}

		/// <summary>
		/// Sends <paramref name="message"/> to the document's <see cref="IXamlErrorSink"/> if it
		/// has one. Never throws - a failure while reporting a failure must still leave the write
		/// path's "never throw" rule intact.
		/// </summary>
		static void ReportToSink(XamlObject target, string message)
		{
			try {
				var serviceProvider = target != null && target.OwnerDocument != null
					? target.OwnerDocument.ServiceProvider
					: null;
				if (serviceProvider == null)
					return;
				var sink = serviceProvider.GetService(typeof(IXamlErrorSink)) as IXamlErrorSink;
				if (sink == null)
					return;
				sink.ReportError(message, 0, 0);
			} catch {
				// Reporting must never throw either.
			}
		}

		static bool Write(XamlObject target)
		{
			var type = target.ElementType;
			var instance = target.Instance;
			var source = instance as DependencyObject;
			if (source == null)
				return false;

			var wroteSomething = false;

			foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties(type)) {
				if (descriptor.IsReadOnly)
					continue;
				var dependencyProperty = WinUIStaticMemberLocator.FindDependencyProperty(type, descriptor.Name);
				if (dependencyProperty == null)
					continue;
				// A collection type with a working attribute text form (PointCollection,
				// DoubleCollection - see XamlValueFormatter's host table) must not be routed
				// through the child-element path below: it already round-trips as text, and no
				// listed type has such a property today, so this guard is currently unreachable
				// but keeps Write correct if one is ever added. What the fall-through actually
				// does is hand such a property to the scalar branch below - which gates on
				// ReadLocalValue, and a lazily-created collection reads UnsetValue there even
				// when populated (Decision 3), so it would `continue` and write nothing. The
				// guard exists to keep an untested path from silently taking the child-element
				// route, not because the fall-through is known to produce attribute text.
				if (CollectionSupport.IsCollectionType(descriptor.PropertyType)
					&& !XamlValueFormatter.HasFormatter(descriptor.PropertyType)) {
					if (WriteCollection(target, descriptor, descriptor.GetValue(instance)))
						wroteSomething = true;
					continue;
				}

				if (source.ReadLocalValue(dependencyProperty) == DependencyProperty.UnsetValue)
					continue;

				var value = descriptor.GetValue(instance);
				if (value == null)
					continue;

				var property = target.FindOrCreateProperty(descriptor.Name);
				property.PropertyValue = target.OwnerDocument.CreatePropertyValue(value, property);
				wroteSomething = true;
			}

			return wroteSomething;
		}

		/// <summary>
		/// Writes a collection property as an explicit <c>&lt;Owner.Property&gt;</c> element with
		/// one child element per item.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Not through <see cref="XamlProperty.CollectionElements"/>: that path mutates the live
		/// collection as well as the XML, and the items being written are already members of it.
		/// Measured, it throws <c>COMException 0x800F1000</c> out of
		/// <c>GradientStopCollection.Insert</c> - WinRT refuses a child that already has that
		/// parent. The parser-side entry point takes a node that is already in the XML DOM, which
		/// is what this method arranges.
		/// </para>
		/// <para>
		/// The gate is <c>Count &gt; 0</c> rather than <c>ReadLocalValue</c>, which Task 1 uses
		/// for scalars. Measured: a <c>GradientStopCollection</c> reads as not locally set even
		/// after two stops have been added to it, because the collection is the property's
		/// lazily-created default value and is mutated in place. Gating collections the way
		/// scalars are gated emits no children at all.
		/// </para>
		/// <para>
		/// The order is load-bearing and both wrong orders fail silently rather than throwing:
		/// a null property element registers the value without attaching its node, and appending
		/// the children after the call leaves an empty property element.
		/// </para>
		/// </remarks>
		static bool WriteCollection(XamlObject target, PropertyDescriptor descriptor, object collection)
		{
			var items = collection as IEnumerable;
			if (items == null)
				return false;

			var present = new List<object>();
			foreach (var item in items) {
				if (item != null)
					present.Add(item);
			}
			if (present.Count == 0)
				return false;

			var property = target.FindOrCreateProperty(descriptor.Name);
			if (!property.IsCollection)
				return false;

			var element = target.XmlElement;
			var propertyElement = element.OwnerDocument.CreateElement(
				element.Prefix, target.ElementType.Name + "." + descriptor.Name, element.NamespaceURI);
			element.AppendChild(propertyElement);

			var wroteSomething = false;
			foreach (var item in present) {
				var child = target.OwnerDocument.CreatePropertyValue(item, property) as XamlObject;
				if (child == null)
					continue;
				propertyElement.AppendChild(child.XmlElement);
				property.ParserAddCollectionElement(propertyElement, child);
				wroteSomething = true;
			}
			// Contract: a false return leaves the element as it was found. The property
			// element was appended optimistically, before any child was known to write
			// successfully - if every item's CreatePropertyValue came back non-XamlObject,
			// remove it rather than leaving an orphaned empty <Owner.Property/>.
			if (!wroteSomething)
				element.RemoveChild(propertyElement);
			return wroteSomething;
		}
	}
}
