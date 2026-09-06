using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace WinUIDesigner.XamlDom
{
	/// <summary>
	/// Turns a WinRT-typed property value back into the XAML attribute text that
	/// <see cref="XamlValueConverter"/> reads.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The table is keyed by the same sixteen declared property types as
	/// <see cref="XamlValueConverter"/>'s host table, so no type gains a write path without a read
	/// path to verify it against. The key is always the DECLARED property type, never
	/// <c>value.GetType()</c>: the table matches exactly, and the instance under a
	/// <c>Path.Data</c> property is a <c>PathGeometry</c> while the entry is <c>Geometry</c>.
	/// </para>
	/// <para>
	/// Nothing here reaches <c>ToString()</c>. <c>TypeConverter.ConvertTo</c> ignores the culture
	/// it is handed and calls <c>value.ToString()</c>, so <c>ConvertToInvariantString</c> does not
	/// make a culture-dependent <c>ToString()</c> safe - measured, <c>Point.ToString()</c> yields
	/// <c>'1,5;2,5'</c> under <c>de-DE</c>.
	/// </para>
	/// </remarks>
	internal static class XamlValueFormatter
	{
		delegate bool Predicate(object value);
		delegate string Writer(object value);

		sealed class Entry
		{
			public readonly Predicate CanFormat;
			public readonly Writer Format;

			public Entry(Predicate canFormat, Writer format)
			{
				this.CanFormat = canFormat;
				this.Format = format;
			}
		}

		static readonly Dictionary<Type, Entry> Table = BuildTable();

		static Dictionary<Type, Entry> BuildTable()
		{
			var table = new Dictionary<Type, Entry>();

			Add(table, typeof(Thickness), v => v is Thickness, v => {
				var t = (Thickness)v;
				return Num(t.Left) + "," + Num(t.Top) + "," + Num(t.Right) + "," + Num(t.Bottom);
			});

			Add(table, typeof(CornerRadius), v => v is CornerRadius, v => {
				var c = (CornerRadius)v;
				return Num(c.TopLeft) + "," + Num(c.TopRight) + "," + Num(c.BottomRight) + "," + Num(c.BottomLeft);
			});

			Add(table, typeof(GridLength), v => v is GridLength, v => {
				var g = (GridLength)v;
				if (g.IsAuto)
					return "Auto";
				if (g.IsStar)
					return g.Value == 1.0 ? "*" : Num(g.Value) + "*";
				return Num(g.Value);
			});

			Add(table, typeof(Windows.UI.Color), v => v is Windows.UI.Color,
				v => Hex((Windows.UI.Color)v));

			// Only SolidColorBrush has an attribute form. The named-colour output reproduces what
			// XamlDocument.cs:287-290 already emits, which RoundTripTests asserts.
			Add(table, typeof(Brush), v => v is SolidColorBrush, v => {
				var color = ((SolidColorBrush)v).Color;
				string name;
				return XamlDocument.ColorBrushDictionary.TryGetValue(color, out name) ? name : Hex(color);
			});

			Add(table, typeof(FontFamily),
				v => v is FontFamily && !string.IsNullOrEmpty(((FontFamily)v).Source),
				v => ((FontFamily)v).Source);

			Add(table, typeof(Windows.UI.Text.FontWeight),
				v => v is Windows.UI.Text.FontWeight && FontWeightName(((Windows.UI.Text.FontWeight)v).Weight) != null,
				v => FontWeightName(((Windows.UI.Text.FontWeight)v).Weight));

			Add(table, typeof(Point), v => v is Point, v => Pt((Point)v));

			Add(table, typeof(PointCollection), v => v is PointCollection, v => {
				var text = new StringBuilder();
				foreach (var point in (PointCollection)v) {
					if (text.Length > 0)
						text.Append(' ');
					text.Append(Pt(point));
				}
				return text.ToString();
			});

			Add(table, typeof(DoubleCollection), v => v is DoubleCollection, v => {
				var text = new StringBuilder();
				foreach (var value in (DoubleCollection)v) {
					if (text.Length > 0)
						text.Append(',');
					text.Append(Num(value));
				}
				return text.ToString();
			});

			Add(table, typeof(Geometry),
				v => v is Geometry && GeometryMiniLanguageWriter.CanWrite((Geometry)v),
				v => GeometryMiniLanguageWriter.Write((PathGeometry)v));

			Add(table, typeof(Rect), v => v is Rect, v => {
				var r = (Rect)v;
				return Num(r.X) + "," + Num(r.Y) + "," + Num(r.Width) + "," + Num(r.Height);
			});

			Add(table, typeof(Size), v => v is Size, v => {
				var s = (Size)v;
				return Num(s.Width) + "," + Num(s.Height);
			});

			Add(table, typeof(Duration), v => v is Duration, v => {
				var d = (Duration)v;
				if (d.HasTimeSpan)
					return d.TimeSpan.ToString("c", CultureInfo.InvariantCulture);
				return d == Duration.Forever ? "Forever" : "Automatic";
			});

			Add(table, typeof(Matrix), v => v is Matrix, v => {
				var m = (Matrix)v;
				return Num(m.M11) + "," + Num(m.M12) + "," + Num(m.M21) + "," + Num(m.M22) + "," +
				       Num(m.OffsetX) + "," + Num(m.OffsetY);
			});

			Add(table, typeof(System.Numerics.Vector3), v => v is System.Numerics.Vector3, v => {
				var vec = (System.Numerics.Vector3)v;
				return Num(vec.X) + "," + Num(vec.Y) + "," + Num(vec.Z);
			});

			return table;
		}

		static void Add(Dictionary<Type, Entry> table, Type targetType, Predicate canFormat, Writer format)
		{
			table[targetType] = new Entry(canFormat, format);
		}

		/// <summary>
		/// Gets whether values of <paramref name="targetType"/> have a text form at all. This is
		/// the answer for a caller that has no instance to ask about - the PropertyGrid, through
		/// the one-argument <c>CanConvertTo</c> overload.
		/// </summary>
		public static bool HasFormatter(Type targetType)
		{
			return targetType != null && Table.ContainsKey(targetType);
		}

		/// <summary>
		/// Gets whether this particular <paramref name="value"/> has a faithful text form.
		/// </summary>
		public static bool CanFormat(Type targetType, object value)
		{
			Entry entry;
			if (targetType == null || value == null || !Table.TryGetValue(targetType, out entry))
				return false;
			return entry.CanFormat(value);
		}

		/// <summary>
		/// Writes <paramref name="value"/> as XAML attribute text. Only call this when
		/// <see cref="CanFormat"/> returned <c>true</c> for the same pair.
		/// </summary>
		public static string Format(Type targetType, object value)
		{
			Entry entry;
			if (targetType == null || !Table.TryGetValue(targetType, out entry))
				throw new NotSupportedException("No formatter for " + (targetType == null ? "null" : targetType.FullName));
			if (value == null || !entry.CanFormat(value))
				throw new NotSupportedException(
					"Values of this shape have no text form: " + (value == null ? "null" : value.GetType().FullName));
			return entry.Format(value);
		}

		#region Primitives

		static string Num(double value)
		{
			return value.ToString("R", CultureInfo.InvariantCulture);
		}

		static string Num(float value)
		{
			return value.ToString("R", CultureInfo.InvariantCulture);
		}

		static string Pt(Point point)
		{
			return Num(point.X) + "," + Num(point.Y);
		}

		static string Hex(Windows.UI.Color color)
		{
			return "#" + color.A.ToString("X2", CultureInfo.InvariantCulture)
			           + color.R.ToString("X2", CultureInfo.InvariantCulture)
			           + color.G.ToString("X2", CultureInfo.InvariantCulture)
			           + color.B.ToString("X2", CultureInfo.InvariantCulture);
		}

		/// <summary>
		/// Maps a numeric weight to the XAML keyword for it, or <c>null</c> when there is none.
		/// The numeric form is not written: Phase A cannot read it back.
		/// </summary>
		static string FontWeightName(ushort weight)
		{
			switch (weight) {
				case 100: return "Thin";
				case 200: return "ExtraLight";
				case 300: return "Light";
				case 350: return "SemiLight";
				case 400: return "Normal";
				case 500: return "Medium";
				case 600: return "SemiBold";
				case 700: return "Bold";
				case 800: return "ExtraBold";
				case 900: return "Black";
				case 950: return "ExtraBlack";
				default: return null;
			}
		}

		#endregion
	}
}
