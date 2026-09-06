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

        // ---- One test per host-table entry. A host that cannot be activated, or a host property
        // whose grammar differs from the target type's, can then never ship silently.

        [Test]
        public void ConvertsCornerRadius()
        {
            WinUITestHost.Run(() => {
                var radius = (CornerRadius)Convert(typeof(CornerRadius), "5");
                Assert.AreEqual(5.0, radius.TopLeft);
                Assert.AreEqual(5.0, radius.BottomRight);
            });
        }

        [Test]
        public void ConvertsGridLengthInEveryUnit()
        {
            WinUITestHost.Run(() => {
                var star = (GridLength)Convert(typeof(GridLength), "2*");
                Assert.AreEqual(GridUnitType.Star, star.GridUnitType);
                Assert.AreEqual(2.0, star.Value);

                var fractionalStar = (GridLength)Convert(typeof(GridLength), "1.5*");
                Assert.AreEqual(GridUnitType.Star, fractionalStar.GridUnitType);
                Assert.AreEqual(1.5, fractionalStar.Value);

                var auto = (GridLength)Convert(typeof(GridLength), "Auto");
                Assert.AreEqual(GridUnitType.Auto, auto.GridUnitType);

                var pixel = (GridLength)Convert(typeof(GridLength), "40");
                Assert.AreEqual(GridUnitType.Pixel, pixel.GridUnitType);
                Assert.AreEqual(40.0, pixel.Value);
            });
        }

        [Test]
        public void ConvertsColorFromBothNamedAndHexForms()
        {
            WinUITestHost.Run(() => {
                var named = (Windows.UI.Color)Convert(typeof(Windows.UI.Color), "Red");
                Assert.AreEqual(255, named.R);
                Assert.AreEqual(0, named.G);
                Assert.AreEqual(255, named.A);

                var hex = (Windows.UI.Color)Convert(typeof(Windows.UI.Color), "#FF102030");
                Assert.AreEqual(0x10, hex.R);
                Assert.AreEqual(0x20, hex.G);
                Assert.AreEqual(0x30, hex.B);
            });
        }

        [Test]
        public void ConvertsBrushFromBothNamedAndHexForms()
        {
            WinUITestHost.Run(() => {
                var named = Convert(typeof(Microsoft.UI.Xaml.Media.Brush), "Red") as Microsoft.UI.Xaml.Media.SolidColorBrush;
                Assert.IsNotNull(named);
                Assert.AreEqual(255, named.Color.R);

                var hex = Convert(typeof(Microsoft.UI.Xaml.Media.Brush), "#FF102030") as Microsoft.UI.Xaml.Media.SolidColorBrush;
                Assert.IsNotNull(hex);
                Assert.AreEqual(0x30, hex.Color.B);
            });
        }

        [Test]
        public void ConvertsFontFamilyIncludingAFragmentUri()
        {
            WinUITestHost.Run(() => {
                var plain = (Microsoft.UI.Xaml.Media.FontFamily)Convert(
                    typeof(Microsoft.UI.Xaml.Media.FontFamily), "Consolas");
                Assert.AreEqual("Consolas", plain.Source);

                var fragment = (Microsoft.UI.Xaml.Media.FontFamily)Convert(
                    typeof(Microsoft.UI.Xaml.Media.FontFamily), "ms-appx:///Fonts/Custom.ttf#Custom");
                Assert.AreEqual("ms-appx:///Fonts/Custom.ttf#Custom", fragment.Source);
            });
        }

        [Test]
        public void ConvertsFontWeightFromItsNamedForm()
        {
            // WinUI rejects the numeric form: FontWeight='450' throws. Only names convert, so a
            // non-standard weight is not representable through this mechanism - a recorded gap.
            WinUITestHost.Run(() => {
                var bold = (Windows.UI.Text.FontWeight)Convert(typeof(Windows.UI.Text.FontWeight), "Bold");
                Assert.AreEqual(700, bold.Weight);

                var semiBold = (Windows.UI.Text.FontWeight)Convert(typeof(Windows.UI.Text.FontWeight), "SemiBold");
                Assert.AreEqual(600, semiBold.Weight);

                var converter = XamlValueConverter.TryCreate(typeof(Windows.UI.Text.FontWeight));
                Assert.Throws<FormatException>(
                    () => converter.ConvertFrom(null, CultureInfo.InvariantCulture, "450"),
                    "WinUI is expected to reject the numeric FontWeight form");
            });
        }

        [Test]
        public void ConvertsDoubleCollectionIncludingTheEmptyForm()
        {
            WinUITestHost.Run(() => {
                var dashes = (Microsoft.UI.Xaml.Media.DoubleCollection)Convert(
                    typeof(Microsoft.UI.Xaml.Media.DoubleCollection), "2 3");
                Assert.AreEqual(2, dashes.Count);
                Assert.AreEqual(2.0, dashes[0]);
                Assert.AreEqual(3.0, dashes[1]);

                var empty = (Microsoft.UI.Xaml.Media.DoubleCollection)Convert(
                    typeof(Microsoft.UI.Xaml.Media.DoubleCollection), "");
                Assert.AreEqual(0, empty.Count);
            });
        }

        [Test]
        public void ConvertsRect()
        {
            WinUITestHost.Run(() => {
                var rect = (Windows.Foundation.Rect)Convert(typeof(Windows.Foundation.Rect), "1,2,3,4");
                Assert.AreEqual(1.0, rect.X);
                Assert.AreEqual(2.0, rect.Y);
                Assert.AreEqual(3.0, rect.Width);
                Assert.AreEqual(4.0, rect.Height);
            });
        }

        [Test]
        public void ConvertsSize()
        {
            WinUITestHost.Run(() => {
                var size = (Windows.Foundation.Size)Convert(typeof(Windows.Foundation.Size), "3,4");
                Assert.AreEqual(3.0, size.Width);
                Assert.AreEqual(4.0, size.Height);
            });
        }

        [Test]
        public void ConvertsDurationInAllThreeForms()
        {
            WinUITestHost.Run(() => {
                // Asserted through ToString() because those are the exact forms measured on this
                // SDK; Duration's own member names were not verified and are not worth guessing.
                Assert.AreEqual("00:00:01", Convert(typeof(Duration), "0:0:1").ToString());
                Assert.AreEqual("Forever", Convert(typeof(Duration), "Forever").ToString());
                Assert.AreEqual("Automatic", Convert(typeof(Duration), "Automatic").ToString());
            });
        }

        [Test]
        public void ConvertsMatrix()
        {
            WinUITestHost.Run(() => {
                var matrix = Convert(typeof(Microsoft.UI.Xaml.Media.Matrix), "1,0,0,1,5,6");
                Assert.IsInstanceOf<Microsoft.UI.Xaml.Media.Matrix>(matrix);
                Assert.AreEqual("1,0,0,1,5,6", matrix.ToString());
            });
        }

        [Test]
        public void ConvertsVector3()
        {
            // UIElement.Translation is a plain CLR property with no TranslationProperty static, so
            // the ReadLocalValue guard cannot apply to this entry; it relies on the throw alone.
            WinUITestHost.Run(() => {
                var vector = (System.Numerics.Vector3)Convert(typeof(System.Numerics.Vector3), "1,2,3");
                Assert.AreEqual(1f, vector.X);
                Assert.AreEqual(2f, vector.Y);
                Assert.AreEqual(3f, vector.Z);
            });
        }

        [Test]
        public void ConvertsAnEmptyPointCollection()
        {
            WinUITestHost.Run(() => {
                var points = (Microsoft.UI.Xaml.Media.PointCollection)Convert(
                    typeof(Microsoft.UI.Xaml.Media.PointCollection), "");
                Assert.AreEqual(0, points.Count);
            });
        }

        [Test]
        public void HasNoEntryForTheTwoTypesWithNoViableHost()
        {
            // Recorded gaps, asserted so they are a decision rather than an oversight.
            // RectangleGeometry: UIElement.Clip declares it, but WinUI runs the Geometry
            // mini-language on that property, so the only host returns a PathGeometry.
            // Vector2: the only Vector2-typed property, UIElement.ActualSize, is read-only.
            Assert.IsNull(XamlValueConverter.TryCreate(typeof(Microsoft.UI.Xaml.Media.RectangleGeometry)));
            Assert.IsNull(XamlValueConverter.TryCreate(typeof(System.Numerics.Vector2)));
        }

        [Test]
        public void EveryHostInTheTableCanBeActivatedInThisProcess()
        {
            // The failure mode this guards is the reason the table is hand-listed at all: a
            // reflection-built map picked TwoPaneView and WebView2, which cannot be activated, and
            // their XamlParseException is indistinguishable from a bad value.
            var targets = new[] {
                typeof(Thickness), typeof(CornerRadius), typeof(GridLength), typeof(Windows.UI.Color),
                typeof(Microsoft.UI.Xaml.Media.Brush), typeof(Microsoft.UI.Xaml.Media.FontFamily),
                typeof(Windows.UI.Text.FontWeight), typeof(Windows.Foundation.Point),
                typeof(Microsoft.UI.Xaml.Media.PointCollection), typeof(Microsoft.UI.Xaml.Media.DoubleCollection),
                typeof(Microsoft.UI.Xaml.Media.Geometry), typeof(Windows.Foundation.Rect),
                typeof(Windows.Foundation.Size), typeof(Duration),
                typeof(Microsoft.UI.Xaml.Media.Matrix), typeof(System.Numerics.Vector3),
            };

            WinUITestHost.Run(() => {
                foreach (var target in targets) {
                    var converter = XamlValueConverter.TryCreate(target);
                    Assert.IsNotNull(converter, "No host-table entry for " + target.FullName);
                    // A NotSupportedException here means the host itself could not be activated.
                    // A FormatException would mean the host is fine and this filler text is not a
                    // valid value, which is expected and not what this test is about.
                    try {
                        converter.ConvertFrom(null, CultureInfo.InvariantCulture, "0");
                    } catch (NotSupportedException ex) {
                        Assert.Fail("Host for " + target.FullName + " is unusable: " + ex.Message);
                    } catch (FormatException) {
                        // Fine - the host activated and rejected the filler value.
                    }
                }
            });
        }
    }
}
