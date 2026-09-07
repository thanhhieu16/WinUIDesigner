using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
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

			var stack = InProgress ?? (InProgress = new List<object>());
			if (stack.Count >= MaxDepth)
				return false;
			for (int i = 0; i < stack.Count; i++) {
				if (ReferenceEquals(stack[i], target.Instance))
					return false;
			}

			stack.Add(target.Instance);
			try {
				return Write(target);
			} catch (Exception) {
				// Rule: never throw from the write path. A partially written element is a
				// degraded save; an exception here is a lost document and, mid-drag, a dead
				// process.
				return false;
			} finally {
				stack.RemoveAt(stack.Count - 1);
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
				if (CollectionSupport.IsCollectionType(descriptor.PropertyType)) {
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
			return wroteSomething;
		}
	}
}
