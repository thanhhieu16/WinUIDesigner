using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using Microsoft.UI.Xaml;
using NUnit.Framework;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.XamlDom.Tests
{
    /// <summary>
    /// Phase A of the XamlDom value-conversion fix: the converter that turns XAML attribute text
    /// into a WinRT-typed value by letting WinUI's own XamlReader do the conversion.
    /// </summary>
    /// <remarks>
    /// Every test runs on the WinUI dispatcher thread (see <see cref="WinUITestHost"/>) because
    /// XamlReader.Load and every object it produces are thread-affine WinRT objects.
    /// </remarks>
    [TestFixture]
    public class ValueConversionTests
    {
        static object Convert(Type propertyType, string text)
        {
            var converter = XamlValueConverter.TryCreate(propertyType);
            Assert.IsNotNull(converter, "No converter was created for " + propertyType.FullName);
            return converter.ConvertFrom(null, CultureInfo.InvariantCulture, text);
        }

        [Test]
        public void ConvertsAValueTypeFromAttributeText()
        {
            WinUITestHost.Run(() => {
                var value = Convert(typeof(Thickness), "1,2,3,4");
                Assert.IsInstanceOf<Thickness>(value);
                var thickness = (Thickness)value;
                Assert.AreEqual(1.0, thickness.Left);
                Assert.AreEqual(2.0, thickness.Top);
                Assert.AreEqual(3.0, thickness.Right);
                Assert.AreEqual(4.0, thickness.Bottom);
            });
        }

        [Test]
        public void ConvertsADependencyObjectValueFromTheGeometryMiniLanguage()
        {
            WinUITestHost.Run(() => {
                var value = Convert(typeof(Microsoft.UI.Xaml.Media.Geometry), "M0,0 L10,10 Z");
                var geometry = value as Microsoft.UI.Xaml.Media.PathGeometry;
                Assert.IsNotNull(geometry, "Path mini-language did not produce a PathGeometry");
                Assert.AreEqual(1, geometry.Figures.Count);
                Assert.AreEqual(1, geometry.Figures[0].Segments.Count);
                Assert.IsTrue(geometry.Figures[0].IsClosed, "The trailing Z did not close the figure");
            });
        }

        [Test]
        public void ConvertsACollectionValueFromAttributeText()
        {
            WinUITestHost.Run(() => {
                var value = Convert(typeof(Microsoft.UI.Xaml.Media.PointCollection), "1,2 3,4 5,6");
                var points = value as Microsoft.UI.Xaml.Media.PointCollection;
                Assert.IsNotNull(points);
                Assert.AreEqual(3, points.Count);
                Assert.AreEqual(1.0, points[0].X);
                Assert.AreEqual(6.0, points[2].Y);
            });
        }

        [Test]
        public void UnwrapsNullableSoAnimationFromAndToConvert()
        {
            // PointAnimation.From is Nullable<Point>; its NullableConverter reports
            // CanConvertFrom(string) == false, so without unwrapping it stays broken.
            WinUITestHost.Run(() => {
                var value = Convert(typeof(Windows.Foundation.Point?), "0.5,0.25");
                Assert.IsInstanceOf<Windows.Foundation.Point>(value);
                var point = (Windows.Foundation.Point)value;
                Assert.AreEqual(0.5, point.X);
                Assert.AreEqual(0.25, point.Y);
            });
        }

        [Test]
        public void ProducesNoConverterForATypeWithNoHostEntry()
        {
            Assert.IsNull(XamlValueConverter.TryCreate(typeof(Microsoft.UI.Xaml.Controls.Grid)));
        }

        [Test]
        public void ProducesNoConverterForUriShapedTypes()
        {
            // Loading an untrusted document must not issue outbound requests: XamlReader would
            // construct a BitmapImage and set UriSource, which begins a fetch. Uri itself already
            // has a working UriTypeConverter and never needed an entry.
            Assert.IsNull(XamlValueConverter.TryCreate(typeof(Microsoft.UI.Xaml.Media.ImageSource)));
            Assert.IsNull(XamlValueConverter.TryCreate(typeof(Uri)));
        }

        [Test]
        public void RefusesMarkupExtensionText()
        {
            WinUITestHost.Run(() => {
                var converter = XamlValueConverter.TryCreate(typeof(Thickness));
                Assert.Throws<NotSupportedException>(
                    () => converter.ConvertFrom(null, CultureInfo.InvariantCulture, "{StaticResource SomeKey}"));
                // XamlTextValue.Text strips a leading "{}", so an escaped literal arrives here as a
                // live extension. This is the boundary that has to hold.
                Assert.Throws<NotSupportedException>(
                    () => converter.ConvertFrom(null, CultureInfo.InvariantCulture, "{Binding}"));
            });
        }

        [Test]
        public void AcceptsEscapedLiteralBraces()
        {
            WinUITestHost.Run(() => {
                var value = Convert(typeof(Thickness), "{}1,2,3,4");
                Assert.AreEqual(1.0, ((Thickness)value).Left);
            });
        }

        [Test]
        public void ReportsAHostThatCannotBeActivatedAsUnsupportedRatherThanAsABadValue()
        {
            // TwoPaneView is the host a reflection-built table picks for GridLength, and it cannot
            // be activated in this process. Its failure must not look like "your value is wrong".
            WinUITestHost.Run(() => {
                var converter = new XamlValueConverter(
                    typeof(GridLength), typeof(Microsoft.UI.Xaml.Controls.TwoPaneView), "Pane1Length");
                var ex = Assert.Throws<NotSupportedException>(
                    () => converter.ConvertFrom(null, CultureInfo.InvariantCulture, "2*"));
                StringAssert.Contains("TwoPaneView", ex.Message);
            });
        }

        [Test]
        public void RefusesAResultThatIsNotAssignableToTheTargetType()
        {
            // UIElement.Clip is declared RectangleGeometry, but WinUI runs the Geometry
            // mini-language on it, so the only viable host returns a PathGeometry. Assigning that
            // would throw InvalidCastException deep inside the parser.
            WinUITestHost.Run(() => {
                var converter = new XamlValueConverter(
                    typeof(Microsoft.UI.Xaml.Media.RectangleGeometry),
                    typeof(Microsoft.UI.Xaml.Shapes.Path), "Data");
                var ex = Assert.Throws<FormatException>(
                    () => converter.ConvertFrom(null, CultureInfo.InvariantCulture, "M0,0 L10,10"));
                StringAssert.Contains("RectangleGeometry", ex.Message);
            });
        }

        [Test]
        public void RejectsTextTheHostCannotParse()
        {
            WinUITestHost.Run(() => {
                var converter = XamlValueConverter.TryCreate(typeof(Thickness));
                Assert.Throws<FormatException>(
                    () => converter.ConvertFrom(null, CultureInfo.InvariantCulture, "not-a-thickness"));
            });
        }

        [Test]
        public void ConvertsAValueThatHappensToEqualTheHostPropertyDefault()
        {
            // The converter treats "WinUI ignored the value" (ReadLocalValue == UnsetValue) as a
            // failure. This pins that the check does not fire on a legitimate value that merely
            // equals the default.
            WinUITestHost.Run(() => {
                var value = Convert(typeof(Thickness), "0,0,0,0");
                Assert.IsInstanceOf<Thickness>(value);
                Assert.AreEqual(0.0, ((Thickness)value).Left);
            });
        }

        [Test]
        public void DoesNotClaimAToStringConversion()
        {
            // PropertyNode gates an editable PropertyGrid text box on
            // CanConvertFrom(string) && CanConvertTo(string). Claiming the to-string direction
            // without a formatter would prefill that box from value.ToString() - a type name - and
            // for FontFamily WinUI accepts any string as a family name, so tabbing through the
            // grid would commit "Microsoft.UI.Xaml.Media.FontFamily" as the user's font.
            var converter = XamlValueConverter.TryCreate(typeof(Thickness));
            Assert.IsTrue(converter.CanConvertFrom(null, typeof(string)));
            Assert.IsFalse(converter.CanConvertTo(null, typeof(string)));
        }

        [Test]
        public void ConvertsIdenticallyUnderANonInvariantCulture()
        {
            WinUITestHost.Run(() => {
                var previous = Thread.CurrentThread.CurrentCulture;
                try {
                    Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                    CultureInfo.CurrentCulture = new CultureInfo("de-DE");

                    var thickness = (Thickness)Convert(typeof(Thickness), "1.5,0,0,0");
                    Assert.AreEqual(1.5, thickness.Left, "Invariant decimal point was not honoured under de-DE");

                    var point = (Windows.Foundation.Point)Convert(typeof(Windows.Foundation.Point?), "1.5,2.5");
                    Assert.AreEqual(1.5, point.X);
                    Assert.AreEqual(2.5, point.Y);
                } finally {
                    Thread.CurrentThread.CurrentCulture = previous;
                    CultureInfo.CurrentCulture = previous;
                }
            });
        }
    }
}
