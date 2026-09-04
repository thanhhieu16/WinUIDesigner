using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.Designer.Controls;
using WinUIDesigner.Designer.Controls.Thumbs;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Post-creation point-editing for Polygon/Polyline. Ported from the reference's
    /// Extensions/PolyLineHandlerExtension.cs. No ZoomControl-scaling of drag.Delta (spec
    /// decision 11 - ZoomControl doesn't exist in this port, matching 4a/4b's own already-accepted
    /// resolution of the same gap).</summary>
    [ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]
    [ExtensionFor(typeof(Polygon), OverrideExtensions = new[] { typeof(WinUIDesigner.Designer.Extensions.ResizeThumbExtension), typeof(WinUIDesigner.Designer.Extensions.RotateThumbExtension), typeof(WinUIDesigner.Designer.Extensions.SkewThumbExtension) })]
    [ExtensionFor(typeof(Polyline), OverrideExtensions = new[] { typeof(WinUIDesigner.Designer.Extensions.ResizeThumbExtension), typeof(WinUIDesigner.Designer.Extensions.RotateThumbExtension), typeof(WinUIDesigner.Designer.Extensions.SkewThumbExtension) })]
    public sealed class PolyLineHandlerExtension : PointEditExtensionBase
    {
        internal readonly List<MultiPointThumb> PointThumbs = new List<MultiPointThumb>();

        // Deviation from the brief: exposed so tests can drive the SAME DragListener instance
        // production pointer events would (mirrors LineHandlerExtension.DragListeners, Task 7).
        // Without this, a test-constructed `new DragListener(thumb)` is a second, fully
        // independent state machine - Simulate* on it fires only ITS OWN (unsubscribed) Started/
        // Changed/Completed, never this extension's Drag_Started/Drag_Changed/Drag_Completed.
        // Confirmed empirically: with the brief's literal test issuing `new DragListener(...)`,
        // DraggingTheMiddlePointMovesOnlyThatPointAndCommitsOneUndoEntry failed (point stayed at
        // its original (50,10), undo count didn't move) even after Points was populated correctly.
        internal readonly Dictionary<MultiPointThumb, DragListener> DragListeners = new Dictionary<MultiPointThumb, DragListener>();

        MultiPointThumb _draggingThumb;
        Point _dragOriginalPoint;
        bool _insertedDuringThisDrag;

        /// <summary>True only for the duration of this extension's own
        /// ExtendedItem.Properties["Points"].SetValue call, so OnExtendedItemPropertyChanged can
        /// tell "I just published this" from "something else changed Points" and skip a rebuild
        /// that would destroy the thumb currently under the pointer. Mirrors
        /// PathHandlerExtension._publishingGeometry exactly.</summary>
        bool _publishingPoints;

        /// <summary>DesignItem.PropertyChanged is a plain INotifyPropertyChanged event carrying the
        /// model property's name - see PathHandlerExtension.DataPropertyName's own remarks for the
        /// trace - so it can be filtered rather than reacted to indiscriminately.</summary>
        const string PointsPropertyName = "Points";

        protected override IEnumerable<DesignerThumb> Thumbs => PointThumbs;

        protected override void OnInitialized()
        {
            base.OnInitialized();
            // A second, additive subscription alongside the base's own (PointEditExtensionBase
            // subscribes a private handler that only calls UpdateAdornerVisibility, never rebuilds
            // thumbs). Same shape as PathHandlerExtension.OnInitialized, which already carries this
            // fix for Path/Data - this is the missing Polygon/Polyline/Points half of it.
            ExtendedItem.PropertyChanged += OnExtendedItemPropertyChanged;
            RebuildThumbs();
        }

        protected override void OnRemove()
        {
            ExtendedItem.PropertyChanged -= OnExtendedItemPropertyChanged;
            base.OnRemove();
        }

        /// <summary>Re-reads Points and rebuilds the thumbs whenever Points changes for a reason
        /// other than this extension's own publish - Undo/Redo above all, but equally a
        /// property-grid edit or another extension writing Points.
        ///
        /// Without this, the thumb list stayed frozen at whatever OnInitialized (or the last
        /// insert/delete) left behind while Points changed underneath it: ghost thumbs outliving the
        /// points they track, and Index values pointing past the end of the collection.
        /// AdornerPanel.ArrangeOverride calls GetPlacement(element).Arrange(...) for EVERY child
        /// unconditionally, and PointTrackerPlacementSupport.Arrange reads polyline.Points[Index],
        /// so every such thumb reached that read on the next layout pass. Undoing a Shift+Alt
        /// point-insert is the shortest way to reach it.
        ///
        /// The final review predicted an ArgumentOutOfRangeException crash there; that turned out
        /// NOT to be what this SDK does - see PointTrackerPlacementSupport.Arrange's own remarks for
        /// the probe. The real symptom was a silently misplaced 7x7 handle sitting on the shape's
        /// local origin. Still a defect, just a visual one rather than a crash.
        ///
        /// The guard is _publishingPoints rather than IsResizing on purpose, for exactly the reason
        /// PathHandlerExtension.OnExtendedItemPropertyChanged documents: PlacementOperation.Abort
        /// reverts Points from inside Drag_Completed while IsResizing is still true, so an
        /// IsResizing guard would swallow the revert and leave the same staleness behind after every
        /// Escape.</summary>
        void OnExtendedItemPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != PointsPropertyName) return;
            if (_publishingPoints) return;
            RebuildThumbs();
        }

        internal void RebuildThumbs()
        {
            foreach (var thumb in PointThumbs) adornerPanel.Children.Remove(thumb);
            PointThumbs.Clear();
            // Kept in lockstep with PointThumbs so a discarded thumb (and its DragListener's
            // pointer-event subscriptions on it) isn't left referenced from here - the same leak
            // ShiftCtrlClickDeletionRemovesTheThumbFromDragListenersToo covers for DeletePoint.
            DragListeners.Clear();
            var points = GetPointCollection();
            for (int i = 0; i < points.Count; i++) PointThumbs.Add(CreateThumb(i));
            UpdateAdornerVisibility();
        }

        /// <summary>Every Points write this extension makes goes through here, so
        /// OnExtendedItemPropertyChanged can tell this extension's own publishes (which already do
        /// their own precise thumb bookkeeping, and must not tear down the thumb under the pointer
        /// mid-drag) from external ones (which must rebuild).</summary>
        void SetPoints(PointCollection next)
        {
            _publishingPoints = true;
            try {
                ExtendedItem.Properties["Points"].SetValue(next);
            } finally {
                _publishingPoints = false;
            }
        }

        PointCollection GetPointCollection()
        {
            if (ExtendedItem.View is Polygon polygon) return polygon.Points;
            return ((Polyline)ExtendedItem.View).Points;
        }

        MultiPointThumb CreateThumb(int index)
        {
            var thumb = new MultiPointThumb { Alignment = PlacementAlignment.BottomRight };
            thumb.SetDesignCursor(InputSystemCursorShape.Cross);
            var placement = Place(PlacementAlignment.BottomRight, index);
            thumb.AdornerPlacement = placement;
            thumb.Index = index;
            AdornerPanel.SetPlacement(thumb, placement);
            adornerPanel.Children.Add(thumb);

            bool dragOccurred = false;
            var drag = new DragListener(thumb);
            drag.Started += d => { dragOccurred = true; Drag_Started(thumb); };
            drag.Changed += d => Drag_Changed(d, thumb);
            drag.Completed += d => Drag_Completed(d);
            DragListeners[thumb] = drag;
            thumb.PointerReleased += (s, e) => {
                if (dragOccurred) { dragOccurred = false; return; }
                HandleThumbClick(thumb, e.KeyModifiers);
            };

            return thumb;
        }

        internal void HandleThumbClick(MultiPointThumb thumb, VirtualKeyModifiers modifiers)
        {
            bool shift = (modifiers & VirtualKeyModifiers.Shift) == VirtualKeyModifiers.Shift;
            bool ctrl = (modifiers & VirtualKeyModifiers.Control) == VirtualKeyModifiers.Control;
            if (shift && ctrl) DeletePoint(thumb);
        }

        // All three point-mutation methods below build a BRAND NEW PointCollection rather than
        // mutating the live one returned by GetPointCollection() in place - SetValue's undo
        // recording is not verified safe against "new value" being the very same, already-
        // mutated object reference the "old value" would have been, so this plan avoids that
        // risk entirely rather than assuming it's fine.
        static PointCollection Clone(PointCollection source)
        {
            var copy = new PointCollection();
            foreach (var p in source) copy.Add(p);
            return copy;
        }

        void DeletePoint(MultiPointThumb thumb)
        {
            var current = GetPointCollection();
            if (current.Count <= 1) return; // never delete down to zero points
            int index = thumb.Index;
            var next = Clone(current);
            next.RemoveAt(index);
            SetPoints(next);

            adornerPanel.Children.Remove(thumb);
            PointThumbs.RemoveAt(index);
            DragListeners.Remove(thumb);
            for (int i = index; i < PointThumbs.Count; i++) PointThumbs[i].Index = i;
            Invalidate();
        }

        void Drag_Started(MultiPointThumb thumb)
        {
            _draggingThumb = thumb;
            _dragOriginalPoint = GetPointCollection()[thumb.Index];
            _insertedDuringThisDrag = false;
            SetOperation();
        }

        void SetOperation()
        {
            if (resizeBehavior != null)
                operation = PlacementOperation.Start(extendedItemArray, PlacementType.MovePoint);
            else
                changeGroup = ExtendedItem.OpenGroup("Resize");
            IsResizing = true;
        }

        void Drag_Changed(DragListener drag, MultiPointThumb thumb)
        {
            bool shift = (drag.KeyModifiers & VirtualKeyModifiers.Shift) == VirtualKeyModifiers.Shift;
            bool alt = (drag.KeyModifiers & VirtualKeyModifiers.Menu) == VirtualKeyModifiers.Menu;

            if (shift && alt && !_insertedDuringThisDrag) {
                InsertPointDuringDrag(thumb);
                _insertedDuringThisDrag = true;
            }

            var (dx, dy) = drag.Delta;
            double newX = _dragOriginalPoint.X + dx;
            double newY = _dragOriginalPoint.Y + dy;
            if (shift && !alt) newY = _dragOriginalPoint.Y; // Shift-only: horizontal snap
            else if (alt && !shift) newX = _dragOriginalPoint.X; // Alt-only: vertical snap

            var next = Clone(GetPointCollection());
            next[thumb.Index] = new Point(System.Math.Round(newX), System.Math.Round(newY));
            SetPoints(next);
            Invalidate();
        }

        /// <summary>Duplicates the point currently being dragged, inserting the duplicate BEFORE
        /// it - the original dragging thumb (and every thumb after it) shifts one index later,
        /// so the user keeps dragging the same logical point while a new stationary vertex is
        /// left behind at its pre-drag position.</summary>
        void InsertPointDuringDrag(MultiPointThumb draggingThumb)
        {
            int insertIndex = draggingThumb.Index;
            var current = GetPointCollection();
            var duplicated = current[insertIndex];
            var next = Clone(current);
            next.Insert(insertIndex, duplicated);
            SetPoints(next);

            for (int i = insertIndex; i < PointThumbs.Count; i++) PointThumbs[i].Index = i + 1;

            var newThumb = CreateThumb(insertIndex);
            PointThumbs.Insert(insertIndex, newThumb);
        }

        void Drag_Completed(DragListener drag)
        {
            if (drag.IsCanceled) {
                operation?.Abort();
                operation = null;
                changeGroup?.Abort();
                changeGroup = null;
            } else {
                operation?.Commit();
                operation = null;
                changeGroup?.Commit();
                changeGroup = null;
            }
            IsResizing = false;
            _draggingThumb = null;
            Invalidate();
        }
    }
}
