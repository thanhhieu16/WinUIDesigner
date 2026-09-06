using System;
using System.Globalization;
using Microsoft.UI.Xaml.Media;
using NUnit.Framework;
using Windows.Foundation;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.XamlDom.Tests
{
    /// <summary>
    /// Phase B of the XamlDom value-conversion fix: turning a WinRT-typed value back into the
    /// XAML attribute text that Phase A's converter can read.
    /// </summary>
    /// <remarks>
    /// Every test runs on the WinUI dispatcher thread (see <see cref="WinUITestHost"/>) because
    /// every object involved is a thread-affine WinRT object.
    /// </remarks>
    [TestFixture]
    public class ValueFormattingTests
    {
        /// <summary>A figure carrying one of every segment type this codebase can produce.</summary>
        static PathGeometry BuildAllSegmentGeometry()
        {
            var figure = new PathFigure { StartPoint = new Point(0, 0), IsClosed = true };
            figure.Segments.Add(new LineSegment { Point = new Point(10, 0) });

            var polyLine = new PointCollection();
            polyLine.Add(new Point(20, 5));
            polyLine.Add(new Point(30, 10));
            figure.Segments.Add(new PolyLineSegment { Points = polyLine });

            figure.Segments.Add(new BezierSegment {
                Point1 = new Point(40, 0), Point2 = new Point(50, 20), Point3 = new Point(60, 10)
            });

            var polyBezier = new PointCollection();
            polyBezier.Add(new Point(70, 0));
            polyBezier.Add(new Point(80, 20));
            polyBezier.Add(new Point(90, 10));
            figure.Segments.Add(new PolyBezierSegment { Points = polyBezier });

            figure.Segments.Add(new QuadraticBezierSegment {
                Point1 = new Point(100, 0), Point2 = new Point(110, 10)
            });

            var polyQuad = new PointCollection();
            polyQuad.Add(new Point(120, 0));
            polyQuad.Add(new Point(130, 10));
            figure.Segments.Add(new PolyQuadraticBezierSegment { Points = polyQuad });

            figure.Segments.Add(new ArcSegment {
                Point = new Point(140, 10), Size = new Size(5, 7.5), RotationAngle = 30,
                IsLargeArc = true, SweepDirection = SweepDirection.Clockwise
            });

            var geometry = new PathGeometry { FillRule = FillRule.Nonzero };
            geometry.Figures.Add(figure);
            return geometry;
        }

        [Test]
        public void WritesEverySegmentTypeInTheCanonicalForm()
        {
            WinUITestHost.Run(() => {
                var geometry = BuildAllSegmentGeometry();
                Assert.IsTrue(GeometryMiniLanguageWriter.CanWrite(geometry));
                Assert.AreEqual(
                    "F1 M0,0 L10,0 L20,5 L30,10 C40,0 50,20 60,10 C70,0 80,20 90,10 " +
                    "Q100,0 110,10 Q120,0 130,10 A5,7.5 30 1 1 140,10 Z",
                    GeometryMiniLanguageWriter.Write(geometry));
            });
        }

        [Test]
        public void TheWrittenGeometryTextIsAFixedPoint()
        {
            WinUITestHost.Run(() => {
                // WinUI's parser expands coordinate runs into individual segments, so the written
                // text must survive being read back and rewritten unchanged - otherwise every save
                // rewrites the file even when nothing was edited.
                var text = GeometryMiniLanguageWriter.Write(BuildAllSegmentGeometry());
                var reloaded = (PathGeometry)XamlValueConverter.TryCreate(typeof(Geometry))
                    .ConvertFrom(null, CultureInfo.InvariantCulture, text);
                Assert.AreEqual(text, GeometryMiniLanguageWriter.Write(reloaded));
            });
        }

        [Test]
        public void WritesTheEvenOddFillRuleAndOpenFigures()
        {
            WinUITestHost.Run(() => {
                var figure = new PathFigure { StartPoint = new Point(1.5, 2.5) };
                figure.Segments.Add(new LineSegment { Point = new Point(3, 4) });
                var geometry = new PathGeometry();      // FillRule defaults to EvenOdd
                geometry.Figures.Add(figure);
                Assert.AreEqual("F0 M1.5,2.5 L3,4", GeometryMiniLanguageWriter.Write(geometry));
            });
        }

        [Test]
        public void WritesEveryFigureOfAMultiFigureGeometry()
        {
            WinUITestHost.Run(() => {
                var geometry = new PathGeometry();
                var first = new PathFigure { StartPoint = new Point(0, 0), IsClosed = true };
                first.Segments.Add(new LineSegment { Point = new Point(10, 0) });
                var second = new PathFigure { StartPoint = new Point(20, 20) };
                second.Segments.Add(new LineSegment { Point = new Point(30, 20) });
                geometry.Figures.Add(first);
                geometry.Figures.Add(second);
                Assert.AreEqual("F0 M0,0 L10,0 Z M20,20 L30,20",
                    GeometryMiniLanguageWriter.Write(geometry));
            });
        }

        [Test]
        public void RefusesGeometryTheMiniLanguageCannotExpress()
        {
            WinUITestHost.Run(() => {
                Assert.IsFalse(GeometryMiniLanguageWriter.CanWrite(new RectangleGeometry()),
                    "RectangleGeometry has no mini-language form and must not be rewritten as a path");
                Assert.IsFalse(GeometryMiniLanguageWriter.CanWrite(new EllipseGeometry()));
                Assert.IsFalse(GeometryMiniLanguageWriter.CanWrite(new LineGeometry()));
                Assert.IsFalse(GeometryMiniLanguageWriter.CanWrite(new GeometryGroup()));

                var transformed = BuildAllSegmentGeometry();
                transformed.Transform = new TranslateTransform { X = 5 };
                Assert.IsFalse(GeometryMiniLanguageWriter.CanWrite(transformed),
                    "A Transform cannot be expressed in the mini-language");

                var unfilled = new PathGeometry();
                unfilled.Figures.Add(new PathFigure { StartPoint = new Point(0, 0), IsFilled = false });
                Assert.IsFalse(GeometryMiniLanguageWriter.CanWrite(unfilled),
                    "IsFilled=false has no mini-language form");
            });
        }

        [Test]
        public void RefusesPolySegmentsWhoseCountIsNotAMultipleOfTheirCommandArity()
        {
            WinUITestHost.Run(() => {
                var badBezierPoints = new PointCollection();
                badBezierPoints.Add(new Point(0, 0));
                badBezierPoints.Add(new Point(10, 0));
                badBezierPoints.Add(new Point(20, 10));
                badBezierPoints.Add(new Point(30, 10));
                var badBezierFigure = new PathFigure { StartPoint = new Point(0, 0) };
                badBezierFigure.Segments.Add(new PolyBezierSegment { Points = badBezierPoints });
                var badBezierGeometry = new PathGeometry();
                badBezierGeometry.Figures.Add(badBezierFigure);
                Assert.IsFalse(GeometryMiniLanguageWriter.CanWrite(badBezierGeometry),
                    "A PolyBezierSegment with a point count that isn't a multiple of 3 would " +
                    "silently drop its trailing points");

                var badQuadPoints = new PointCollection();
                badQuadPoints.Add(new Point(0, 0));
                badQuadPoints.Add(new Point(10, 0));
                badQuadPoints.Add(new Point(20, 10));
                var badQuadFigure = new PathFigure { StartPoint = new Point(0, 0) };
                badQuadFigure.Segments.Add(new PolyQuadraticBezierSegment { Points = badQuadPoints });
                var badQuadGeometry = new PathGeometry();
                badQuadGeometry.Figures.Add(badQuadFigure);
                Assert.IsFalse(GeometryMiniLanguageWriter.CanWrite(badQuadGeometry),
                    "A PolyQuadraticBezierSegment with a point count that isn't a multiple of 2 " +
                    "would silently drop its trailing point");
            });
        }

        static string Format(Type declaredType, object value)
        {
            Assert.IsTrue(XamlValueFormatter.HasFormatter(declaredType),
                "No formatter for " + declaredType.FullName);
            Assert.IsTrue(XamlValueFormatter.CanFormat(declaredType, value),
                "The formatter refused a value it should accept: " + value);
            return XamlValueFormatter.Format(declaredType, value);
        }

        static object ReadBack(Type declaredType, string text)
        {
            return XamlValueConverter.TryCreate(declaredType)
                .ConvertFrom(null, CultureInfo.InvariantCulture, text);
        }

        [Test]
        public void FormatsEveryValueTypeInTheTableAndReadsItBackEqual()
        {
            WinUITestHost.Run(() => {
                var cases = new object[,] {
                    { typeof(Microsoft.UI.Xaml.Thickness), new Microsoft.UI.Xaml.Thickness(1, 2.5, 3, 4), "1,2.5,3,4" },
                    { typeof(Microsoft.UI.Xaml.CornerRadius), new Microsoft.UI.Xaml.CornerRadius(1, 2, 3, 4), "1,2,3,4" },
                    { typeof(Microsoft.UI.Xaml.GridLength), new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Auto), "Auto" },
                    { typeof(Microsoft.UI.Xaml.GridLength), new Microsoft.UI.Xaml.GridLength(40), "40" },
                    { typeof(Microsoft.UI.Xaml.GridLength), new Microsoft.UI.Xaml.GridLength(2, Microsoft.UI.Xaml.GridUnitType.Star), "2*" },
                    { typeof(Windows.UI.Color), Microsoft.UI.Colors.Blue, "#FF0000FF" },
                    { typeof(Windows.UI.Text.FontWeight), Microsoft.UI.Text.FontWeights.Bold, "Bold" },
                    { typeof(Point), new Point(1.5, 2.5), "1.5,2.5" },
                    { typeof(Rect), new Rect(1, 2, 3.5, 4), "1,2,3.5,4" },
                    { typeof(Size), new Size(3.5, 4), "3.5,4" },
                    { typeof(Microsoft.UI.Xaml.Duration), new Microsoft.UI.Xaml.Duration(TimeSpan.FromSeconds(1)), "00:00:01" },
                    { typeof(Microsoft.UI.Xaml.Duration), Microsoft.UI.Xaml.Duration.Forever, "Forever" },
                    { typeof(Microsoft.UI.Xaml.Duration), Microsoft.UI.Xaml.Duration.Automatic, "Automatic" },
                    { typeof(Matrix), new Matrix(1, 2, 3, 4, 5.5, 6), "1,2,3,4,5.5,6" },
                    { typeof(System.Numerics.Vector3), new System.Numerics.Vector3(1, 2.5f, 3), "1,2.5,3" },
                };

                for (int i = 0; i < cases.GetLength(0); i++) {
                    var declared = (Type)cases[i, 0];
                    var value = cases[i, 1];
                    var expected = (string)cases[i, 2];
                    var text = Format(declared, value);
                    Assert.AreEqual(expected, text, "Wrong text for " + declared.Name + " " + value);
                    Assert.AreEqual(value, ReadBack(declared, text),
                        "Round trip changed the value for " + declared.Name);
                }
            });
        }

        [Test]
        public void FormatsEveryReferenceTypeInTheTableAndReadsItBackWithTheSameContent()
        {
            WinUITestHost.Run(() => {
                // None of these five implements value equality, so a correct round trip still
                // compares unequal under Equals. Assert on content.
                var family = new FontFamily("Segoe UI");
                Assert.AreEqual("Segoe UI", Format(typeof(FontFamily), family));
                Assert.AreEqual("Segoe UI", ((FontFamily)ReadBack(typeof(FontFamily), "Segoe UI")).Source);

                var points = new PointCollection();
                points.Add(new Point(1, 2));
                points.Add(new Point(3.5, 4));
                Assert.AreEqual("1,2 3.5,4", Format(typeof(PointCollection), points));
                var readPoints = (PointCollection)ReadBack(typeof(PointCollection), "1,2 3.5,4");
                Assert.AreEqual(2, readPoints.Count);
                Assert.AreEqual(3.5, readPoints[1].X);

                var doubles = new DoubleCollection();
                doubles.Add(1);
                doubles.Add(2.5);
                doubles.Add(3);
                Assert.AreEqual("1,2.5,3", Format(typeof(DoubleCollection), doubles));
                var readDoubles = (DoubleCollection)ReadBack(typeof(DoubleCollection), "1,2.5,3");
                Assert.AreEqual(3, readDoubles.Count);
                Assert.AreEqual(2.5, readDoubles[1]);

                var brush = new SolidColorBrush(Microsoft.UI.Colors.Blue);
                Assert.AreEqual("Blue", Format(typeof(Brush), brush),
                    "A named colour must stay named, as XamlDocument's own dictionary emits it");
                Assert.AreEqual(Microsoft.UI.Colors.Blue,
                    ((SolidColorBrush)ReadBack(typeof(Brush), "Blue")).Color);

                var unnamed = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 1, 2, 3));
                Assert.AreEqual("#FF010203", Format(typeof(Brush), unnamed));

                var geometry = BuildAllSegmentGeometry();
                Assert.AreEqual(GeometryMiniLanguageWriter.Write(geometry),
                    Format(typeof(Geometry), geometry));
            });
        }

        [Test]
        public void FormatsTheSameUnderACommaDecimalCulture()
        {
            WinUITestHost.Run(() => {
                var previous = CultureInfo.CurrentCulture;
                try {
                    CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                    // Point.ToString() yields '1,5;2,5' under this culture, which is why no
                    // formatter is allowed to reach ToString().
                    Assert.AreEqual("1.5,2.5", Format(typeof(Point), new Point(1.5, 2.5)));
                    Assert.AreEqual("1,2.5,3,4",
                        Format(typeof(Microsoft.UI.Xaml.Thickness), new Microsoft.UI.Xaml.Thickness(1, 2.5, 3, 4)));
                    Assert.AreEqual("1,2,3.5,4", Format(typeof(Rect), new Rect(1, 2, 3.5, 4)));
                    Assert.AreEqual("00:00:01",
                        Format(typeof(Microsoft.UI.Xaml.Duration), new Microsoft.UI.Xaml.Duration(TimeSpan.FromSeconds(1))));
                    Assert.AreEqual("1,2.5,3",
                        Format(typeof(System.Numerics.Vector3), new System.Numerics.Vector3(1, 2.5f, 3)));

                    var doubles = new DoubleCollection();
                    doubles.Add(2.5);
                    Assert.AreEqual("2.5", Format(typeof(DoubleCollection), doubles));
                } finally {
                    CultureInfo.CurrentCulture = previous;
                }
            });
        }

        [Test]
        public void RefusesInstancesItCannotFormatFaithfully()
        {
            WinUITestHost.Run(() => {
                Assert.IsTrue(XamlValueFormatter.HasFormatter(typeof(Brush)),
                    "The type-level answer stays true so the PropertyGrid text box is enabled");
                Assert.IsFalse(XamlValueFormatter.CanFormat(typeof(Brush), new LinearGradientBrush()),
                    "A gradient brush has no attribute form");
                Assert.IsFalse(XamlValueFormatter.CanFormat(typeof(Geometry), new RectangleGeometry()));

                // A live FontFamily with an empty Source cannot exist in this WinUI SDK to begin
                // with: both `new FontFamily("")` and reading "" back through Phase A's own
                // converter (which goes through XamlReader.Load, not this constructor) throw -
                // measured. So CanFormat's own !string.IsNullOrEmpty(Source) guard can never be
                // exercised by a real instance; this asserts the platform fact instead of a
                // scenario that cannot be constructed.
                Assert.Throws<ArgumentException>(() => new FontFamily(""),
                    "An empty family name has no live FontFamily form to guard against");

                Assert.IsFalse(
                    XamlValueFormatter.CanFormat(typeof(Windows.UI.Text.FontWeight),
                        new Windows.UI.Text.FontWeight { Weight = 137 }),
                    "137 has no name, and Phase A cannot read the numeric form back");
                Assert.IsFalse(XamlValueFormatter.CanFormat(typeof(Point), null),
                    "A null value has no text form");
            });
        }

        [Test]
        public void HasNoFormatterForTypesOutsideTheTable()
        {
            WinUITestHost.Run(() => {
                Assert.IsFalse(XamlValueFormatter.HasFormatter(typeof(object)));
                Assert.IsFalse(XamlValueFormatter.HasFormatter(typeof(System.Numerics.Vector2)),
                    "Vector2 has no host entry either, so it could not be round-trip tested");
                Assert.IsFalse(XamlValueFormatter.HasFormatter(typeof(Microsoft.UI.Xaml.Controls.Button)));
            });
        }
    }
}
