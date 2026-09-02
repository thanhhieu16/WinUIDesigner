using System;
using Windows.Foundation;
using Windows.System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Adorners;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>
    /// Snap-to-raster placement base class. Ported from the reference's
    /// Extensions/RasterPlacementBehavior.cs. Like the reference it carries no [ExtensionFor]
    /// attribute of its own - it exists only as the base of SnaplinePlacementBehavior (Task 6)
    /// and, through it, CanvasPlacementSupport/GridPlacementSupport (Tasks 9-10).
    ///
    /// Three WinUI-specific changes from the reference:
    /// - Keyboard.IsKeyDown(Key.LeftCtrl) has no WinUI equivalent; the Ctrl check reads the
    ///   modifiers argument threaded from the originating pointer event instead (Task 4).
    /// - Brushes.Black doesn't exist in Microsoft.UI.Xaml.Media (compiler-confirmed) -
    ///   new SolidColorBrush(Microsoft.UI.Colors.Black).
    /// - ExtendedItem.Services.DesignPanel is GetRequiredService-backed in this port and throws
    ///   when no IDesignPanel is registered, unlike the reference's null-returning property.
    ///   Every "DesignPanel designPanel = ...Services.DesignPanel as DesignPanel" in the
    ///   reference becomes Services.GetService&lt;IDesignPanel&gt;() as DesignPanel, which
    ///   preserves the reference's null-check semantics.
    /// </summary>
    public class RasterPlacementBehavior : DefaultPlacementBehavior
    {
        Canvas surface;
        AdornerPanel adornerPanel;
        bool rasterDrawn = false;
        int raster = 5;

        public override void BeginPlacement(PlacementOperation operation)
        {
            base.BeginPlacement(operation);

            DesignPanel designPanel = ExtendedItem.Services.GetService<IDesignPanel>() as DesignPanel;
            if (designPanel != null)
                raster = designPanel.RasterWidth;

            CreateSurface(operation);
        }

        public override void EndPlacement(PlacementOperation operation)
        {
            base.EndPlacement(operation);
            DeleteSurface();
        }

        public override void EnterContainer(PlacementOperation operation)
        {
            base.EnterContainer(operation);
            CreateSurface(operation);
        }

        public override void LeaveContainer(PlacementOperation operation)
        {
            base.LeaveContainer(operation);
            DeleteSurface();
        }

        void CreateSurface(PlacementOperation operation)
        {
            if (ExtendedItem.Services.GetService<IDesignPanel>() != null) {
                surface = new Canvas();
                adornerPanel = new AdornerPanel();
                adornerPanel.SetAdornedElement(ExtendedItem.View, ExtendedItem);
                AdornerPanel.SetPlacement(surface, AdornerPlacement.FillContent);
                adornerPanel.Children.Add(surface);
                ExtendedItem.Services.DesignPanel.Adorners.Add(adornerPanel);
            }
        }

        void DeleteSurface()
        {
            rasterDrawn = false;
            if (surface != null) {
                ExtendedItem.Services.DesignPanel.Adorners.Remove(adornerPanel);
                adornerPanel = null;
                surface = null;
            }
        }

        public override void BeforeSetPosition(PlacementOperation operation, VirtualKeyModifiers modifiers)
        {
            base.BeforeSetPosition(operation, modifiers);
            if (surface == null) return;

            DesignPanel designPanel = ExtendedItem.Services.GetService<IDesignPanel>() as DesignPanel;
            if (designPanel == null || !designPanel.UseRasterPlacement)
                return;

            if ((modifiers & VirtualKeyModifiers.Control) == VirtualKeyModifiers.Control) {
                surface.Children.Clear();
                rasterDrawn = false;
                return;
            }

            drawRaster();

            var bounds = operation.PlacedItems[0].Bounds;
            bounds.Y = ((int)bounds.Y / raster) * raster;
            bounds.X = ((int)bounds.X / raster) * raster;
            bounds.Width = Convert.ToInt32((bounds.Width / raster)) * raster;
            bounds.Height = Convert.ToInt32((bounds.Height / raster)) * raster;
            operation.PlacedItems[0].Bounds = bounds;
        }

        public override Point PlacePoint(Point point, VirtualKeyModifiers modifiers)
        {
            if (surface == null)
                return base.PlacePoint(point, modifiers);

            DesignPanel designPanel = ExtendedItem.Services.GetService<IDesignPanel>() as DesignPanel;
            if (designPanel == null || !designPanel.UseRasterPlacement)
                return base.PlacePoint(point, modifiers);

            if ((modifiers & VirtualKeyModifiers.Control) == VirtualKeyModifiers.Control) {
                surface.Children.Clear();
                rasterDrawn = false;
                return base.PlacePoint(point, modifiers);
            }

            drawRaster();

            point.Y = ((int)point.Y / raster) * raster;
            point.X = ((int)point.X / raster) * raster;

            return point;
        }

        void drawRaster()
        {
            if (!rasterDrawn) {
                rasterDrawn = true;

                var w = ModelTools.GetWidth(ExtendedItem.View);
                var h = ModelTools.GetHeight(ExtendedItem.View);
                // A DoubleCollection cannot be shared between two Shapes in WinUI - a second
                // StrokeDashArray assignment of the same instance throws ArgumentException
                // (E_INVALIDARG), verified at runtime - so unlike the reference's single hoisted
                // `dash` collection, this rebuilds the dash array per line.
                for (int i = 0; i <= h; i += raster) {
                    var line = new Line() {
                        X1 = 0,
                        Y1 = i,
                        X2 = w,
                        Y2 = i,
                        StrokeThickness = 1,
                        Stroke = new SolidColorBrush(Microsoft.UI.Colors.Black),
                        StrokeDashArray = new DoubleCollection() { 1, raster - 1 },
                    };
                    surface.Children.Add(line);
                }
            }
        }
    }
}
