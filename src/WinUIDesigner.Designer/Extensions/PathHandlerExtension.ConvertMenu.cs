using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using WinUIDesigner.DesignModel;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Task 11's PathHandlerExtension, completed with the reference's per-point
    /// convert-segment context menu (reference PathHandlerExtension.cs:167-324). This is a second
    /// `partial` piece of the SAME class rather than a reopening of Task 11's file, which is why
    /// that class carries `partial`.
    ///
    /// EVERYTHING HERE OPERATES ON <see cref="_workingGeometry"/>, NEVER ON THE LIVE
    /// ((Path)ExtendedItem.View).Data. Task 11 made _workingGeometry a deep clone of Data that is
    /// deliberately never itself assigned to Data (see its own remarks), so every PathPoint's
    /// ParentFigure/SegmentOrFigure is an object living inside _workingGeometry and can never be
    /// ReferenceEquals to anything reachable from the live Data graph. Re-fetching Data here and
    /// searching it for a PathPoint's figure therefore cannot ever match - confirmed empirically
    /// before this file was written, see the report for the run: the brief's literal
    /// CommitFiguresChange (which did exactly that) died with
    /// System.Runtime.InteropServices.COMException "No installed components were detected." at
    /// PathFigureCollection.Add, thrown from its own `if (!ReferenceEquals(f, targetFigure))`
    /// non-matching branch, because that branch re-adds a PathFigure that already belongs to the
    /// live Data's own PathFigureCollection - the same already-parented-collection-element
    /// constraint Task 11's SaveGeometry remarks document.</summary>
    public sealed partial class PathHandlerExtension
    {
        internal enum PathPartConvertType { ToLineSegment, ToBezierSegment, ToQuadricBezierSegment, ToArcSegment, InsertPoint }

        /// <summary>The flyout for one PathThumb, or null when this point has nothing to offer -
        /// the reference returns null for exactly the same case (its BuildMenu ends with
        /// `if (!menuList.Any()) return null;`, reference:210-211), and a null ContextFlyout is
        /// what makes right-clicking a Bezier control point do nothing at all instead of popping an
        /// empty menu.</summary>
        internal MenuFlyout BuildMenu(PathPoint point)
        {
            var flyout = new MenuFlyout();

            // Only a real anchor point (not a Bezier/Arc control point, TargetPathPoint == null)
            // gets anything - mirrors the reference's own BuildMenu gate (reference:172).
            if (point.TargetPathPoint == null) {
                if (point.SegmentOrFigure is PathSegment segment) {
                    AddConvertItem(flyout, "Insert Point", point, segment, PathPartConvertType.InsertPoint);
                    AddConvertItem(flyout, "Convert to Line", point, segment, PathPartConvertType.ToLineSegment);
                    AddConvertItem(flyout, "Convert to Bezier", point, segment, PathPartConvertType.ToBezierSegment);
                    AddConvertItem(flyout, "Convert to Quadratic Bezier", point, segment, PathPartConvertType.ToQuadricBezierSegment);
                    AddConvertItem(flyout, "Convert to Arc", point, segment, PathPartConvertType.ToArcSegment);
                }

                // Reference gates "is Closed" on `pathpoint.Object is PathFigure` (reference:204),
                // i.e. the figure's own start-point thumb only; the brief instead hung it off every
                // segment anchor via ParentFigure. Doing it for every anchor point covers both:
                // the start-point thumb (whose SegmentOrFigure IS its ParentFigure, see
                // AddFigurePoints, PathHandlerExtension.cs:182) keeps the reference's item instead
                // of being left with a null/empty menu, and segment anchors keep the brief's.
                var closedItem = new ToggleMenuFlyoutItem { Text = "Is Closed", IsChecked = point.ParentFigure.IsClosed };
                closedItem.Click += (s, e) => ChangeIsClosed(point.ParentFigure);
                flyout.Items.Add(closedItem);
            }

            // NOT PORTED, and not portable: the reference's "is Stroked" toggle (reference:189-191)
            // and "is Smooth Join" display item (reference:192-193). Both read
            // System.Windows.Media.PathSegment.IsStroked/.IsSmoothJoin, and
            // Microsoft.UI.Xaml.Media.PathSegment has NEITHER - it declares no properties of its
            // own at all. Compiler-confirmed (`error CS1061: 'PathSegment' does not contain a
            // definition for 'IsStroked'`) and then confirmed a second time by reflection over the
            // live WinRT type, which lists only the two DependencyObject-inherited members
            // (Dispatcher, DispatcherQueue). IsStroked has no per-segment equivalent anywhere in
            // WinUI's Path model, so there is nothing to toggle - the item is dropped rather than
            // stubbed. Same family as root CLAUDE.md's known WinUI platform gaps.
            //
            // Also not ported (spec decision 5, the reference's OWN incompleteness): "is large
            // Arc" / "Rotation Angle" / "Clockwise SweepDirection" (reference:195-202). Those three
            // only ever set IsChecked in the reference, with no Click handler wired at all, so the
            // reference cannot change them either. Omitting them entirely states that more honestly
            // than shipping three display-only items that do nothing.
            return flyout.Items.Count == 0 ? null : flyout;
        }

        void AddConvertItem(MenuFlyout flyout, string text, PathPoint point, PathSegment segment, PathPartConvertType type)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += (s, e) => ConvertPart(point, segment, type);
            flyout.Items.Add(item);
        }

        internal void ConvertPart(PathPoint point, PathSegment segment, PathPartConvertType convertType)
        {
            if (_workingGeometry == null) return;
            var figure = point.ParentFigure;
            if (figure == null || figure.Segments == null) return;

            // Identity search inside _workingGeometry's own figure - see the class remarks for why
            // this is the only collection the search can possibly succeed against.
            int idx = -1;
            for (int i = 0; i < figure.Segments.Count; i++) if (ReferenceEquals(figure.Segments[i], segment)) { idx = i; break; }
            if (idx < 0) return;

            if (segment is PolyBezierSegment || segment is PolyQuadraticBezierSegment) {
                // Reference's own upstream incompleteness (spec decision 5): both branches are an
                // empty //TODO in the reference itself (reference:273-277). Matched here - not
                // completed. Deliberately placed BEFORE PlacementOperation.Start so the no-op
                // leaks no uncommitted operation and adds no undo entry.
                return;
            }

            var anchorPoint = point.Point;
            // KNOWN DEGRADATION vs the reference (carried over from the brief, flagged rather than
            // silently kept): the reference's midp is the midpoint between the PREVIOUS anchor and
            // this point (reference:281, via PathPoint.ParentPathPoint). Task 11's PathPoint has no
            // ParentPathPoint - only TargetPathPoint, which is the control-point -> anchor link -
            // so for every point BuildMenu actually offers a menu on (TargetPathPoint == null, by
            // the gate above) midPoint collapses to anchorPoint. "Insert Point" therefore inserts a
            // coincident, immediately draggable point rather than one halfway back along the
            // segment. Fixing it properly means adding a ParentPathPoint/previous-anchor link to
            // Task 11's PathPoint, which is outside this task's one-line change to that file.
            var midPoint = point.TargetPathPoint != null
                ? new Point((point.TargetPathPoint.Point.X + anchorPoint.X) / 2, (point.TargetPathPoint.Point.Y + anchorPoint.Y) / 2)
                : anchorPoint;

            // A PolyLineSegment holds many points in one segment, so converting ONE of them means
            // splitting the segment first - reference:258-272 does exactly this. Without the split
            // the generic path below would replace the whole PolyLineSegment with a single
            // Line/Bezier/Arc segment and silently drop every other point in it.
            var prefix = new List<PathSegment>();
            var suffix = new List<PathSegment>();
            var target = segment;
            if (segment is PolyLineSegment poly && point.PolyLineIndex >= 0 && point.PolyLineIndex < poly.Points.Count) {
                int k = point.PolyLineIndex;
                prefix.Add(new PolyLineSegment { Points = PointsRange(poly.Points, 0, k) });
                target = new LineSegment { Point = poly.Points[k] };
                suffix.Add(new PolyLineSegment { Points = PointsRange(poly.Points, k + 1, poly.Points.Count - k - 1) });
            }

            var replacements = new List<PathSegment>(prefix);
            if (convertType == PathPartConvertType.InsertPoint) {
                // Reference order (reference:296 then :304 both Insert at idx, so the NEW segment
                // ends up first): previous anchor -> midPoint -> this anchor. The brief had the two
                // the other way round, which back-tracks along the path whenever midPoint is a real
                // midpoint. Identical in practice today - see the midPoint note above.
                replacements.Add(new LineSegment { Point = midPoint });
                replacements.Add(target);
            } else {
                switch (convertType) {
                    case PathPartConvertType.ToBezierSegment:
                        replacements.Add(new BezierSegment {
                            Point1 = new Point(midPoint.X - 40, midPoint.Y - 40),
                            Point2 = new Point(midPoint.X - 40, midPoint.Y + 40),
                            Point3 = anchorPoint
                        });
                        break;
                    case PathPartConvertType.ToQuadricBezierSegment:
                        replacements.Add(new QuadraticBezierSegment {
                            Point1 = new Point(anchorPoint.X - 40, anchorPoint.Y - 40),
                            Point2 = anchorPoint
                        });
                        break;
                    case PathPartConvertType.ToArcSegment:
                        replacements.Add(new ArcSegment { Point = anchorPoint, Size = new Size(20, 20) });
                        break;
                    default:
                        replacements.Add(new LineSegment { Point = anchorPoint });
                        break;
                }
            }
            replacements.AddRange(suffix);

            var operation = PlacementOperation.Start(extendedItemArray, PlacementType.MovePoint);
            CommitFiguresChange(figure, idx, replacements, operation);
        }

        static PointCollection PointsRange(PointCollection source, int start, int count)
        {
            var range = new PointCollection();
            for (int i = start; i < start + count; i++) range.Add(source[i]);
            return range;
        }

        /// <summary>Swaps <paramref name="replacements"/> in for the segment at
        /// <paramref name="segmentIndex"/> of a _workingGeometry-owned figure, publishes the result
        /// and rebuilds the thumbs.
        ///
        /// The mutation is done in place on _workingGeometry rather than by assembling a new
        /// PathGeometry out of the existing figures/segments: those objects already belong to a
        /// PathFigureCollection/PathSegmentCollection, and re-adding an already-parented one throws
        /// COMException 0x800F1000 (Task 11's SaveGeometry remarks; reproduced again while writing
        /// this file). Removing first and re-inserting into the SAME collection is fine, which is
        /// what makes InsertPoint's "keep the original segment too" work.
        ///
        /// SaveGeometry then publishes a fresh deep clone, so Data still never aliases
        /// _workingGeometry and the undo entry still records a genuinely untouched "old" value.
        /// ReapplyExtension is what rebuilds the thumbs against the new segment list: SaveGeometry
        /// suppresses OnExtendedItemPropertyChanged's own rebuild via _publishingGeometry (that
        /// guard exists so a drag isn't torn down mid-gesture), so nothing else would.</summary>
        void CommitFiguresChange(PathFigure targetFigure, int segmentIndex, IEnumerable<PathSegment> replacements, PlacementOperation operation)
        {
            var segments = targetFigure.Segments;
            segments.RemoveAt(segmentIndex);
            int at = segmentIndex;
            foreach (var replacement in replacements) segments.Insert(at++, replacement);

            SaveGeometry(_workingGeometry);
            operation.Commit();
            ExtendedItem.ReapplyExtension(typeof(PathHandlerExtension));
        }

        /// <summary>The figure handed in is a _workingGeometry-owned PathFigure (that is the only
        /// kind a PathPoint ever references), so the flip below lands on the right object and
        /// SaveGeometry(_workingGeometry) is what publishes it. Publishing
        /// ((Path)ExtendedItem.View).Data instead - as the brief did - would have re-published the
        /// live, untouched geometry and silently thrown the toggle away with no exception to show
        /// for it. No ReapplyExtension: IsClosed changes no point, so the thumbs stay valid.</summary>
        internal void ChangeIsClosed(PathFigure figure)
        {
            if (_workingGeometry == null || figure == null) return;
            var operation = PlacementOperation.Start(extendedItemArray, PlacementType.MovePoint);
            figure.IsClosed = !figure.IsClosed;
            SaveGeometry(_workingGeometry);
            operation.Commit();
        }
    }
}
