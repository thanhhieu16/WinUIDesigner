using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Microsoft.UI.Xaml;

namespace WinUIDesigner.XamlDom
{
	/// <summary>
	/// Converts XAML attribute text to a WinRT-typed value by letting WinUI's own
	/// <c>XamlReader</c> perform the conversion on a synthetic one-element fragment.
	/// </summary>
	/// <remarks>
	/// <para>
	/// WinRT types projected into .NET carry no <see cref="TypeConverterAttribute"/>, so
	/// <c>TypeDescriptor.GetConverter</c> falls back to the base <see cref="TypeConverter"/>,
	/// whose <c>CanConvertFrom(string)</c> is false. Measured on this SDK: of 363 distinct
	/// property types on WinUI's <see cref="DependencyObject"/> surface, 115 report
	/// <c>CanConvertTo(string)</c> but not <c>CanConvertFrom(string)</c> - and the to-string claim
	/// is the base implementation's <c>ToString()</c>, which is why the gap never looked like a
	/// missing converter. The result was that only <c>double</c>, <c>enum</c> and <c>string</c>
	/// attribute values parsed at all.
	/// </para>
	/// <para>
	/// Rather than hand-write a converter per type, this class delegates to the platform:
	/// <c>&lt;Host P='text'/&gt;</c> is loaded through
	/// <c>Microsoft.UI.Xaml.Markup.XamlReader.Load</c> and <c>P</c> is read back. WinUI's own
	/// converters then handle every grammar exactly - <c>"Red"</c>, <c>"2*"</c>, <c>"Auto"</c>,
	/// <c>"M0,0 L10,10"</c>. <see cref="TemplateHelper"/> already uses <c>XamlReader.Load</c>
	/// inside this library for the same reason.
	/// </para>
	/// <para>
	/// Hosts are hand-listed. A reflection-built map was tried and rejected: ordering candidates
	/// by shortest type name selects <c>TwoPaneView.Pane1Length</c> for <see cref="GridLength"/>
	/// and <c>WebView2.DefaultBackgroundColor</c> for <c>Windows.UI.Color</c>, neither of which
	/// can be activated - and the resulting <c>XamlParseException</c> is indistinguishable from
	/// the one a bad value produces.
	/// </para>
	/// </remarks>
	internal sealed class XamlValueConverter : TypeConverter
	{
		const string PresentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

		readonly Type targetType;
		readonly Type hostType;
		readonly string hostPropertyName;

		internal XamlValueConverter(Type targetType, Type hostType, string hostPropertyName)
		{
			this.targetType = targetType;
			this.hostType = hostType;
			this.hostPropertyName = hostPropertyName;
		}

		#region Host table

		static readonly Dictionary<Type, KeyValuePair<Type, string>> HostTable = BuildHostTable();

		static Dictionary<Type, KeyValuePair<Type, string>> BuildHostTable()
		{
			var table = new Dictionary<Type, KeyValuePair<Type, string>>();
			Add(table, typeof(Thickness), typeof(Microsoft.UI.Xaml.Controls.Border), "Padding");
			Add(table, typeof(CornerRadius), typeof(Microsoft.UI.Xaml.Controls.Border), "CornerRadius");
			Add(table, typeof(GridLength), typeof(Microsoft.UI.Xaml.Controls.ColumnDefinition), "Width");
			Add(table, typeof(Windows.UI.Color), typeof(Microsoft.UI.Xaml.Media.SolidColorBrush), "Color");
			Add(table, typeof(Microsoft.UI.Xaml.Media.Brush), typeof(Microsoft.UI.Xaml.Controls.Border), "Background");
			Add(table, typeof(Microsoft.UI.Xaml.Media.FontFamily), typeof(Microsoft.UI.Xaml.Controls.TextBlock), "FontFamily");
			Add(table, typeof(Windows.UI.Text.FontWeight), typeof(Microsoft.UI.Xaml.Controls.TextBlock), "FontWeight");
			Add(table, typeof(Windows.Foundation.Point), typeof(Microsoft.UI.Xaml.Controls.Border), "RenderTransformOrigin");
			Add(table, typeof(Microsoft.UI.Xaml.Media.PointCollection), typeof(Microsoft.UI.Xaml.Shapes.Polygon), "Points");
			Add(table, typeof(Microsoft.UI.Xaml.Media.DoubleCollection), typeof(Microsoft.UI.Xaml.Shapes.Line), "StrokeDashArray");
			Add(table, typeof(Microsoft.UI.Xaml.Media.Geometry), typeof(Microsoft.UI.Xaml.Shapes.Path), "Data");
			Add(table, typeof(Windows.Foundation.Rect), typeof(Microsoft.UI.Xaml.Media.RectangleGeometry), "Rect");
			Add(table, typeof(Windows.Foundation.Size), typeof(Microsoft.UI.Xaml.Media.ArcSegment), "Size");
			Add(table, typeof(Duration), typeof(Microsoft.UI.Xaml.Media.Animation.Storyboard), "Duration");
			Add(table, typeof(Microsoft.UI.Xaml.Media.Matrix), typeof(Microsoft.UI.Xaml.Media.MatrixTransform), "Matrix");
			Add(table, typeof(System.Numerics.Vector3), typeof(Microsoft.UI.Xaml.Controls.Grid), "Translation");
			return table;
		}

		static void Add(Dictionary<Type, KeyValuePair<Type, string>> table, Type target, Type host, string property)
		{
			table[target] = new KeyValuePair<Type, string>(host, property);
		}

		#endregion

		/// <summary>
		/// Returns a converter for <paramref name="propertyType"/>, or <c>null</c> when this
		/// mechanism does not apply to it.
		/// </summary>
		public static TypeConverter TryCreate(Type propertyType)
		{
			if (propertyType == null)
				return null;

			// Uri-shaped types are excluded deliberately. XamlReader would construct a
			// BitmapImage and set UriSource for an ImageSource attribute, beginning an outbound
			// fetch - so merely opening an untrusted document would issue attacker-chosen
			// requests. XamlParser routes ImageSource through TypeFinder.ConvertUriToLocalUri
			// first, but the base implementation is a pass-through (XamlTypeFinder.cs:417-420).
			// Uri itself already has a working UriTypeConverter.
			if (typeof(Uri).IsAssignableFrom(propertyType))
				return null;
			if (typeof(Microsoft.UI.Xaml.Media.ImageSource).IsAssignableFrom(propertyType))
				return null;

			// NullableConverter delegates to the underlying type's converter, so unwrapping here
			// covers PointAnimation.From (Point?), ColorAnimation.From (Color?) and
			// ToolTip.PlacementRect (Rect?) without doubling the table. Nullable<double> and
			// friends never reach here: their converters report CanConvertFrom(string) already,
			// so GetCustomTypeConverter is not consulted for them.
			var lookupType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

			KeyValuePair<Type, string> host;
			if (!HostTable.TryGetValue(lookupType, out host))
				return null;

			return new XamlValueConverter(lookupType, host.Key, host.Value);
		}

		public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
		{
			return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
		}

		/// <remarks>
		/// Two callers ask this question and they need different answers.
		/// <c>XamlDocument.CreatePropertyValue</c> assigns <c>ctx.Instance</c> before asking
		/// (<c>XamlDocument.cs:283</c>) and wants to know whether THAT value has a text form;
		/// <c>PropertyNode</c> uses the one-argument overload, which passes a null context
		/// (measured), and wants to know whether the TYPE has one at all, because that is what
		/// gates its editable text box (<c>PropertyNode.cs:231-233</c>, <c>:108-118</c>).
		/// Answering per-instance is also what keeps the write path from throwing: an instance the
		/// formatter cannot handle is refused here, so <see cref="ConvertTo"/> is never reached
		/// for it and serialization simply falls through to the element form.
		/// </remarks>
		public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
		{
			if (destinationType == typeof(string)) {
				var instance = context != null ? context.Instance : null;
				return instance == null
					? XamlValueFormatter.HasFormatter(targetType)
					: XamlValueFormatter.CanFormat(targetType, instance);
			}
			return base.CanConvertTo(context, destinationType);
		}

		public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture,
		                                 object value, Type destinationType)
		{
			if (destinationType == typeof(string) && XamlValueFormatter.CanFormat(targetType, value))
				return XamlValueFormatter.Format(targetType, value);
			return base.ConvertTo(context, culture, value, destinationType);
		}

		public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
		{
			var text = value as string;
			if (text == null)
				return base.ConvertFrom(context, culture, value);

			var attributeText = text.StartsWith("{}", StringComparison.Ordinal) ? text.Substring(2) : text;

			// Markup extensions are out of scope, and letting one through is worse than failing:
			// an isolated fragment resolves {StaticResource}/{ThemeResource} against
			// Application.Current.Resources - the designer's own resource scope, not the
			// document's - so the designer would render the user's document with its own theme
			// values and say nothing. The guard runs on the STRIPPED text on purpose: "{}" is
			// XAML's escape for a literal leading brace, so "{}{Binding}" means the literal text
			// "{Binding}", and guarding before the strip let exactly that through.
			if (attributeText.StartsWith("{", StringComparison.Ordinal))
				throw new NotSupportedException(
					"Markup extension text cannot be converted to " + targetType.FullName + ": " + text);

			// Host viability depends on the hosting process's IXamlMetadataProvider and available
			// theme resources - a bare <TextBox/> does not load in this test host, for instance -
			// so a host proven here is not proven in every consumer. Checking it separately keeps
			// "this host is unusable" from being reported as "your value is wrong".
			if (!IsHostUsable(hostType))
				throw new NotSupportedException(
					"Conversion host <" + hostType.Name + "/> cannot be activated in this process, so " +
					targetType.FullName + " values cannot be converted here.");

			var xaml = "<" + hostType.Name + " xmlns='" + PresentationNamespace + "' " +
			           hostPropertyName + "='" + System.Security.SecurityElement.Escape(attributeText) + "'/>";

			object host;
			try {
				host = Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
			} catch (Exception) {
				// The XamlParseException's own message is unusable: WinUI caches the first
				// failure's message for the lifetime of the process and repeats it for every
				// later failure, across intervening successes, and its Line/Position refer to
				// this synthetic fragment rather than the user's file.
				throw new FormatException(FailureMessage(text));
			}

			var hostProperty = hostType.GetProperty(hostPropertyName, BindingFlags.Public | BindingFlags.Instance);
			if (hostProperty == null)
				throw new NotSupportedException(
					"Conversion host property " + hostType.Name + "." + hostPropertyName + " does not exist.");

			// WinUI does not always throw on a value it cannot use; it can leave the property
			// untouched, which is indistinguishable from a successful conversion to the same
			// default. The local value says which happened.
			var dependencyProperty = TryGetHostDependencyProperty();
			var hostObject = host as DependencyObject;
			if (dependencyProperty != null && hostObject != null &&
			    hostObject.ReadLocalValue(dependencyProperty) == DependencyProperty.UnsetValue)
				throw new FormatException(FailureMessage(text));

			var result = hostProperty.GetValue(host);

			// WinUI gives a reference-typed property value a single owner: a PointCollection or a
			// Geometry read off the synthetic host throws
			// ArgumentException("Value does not fall within the expected range") when it is later
			// assigned to the real element. Clearing the host's local value releases that claim,
			// after which the value assigns freely and can be reassigned more than once. Struct
			// values are unaffected, and a host with no dependency property (Grid.Translation) has
			// nothing to clear.
			if (dependencyProperty != null && hostObject != null) {
				// The value is already captured, so a failed release must not discard it: degrade
				// to the pre-fix behaviour for this one type rather than losing the conversion to
				// UpdateValueOnInstance's catch-all.
				try {
					hostObject.ClearValue(dependencyProperty);
				} catch (Exception) {
				}
			}

			if (result != null && !targetType.IsInstanceOfType(result))
				throw new FormatException(
					"Converting '" + text + "' produced a " + result.GetType().FullName +
					", which is not assignable to " + targetType.FullName + ".");

			return result;
		}

		string FailureMessage(string text)
		{
			return "Cannot convert '" + text + "' to " + targetType.FullName + ".";
		}

		#region Host viability

		static readonly Dictionary<Type, bool> hostViability = new Dictionary<Type, bool>();

		static bool IsHostUsable(Type hostType)
		{
			lock (hostViability) {
				bool usable;
				if (hostViability.TryGetValue(hostType, out usable))
					return usable;

				try {
					Microsoft.UI.Xaml.Markup.XamlReader.Load(
						"<" + hostType.Name + " xmlns='" + PresentationNamespace + "'/>");
					usable = true;
				} catch (Exception) {
					usable = false;
				}

				// Only a positive result is cached. A failure here can be environmental rather than
				// permanent - XamlReader.Load is thread-affine, so a call from a non-dispatcher
				// thread throws - and caching that would report "host unusable in this process" for
				// every later conversion of this type, including correct ones. Re-probing costs one
				// XamlReader.Load, and only on a path that is already failing.
				if (usable)
					hostViability[hostType] = true;
				return usable;
			}
		}

		#endregion

		DependencyProperty TryGetHostDependencyProperty()
		{
			try {
				return (DependencyProperty)WinUIStaticMemberLocator.GetStaticValue(
					hostType, hostPropertyName + "Property", typeof(DependencyProperty),
					includeNonPublic: true, walkBaseTypes: true);
			} catch (Exception) {
				return null;
			}
		}
	}
}
