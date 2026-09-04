using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.UI.Xaml.Media;
using NUnit.Framework;
using Windows.Foundation;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class PathHandlerExtensionConvertMenuTests
    {
        // NOTE: no <Path.Data> in the XAML, deliberately - the same workaround Tasks 9 and 11 both
        // landed on, and re-confirmed empirically for this task's own fixture before the change.
        // The brief's fixture used <PathFigure StartPoint="10,10"><LineSegment Point="50,10" />,
        // and those Windows.Foundation.Point-typed XAML attributes silently parse to (0,0) in this
        // port: XamlPropertyInfo resolves converters through TypeDescriptor.GetConverter(ReturnType)
        // (XamlPropertyInfo.cs:177), Windows.Foundation.Point carries no TypeConverterAttribute, so
        // the base TypeConverter's ConvertFromString throws NotSupportedException, which
        // XamlProperty.UpdateValueOnInstance swallows into a Debug.WriteLine (XamlProperty.cs:269-271).
        // Measured with a throwaway probe over the brief's exact fixture string:
        //   PROBE StartPoint=0,0  LineSegment.Point=0,0
        // Every fixture below therefore builds its PathGeometry as a real CLR object graph and hands
        // it to Properties["Data"].SetValue - the same typed-object path SaveGeometry itself uses.
        const string PathInCanvasXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\">" +
            "<Path x:Name=\"P\" Stroke=\"Black\" StrokeThickness=\"2\" />" +
            "</Canvas>";

        static (DesignSurface surface, Microsoft.UI.Xaml.Shapes.Path path, PathHandlerExtension extension) Load(
            Microsoft.UI.Xaml.Window window, PathGeometry data)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(PathInCanvasXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();
            var path = (Microsoft.UI.Xaml.Shapes.Path)((Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component).Children[0];
            var pathItem = surface.DesignContext.Services.Component.GetDesignItem(path);

            // Data must be in place BEFORE selection: OnlyOneItemSelectedExtensionServer attaches
            // PathHandlerExtension on selection, and its OnInitialized clones whatever Data holds
            // at that moment into _workingGeometry.
            pathItem.Properties["Data"].SetValue(data);

            surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { pathItem }, SelectionTypes.Primary);
            surface.UpdateLayout();
            var extension = pathItem.Extensions.OfType<PathHandlerExtension>().Single();
            return (surface, path, extension);
        }

        static PathGeometry Geometry(params PathSegment[] segments)
        {
            var collection = new PathSegmentCollection();
            foreach (var segment in segments) collection.Add(segment);
            var figures = new PathFigureCollection();
            figures.Add(new PathFigure { StartPoint = new Point(10, 10), Segments = collection });
            return new PathGeometry { Figures = figures };
        }

        /// <summary>StartPoint (10,10) + one LineSegment to (50,10). Two PathPoints, two thumbs:
        /// [0] the figure's start point, [1] the LineSegment's own anchor.</summary>
        static PathGeometry OneLineSegment() => Geometry(new LineSegment { Point = new Point(50, 10) });

        static PathFigure LiveFigure(Microsoft.UI.Xaml.Shapes.Path path) => ((PathGeometry)path.Data).Figures[0];

        [Test]
        public void ConvertToArcReplacesTheLineSegmentWithAnArcSegmentAtTheSamePointAndCommitsOneUndoEntry()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, path, extension) = Load(window, OneLineSegment());
                var undo = surface.DesignContext.Services.GetService<UndoService>();
                int before = undo.UndoActions.Count();
                var anchorThumb = extension.PointThumbs[1]; // the LineSegment's own anchor point (50,10)
                var lineSegment = (LineSegment)anchorThumb.AssociatedPoint.SegmentOrFigure;

                extension.ConvertPart(anchorThumb.AssociatedPoint, lineSegment, PathHandlerExtension.PathPartConvertType.ToArcSegment);
                surface.UpdateLayout();

                // Read back the LIVE, published Data - not the extension's working copy. This is
                // the assertion the brief's literal CommitFiguresChange could never reach: it
                // searched ((Path)View).Data for a _workingGeometry-owned figure, never matched,
                // and died in its non-matching branch with COMException "No installed components
                // were detected." from PathFigureCollection.Add.
                var figure = LiveFigure(path);
                Assert.AreEqual(1, figure.Segments.Count, "the LineSegment is replaced, not appended to");
                Assert.IsInstanceOf<ArcSegment>(figure.Segments[0]);
                var arc = (ArcSegment)figure.Segments[0];
                Assert.AreEqual(50, arc.Point.X, 0.5);
                Assert.AreEqual(10, arc.Point.Y, 0.5);
                Assert.AreEqual(20, arc.Size.Width, 0.5);
                Assert.AreEqual(20, arc.Size.Height, 0.5);
                Assert.AreEqual(before + 1, undo.UndoActions.Count());
            });
        }

        [Test]
        public void ConvertToBezierPutsABezierSegmentInPlaceAndOneUndoRestoresTheOriginalLineSegment()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, path, extension) = Load(window, OneLineSegment());
                var undo = surface.DesignContext.Services.GetService<UndoService>();
                var anchorThumb = extension.PointThumbs[1];
                var lineSegment = (LineSegment)anchorThumb.AssociatedPoint.SegmentOrFigure;

                extension.ConvertPart(anchorThumb.AssociatedPoint, lineSegment, PathHandlerExtension.PathPartConvertType.ToBezierSegment);
                surface.UpdateLayout();

                var bezier = (BezierSegment)LiveFigure(path).Segments[0];
                Assert.AreEqual(10, bezier.Point1.X, 0.5, "Point1 = midPoint - (40,40), midPoint == the anchor here");
                Assert.AreEqual(-30, bezier.Point1.Y, 0.5);
                Assert.AreEqual(10, bezier.Point2.X, 0.5);
                Assert.AreEqual(50, bezier.Point2.Y, 0.5);
                Assert.AreEqual(50, bezier.Point3.X, 0.5, "the anchor point is preserved");
                Assert.AreEqual(10, bezier.Point3.Y, 0.5);

                undo.Undo();
                surface.UpdateLayout();
                var restored = LiveFigure(path).Segments[0];
                Assert.IsInstanceOf<LineSegment>(restored, "one undo reverts the whole convert");
                Assert.AreEqual(50, ((LineSegment)restored).Point.X, 0.5);
                Assert.AreEqual(10, ((LineSegment)restored).Point.Y, 0.5);
            });
        }

        [Test]
        public void InsertPointAddsASegmentBeforeTheConvertedOneAndKeepsTheOriginal()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, path, extension) = Load(window, OneLineSegment());
                var anchorThumb = extension.PointThumbs[1];
                var lineSegment = (LineSegment)anchorThumb.AssociatedPoint.SegmentOrFigure;

                extension.ConvertPart(anchorThumb.AssociatedPoint, lineSegment, PathHandlerExtension.PathPartConvertType.InsertPoint);
                surface.UpdateLayout();

                var figure = LiveFigure(path);
                Assert.AreEqual(2, figure.Segments.Count, "the original segment survives, one new one is inserted");
                // Both land on (50,10): midPoint collapses to the anchor because Task 11's PathPoint
                // has no previous-anchor link (see ConvertPart's KNOWN DEGRADATION note). The point
                // this asserts is the segment COUNT and that neither segment was dropped.
                Assert.AreEqual(50, ((LineSegment)figure.Segments[0]).Point.X, 0.5);
                Assert.AreEqual(50, ((LineSegment)figure.Segments[1]).Point.X, 0.5);
                // The rebuild really happened: three points now (start + two anchors), and the
                // extension instance is a fresh one after ReapplyExtension.
                var rebuilt = surface.DesignContext.Services.Component.GetDesignItem(path).Extensions.OfType<PathHandlerExtension>().Single();
                Assert.AreEqual(3, rebuilt.PointThumbs.Count);
            });
        }

        [Test]
        public void ConvertingOnePointOfAPolyLineSegmentSplitsItInsteadOfDroppingItsOtherPoints()
        {
            // Reference:258-272 splits a PolyLineSegment around the converted point. Without that
            // split the generic replace-the-whole-segment path would turn a 3-point PolyLineSegment
            // into a single ArcSegment and silently lose the other two points.
            WinUITestHost.RunWithWindow(window => {
                var points = new PointCollection();
                points.Add(new Point(50, 10));
                points.Add(new Point(50, 50));
                points.Add(new Point(10, 50));
                var (surface, path, extension) = Load(window, Geometry(new PolyLineSegment { Points = points }));

                // Thumbs: [0] start point, [1..3] the three PolyLineSegment points.
                var middleThumb = extension.PointThumbs[2];
                Assert.AreEqual(1, middleThumb.AssociatedPoint.PolyLineIndex, "sanity: thumb 2 is the poly's index-1 point");
                var polySegment = (PolyLineSegment)middleThumb.AssociatedPoint.SegmentOrFigure;

                extension.ConvertPart(middleThumb.AssociatedPoint, polySegment, PathHandlerExtension.PathPartConvertType.ToArcSegment);
                surface.UpdateLayout();

                var figure = LiveFigure(path);
                Assert.AreEqual(3, figure.Segments.Count, "split into prefix poly + converted segment + suffix poly");
                var prefix = (PolyLineSegment)figure.Segments[0];
                Assert.AreEqual(1, prefix.Points.Count);
                Assert.AreEqual(50, prefix.Points[0].X, 0.5);
                Assert.AreEqual(10, prefix.Points[0].Y, 0.5);
                var arc = (ArcSegment)figure.Segments[1];
                Assert.AreEqual(50, arc.Point.X, 0.5, "the converted point keeps its own position");
                Assert.AreEqual(50, arc.Point.Y, 0.5);
                var suffix = (PolyLineSegment)figure.Segments[2];
                Assert.AreEqual(1, suffix.Points.Count, "the trailing point is NOT dropped");
                Assert.AreEqual(10, suffix.Points[0].X, 0.5);
                Assert.AreEqual(50, suffix.Points[0].Y, 0.5);
            });
        }

        [Test]
        public void ConvertingAPolyBezierSegmentIsANoOpMatchingTheReferencesOwnIncompleteness()
        {
            // The PolyBezierSegment must be in the INITIAL Data, so that GetPoints' clone puts THAT
            // instance inside _workingGeometry and the thumb's own AssociatedPoint really points at
            // it. The brief's version instead swapped a PolyBezierSegment into the live Path.Data
            // after load, which _workingGeometry never sees - ConvertPart's identity search would
            // then never find it and the method would return via the `idx < 0` guard, passing the
            // test without ever reaching the no-op branch it claims to cover.
            WinUITestHost.RunWithWindow(window => {
                var points = new PointCollection();
                points.Add(new Point(20, 20)); // control 1
                points.Add(new Point(30, 20)); // control 2
                points.Add(new Point(40, 10)); // anchor
                var (surface, path, extension) = Load(window, Geometry(new PolyBezierSegment { Points = points }));
                var undo = surface.DesignContext.Services.GetService<UndoService>();
                int before = undo.UndoActions.Count();

                // AddFigurePoints emits c1, c2, anchor per triple (PathHandlerExtension.cs:211-220),
                // after the figure's start point - so thumb 3 is the triple's anchor, the only one
                // of the three with TargetPathPoint == null.
                var anchorThumb = extension.PointThumbs[3];
                Assert.IsNull(anchorThumb.AssociatedPoint.TargetPathPoint, "sanity: thumb 3 is the anchor, not a control point");
                var polyBezier = (PolyBezierSegment)anchorThumb.AssociatedPoint.SegmentOrFigure;
                Assert.AreSame(polyBezier, extension.PointThumbs[1].AssociatedPoint.SegmentOrFigure,
                               "sanity: the whole triple belongs to the one _workingGeometry-owned PolyBezierSegment");

                Assert.DoesNotThrow(() => extension.ConvertPart(anchorThumb.AssociatedPoint, polyBezier, PathHandlerExtension.PathPartConvertType.ToArcSegment));
                surface.UpdateLayout();

                var figure = LiveFigure(path);
                Assert.AreEqual(1, figure.Segments.Count);
                Assert.IsInstanceOf<PolyBezierSegment>(figure.Segments[0], "PolyBezierSegment convert is a no-op - segment type unchanged");
                var published = (PolyBezierSegment)figure.Segments[0];
                Assert.AreEqual(3, published.Points.Count);
                Assert.AreEqual(40, published.Points[2].X, 0.5, "no point moved either");
                Assert.AreEqual(10, published.Points[2].Y, 0.5);
                Assert.AreEqual(before, undo.UndoActions.Count(), "a no-op must not leave an undo entry behind");
            });
        }

        [Test]
        public void ConvertingAPolyQuadraticBezierSegmentIsANoOpToo()
        {
            WinUITestHost.RunWithWindow(window => {
                var points = new PointCollection();
                points.Add(new Point(20, 20)); // control
                points.Add(new Point(40, 10)); // anchor
                var (surface, path, extension) = Load(window, Geometry(new PolyQuadraticBezierSegment { Points = points }));
                var undo = surface.DesignContext.Services.GetService<UndoService>();
                int before = undo.UndoActions.Count();

                var anchorThumb = extension.PointThumbs[2]; // start, c1, anchor
                Assert.IsNull(anchorThumb.AssociatedPoint.TargetPathPoint);
                var polyQuad = (PolyQuadraticBezierSegment)anchorThumb.AssociatedPoint.SegmentOrFigure;

                Assert.DoesNotThrow(() => extension.ConvertPart(anchorThumb.AssociatedPoint, polyQuad, PathHandlerExtension.PathPartConvertType.ToLineSegment));
                surface.UpdateLayout();

                Assert.IsInstanceOf<PolyQuadraticBezierSegment>(LiveFigure(path).Segments[0]);
                Assert.AreEqual(before, undo.UndoActions.Count());
            });
        }

        [Test]
        public void TogglingIsClosedPublishesTheChangeToTheLivePathDataAndUndoesInOneStep()
        {
            // The second half of the same _workingGeometry bug: the brief mutated the correct
            // (_workingGeometry-owned) figure but then published ((Path)View).Data, i.e. the live,
            // untouched geometry - so this assertion on the live figure would have failed with
            // IsClosed still false, and with no exception to notice.
            WinUITestHost.RunWithWindow(window => {
                var (surface, path, extension) = Load(window, OneLineSegment());
                var undo = surface.DesignContext.Services.GetService<UndoService>();
                int before = undo.UndoActions.Count();
                Assert.IsFalse(LiveFigure(path).IsClosed, "sanity: starts open");

                var figure = extension.PointThumbs[0].AssociatedPoint.ParentFigure;
                extension.ChangeIsClosed(figure);
                surface.UpdateLayout();

                Assert.IsTrue(LiveFigure(path).IsClosed, "the toggle must reach the published Data");
                Assert.AreEqual(before + 1, undo.UndoActions.Count());

                undo.Undo();
                surface.UpdateLayout();
                Assert.IsFalse(LiveFigure(path).IsClosed, "one undo reverts the toggle");
            });
        }

        [Test]
        public void EveryAnchorThumbGetsAConvertMenuAndControlPointThumbsGetNone()
        {
            WinUITestHost.RunWithWindow(window => {
                // Bezier fixture: start point, then c1/c2 (control points) and the anchor.
                var (_, _, extension) = Load(window, Geometry(new BezierSegment {
                    Point1 = new Point(20, 0), Point2 = new Point(40, 0), Point3 = new Point(50, 10)
                }));
                Assert.AreEqual(4, extension.PointThumbs.Count);

                foreach (var thumb in extension.PointThumbs) {
                    var flyout = (Microsoft.UI.Xaml.Controls.MenuFlyout)thumb.ContextFlyout;
                    if (thumb.AssociatedPoint.TargetPathPoint != null) {
                        Assert.IsNull(flyout, "a Bezier control point has nothing to convert - no menu at all, matching the reference's own null return");
                        continue;
                    }
                    Assert.IsNotNull(flyout, "every anchor point gets a menu");
                    var texts = flyout.Items.Select(i => i is Microsoft.UI.Xaml.Controls.MenuFlyoutItem m ? m.Text
                                                       : ((Microsoft.UI.Xaml.Controls.ToggleMenuFlyoutItem)i).Text).ToList();
                    Assert.Contains("Is Closed", texts);
                    if (thumb.AssociatedPoint.SegmentOrFigure is PathFigure) {
                        // The figure's own start point: no segment to convert, so only "Is Closed" -
                        // exactly the reference's `pathpoint.Object is PathFigure` branch.
                        Assert.AreEqual(new[] { "Is Closed" }, texts.ToArray());
                    } else {
                        Assert.AreEqual(
                            new[] { "Insert Point", "Convert to Line", "Convert to Bezier", "Convert to Quadratic Bezier", "Convert to Arc", "Is Closed" },
                            texts.ToArray());
                    }
                }
            });
        }
    }
}
