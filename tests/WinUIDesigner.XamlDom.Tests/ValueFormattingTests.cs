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
    }
}
