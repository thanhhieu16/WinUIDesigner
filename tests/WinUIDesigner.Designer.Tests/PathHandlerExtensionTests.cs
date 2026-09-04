using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Controls;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class PathHandlerExtensionTests
    {
        // NOTE: no <Path.Data> here (deliberately, see Load() below). The brief's fixture used
        // <PathFigure StartPoint="10,10"><LineSegment Point="50,10" />...  - those are
        // Windows.Foundation.Point-typed XAML attributes, and this port's XamlDom layer resolves a
        // property's converter via System.ComponentModel.TypeDescriptor.GetConverter(ReturnType)
        // (XamlPropertyInfo.cs:177). Windows.Foundation.Point carries no
        // System.ComponentModel.TypeConverterAttribute in this WinUI SDK, so GetConverter falls
        // back to the base TypeConverter, whose ConvertFromString throws NotSupportedException -
        // and XamlProperty.UpdateValueOnInstance swallows that into a Debug.WriteLine
        // (XamlProperty.cs:269-271) rather than rethrowing. Result: the element tree
        // (PathGeometry/PathFigure/2x LineSegment) parses fine but every Point-typed attribute
        // silently stays at its (0,0) default. Confirmed empirically with a throwaway probe:
        //   TypeDescriptor.GetConverter(typeof(Windows.Foundation.Point))
        //     => System.ComponentModel.TypeConverter, CanConvertFrom(string) == False,
        //        ConvertFromString("10,10") => NotSupportedException,
        //        typeof(Point).GetCustomAttributes(typeof(TypeConverterAttribute), true).Length == 0
        // and by parsing the brief's own fixture: StartPoint 0,0 and both LineSegment.Point 0,0.
        // Same class of gap Task 9 hit with Microsoft.UI.Xaml.Media.PointCollection, same
        // workaround: build the object graph in code and hand it to SetValue as a typed CLR
        // object.
        const string PathInCanvasXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\">" +
            "<Path x:Name=\"P\" Stroke=\"Black\" StrokeThickness=\"2\" />" +
            "</Canvas>";

        static (DesignSurface surface, DesignItem pathItem, Microsoft.UI.Xaml.Shapes.Path path, PathHandlerExtension extension) Load(Microsoft.UI.Xaml.Window window)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(PathInCanvasXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();
            var path = (Microsoft.UI.Xaml.Shapes.Path)((Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component).Children[0];
            var pathItem = surface.DesignContext.Services.Component.GetDesignItem(path);

            // Set the initial Data via the DesignItem property API (a real PathGeometry object
            // graph, not a string) - the same SetValue(object) path PathHandlerExtension.
            // SaveGeometry itself uses - BEFORE selection, so PathHandlerExtension.OnInitialized
            // (which runs when OnlyOneItemSelectedExtensionServer attaches the extension on
            // selection) sees the figure already in place.
            var segments = new Microsoft.UI.Xaml.Media.PathSegmentCollection();
            segments.Add(new Microsoft.UI.Xaml.Media.LineSegment { Point = new Windows.Foundation.Point(50, 10) });
            segments.Add(new Microsoft.UI.Xaml.Media.LineSegment { Point = new Windows.Foundation.Point(50, 50) });
            var figures = new Microsoft.UI.Xaml.Media.PathFigureCollection();
            figures.Add(new Microsoft.UI.Xaml.Media.PathFigure {
                StartPoint = new Windows.Foundation.Point(10, 10),
                Segments = segments
            });
            pathItem.Properties["Data"].SetValue(new Microsoft.UI.Xaml.Media.PathGeometry { Figures = figures });

            surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { pathItem }, SelectionTypes.Primary);
            surface.UpdateLayout();
            var extension = pathItem.Extensions.OfType<PathHandlerExtension>().Single();
            return (surface, pathItem, path, extension);
        }

        [Test]
        public void ThreePointsInTheFigureProduceThreeThumbs()
        {
            // StartPoint + 2 LineSegment anchors = 3 PathPoints, 3 thumbs (no control points -
            // LineSegment has no TargetPathPoint).
            WinUITestHost.RunWithWindow(window => {
                var (_, _, _, extension) = Load(window);
                Assert.AreEqual(3, extension.PointThumbs.Count);
                Assert.IsTrue(extension.PointThumbs.All(t => !t.IsEllipse), "LineSegment anchors are all squares, no Bezier control points here");
            });
        }

        [Test]
        public void DraggingTheMiddleAnchorMovesOnlyThatLineSegmentsPointAndCommitsOneUndoEntry()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, _, path, extension) = Load(window);
                var undo = surface.DesignContext.Services.GetService<UndoService>();
                int before = undo.UndoActions.Count();
                var middleThumb = extension.PointThumbs[1]; // first LineSegment's Point (was 50,10)
                // Must be the extension's OWN DragListener for this thumb (not a freshly
                // constructed one) - Simulate* drives whichever instance's internal state machine
                // it's called on, and only the extension's own instance is wired to
                // Drag_Started/Drag_Changed/Drag_Completed. See PathHandlerExtension's
                // DragListeners field remarks.
                var drag = extension.DragListeners[middleThumb];

                drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                drag.SimulateMove(new Windows.Foundation.Point(5, -5));
                drag.SimulateUp(new Windows.Foundation.Point(5, -5));
                surface.UpdateLayout();

                var geometry = (Microsoft.UI.Xaml.Media.PathGeometry)path.Data;
                var figure = geometry.Figures[0];
                Assert.AreEqual(10, figure.StartPoint.X, 0.5, "start point untouched");
                var firstLine = (Microsoft.UI.Xaml.Media.LineSegment)figure.Segments[0];
                Assert.AreEqual(55, firstLine.Point.X, 0.5);
                Assert.AreEqual(5, firstLine.Point.Y, 0.5);
                var secondLine = (Microsoft.UI.Xaml.Media.LineSegment)figure.Segments[1];
                Assert.AreEqual(50, secondLine.Point.X, 0.5, "second segment's own point untouched");
                Assert.AreEqual(before + 1, undo.UndoActions.Count());
            });
        }
    }
}
