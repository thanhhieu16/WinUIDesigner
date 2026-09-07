using System.Linq;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using NUnit.Framework;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Shell.Tests
{
    [TestFixture]
    public class DocumentTests
    {
        const string CanvasWithButton =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"640\" Height=\"480\">" +
            "<Button x:Name=\"Ok\" Width=\"75\" Height=\"32\" Canvas.Left=\"10\" Canvas.Top=\"20\" Content=\"Ok\" />" +
            "</Canvas>";

        const string CanvasWithPolyline =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"640\" Height=\"480\">" +
            "<Polyline x:Name=\"Line\" Points=\"10,10 40,25 70,5\" Stroke=\"Red\" StrokeThickness=\"2\" />" +
            "</Canvas>";

        [Test]
        public void ALoadedDocumentExposesItsContextRootAndOutline()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    Assert.That(doc.LoadFailed, Is.False);
                    Assert.That(doc.DesignContext, Is.Not.Null);
                    Assert.That(doc.DesignContext.RootItem, Is.Not.Null);
                    Assert.That(doc.DesignContext.RootItem.Component, Is.TypeOf<Canvas>());
                    Assert.That(doc.OutlineRoot, Is.Not.Null);
                    Assert.That(doc.UndoService, Is.Not.Null);
                    Assert.That(doc.SelectionService, Is.Not.Null);
                    Assert.That(doc.LoadErrors, Is.Empty);
                }
            });
        }

        [Test]
        public void TheRoundTripIsAFixedPointAsAString()
        {
            WinUITestHost.Run(() =>
            {
                string first;
                using (var doc = new Document(null, CanvasWithButton))
                {
                    doc.UpdateXaml();
                    first = doc.Text;
                }

                string second;
                using (var doc = new Document(null, first))
                {
                    doc.UpdateXaml();
                    second = doc.Text;
                }

                // Checked before the fixed-point comparison below, and load-bearing: UpdateXaml
                // returns early when DesignContext is null, leaving Text at the constructor's
                // value. Without this assertion an UpdateXaml that did nothing at all would still
                // pass, because `first` would just be the input constant and the second document
                // would be built from that same string. The formatter guarantees the difference -
                // CanvasWithButton is one unindented line and a round trip is never that.
                Assert.That(first, Is.Not.EqualTo(CanvasWithButton).Using((System.Collections.Generic.IComparer<string>)System.StringComparer.Ordinal),
                    "UpdateXaml must actually re-serialize the tree, or the fixed point below is vacuous");

                // Ordinal string equality, deliberately: "re-parses into an equivalent tree" can
                // be satisfied by a null check, this cannot. Save/load/save must be stable, or
                // every save churns the file.
                Assert.That(second, Is.EqualTo(first).Using((System.Collections.Generic.IComparer<string>)System.StringComparer.Ordinal));
            });
        }

        [Test]
        public void APolylineKeepsItsPointsThroughASave()
        {
            WinUITestHost.Run(() =>
            {
                string saved;
                using (var doc = new Document(null, CanvasWithPolyline))
                {
                    doc.UpdateXaml();
                    saved = doc.Text;
                }

                // Phases A and B exist so this holds: before them a WinRT-typed value serialized
                // as an empty element and the points were lost on save.
                Assert.That(saved, Does.Contain("Points"));

                using (var reloaded = new Document(null, saved))
                {
                    var canvas = (Canvas)reloaded.DesignContext.RootItem.Component;
                    var polyline = canvas.Children.OfType<Polyline>().Single();
                    Assert.That(polyline.Points.Count, Is.EqualTo(3));
                    Assert.That(polyline.Points[0].X, Is.EqualTo(10.0));
                    Assert.That(polyline.Points[1].Y, Is.EqualTo(25.0));
                    Assert.That(polyline.Points[2].X, Is.EqualTo(70.0));
                }
            });
        }

        [Test]
        public void DirtyTracksTheUndoStackAndNotTheTextSetter()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    Assert.That(doc.IsDirty, Is.False, "a freshly loaded document is clean");

                    doc.UpdateXaml();
                    Assert.That(doc.IsDirty, Is.False,
                        "serializing the document is not an edit - the reference's Text setter " +
                        "marks it dirty here, which is the behaviour Decision 5 of the spec drops");

                    var canvas = doc.DesignContext.RootItem;
                    var button = canvas.ContentProperty.CollectionElements[0];
                    button.Properties["Width"].SetValue(120.0);
                    Assert.That(doc.IsDirty, Is.True, "a committed change marks the document dirty");

                    doc.MarkSaved();
                    Assert.That(doc.IsDirty, Is.False);
                }
            });
        }

        [Test]
        public void MarkSuspectMakesACleanDocumentDirty()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    Assert.That(doc.IsDirty, Is.False, "a freshly loaded document is clean");

                    // Spec Decision 18: after an unhandled exception the tree survives in whatever
                    // state the failure left it, and only a forced dirty flag makes the close gate
                    // prompt about it. Nothing else on Document can raise IsDirty from the outside.
                    doc.MarkSuspect();

                    Assert.That(doc.IsDirty, Is.True);
                    Assert.That(doc.Title, Does.EndWith("*"), "a suspect document reads as unsaved");
                }
            });
        }

        [Test]
        public void DisposeUnloadsTheDesignerAndIsIdempotent()
        {
            WinUITestHost.Run(() =>
            {
                var doc = new Document(null, CanvasWithButton);
                Assert.That(doc.DesignSurface.DesignContext, Is.Not.Null);

                // Captured before Dispose so it survives to prove the handler was detached: a
                // leaked Document.OnUndoStackChanged subscription would otherwise mark a disposed
                // document dirty in response to activity it should no longer be watching, and the
                // soak test would end up blaming the engine for a leak that lives here instead.
                var undoService = doc.UndoService;

                doc.Dispose();
                Assert.That(doc.DesignSurface.DesignContext, Is.Null);
                Assert.DoesNotThrow(() => doc.Dispose());

                undoService.Clear();
                Assert.That(doc.IsDirty, Is.False,
                    "Dispose must have detached UndoStackChanged, or this raise would mark the disposed document dirty");
            });
        }

        [Test]
        public void TitleMarksUnsavedWork()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    doc.SetTempName("New1");
                    Assert.That(doc.Title, Is.EqualTo("New1"));

                    var button = doc.DesignContext.RootItem.ContentProperty.CollectionElements[0];
                    button.Properties["Width"].SetValue(200.0);
                    Assert.That(doc.Title, Is.EqualTo("New1*"));
                }
            });
        }
    }
}
