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

        /// <summary>
        /// Parses <em>successfully</em> and still reports an error: XamlParser.ReportException
        /// reports and continues whenever an error sink is present, so a bad value conversion
        /// leaves LoadFailed false with LoadErrors non-empty. Measured on this exact string:
        /// one error, "Cannot set Width: nonsense is not a valid value for Double.
        /// (Parameter 'value')", at line 3 column 23 - which is the W of Width on line 3.
        /// Three lines and real indentation on purpose: a re-serialization of this tree is a
        /// different string (attributes re-sorted, Content before Width), so a test that the
        /// text survives the mode switch cannot pass by accident.
        /// </summary>
        const string RecoverableErrorXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"\r\n" +
            "        xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\r\n" +
            "  <Button x:Name=\"Ok\" Width=\"nonsense\" Content=\"Ok\" />\r\n" +
            "</Canvas>";

        /// <summary>
        /// A perfectly ordinary document with LF-only line endings - what File.ReadAllText
        /// hands back for a checkout with core.autocrlf=false, a file authored on another
        /// platform, or anything generated. Not a CRLF character anywhere. Deliberately free of
        /// parse errors, so the only thing the test below can be measuring is line endings.
        /// </summary>
        const string LfOnlyXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"\n" +
            "        xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
            "  <Button x:Name=\"Ok\" Width=\"75\" Content=\"Ok\" />\n" +
            "</Canvas>";

        const string MalformedXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<Button Width=\"75\"";

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
        public void SwitchingToXamlModeFillsTextFromTheLiveTree()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    var button = doc.DesignContext.RootItem.ContentProperty.CollectionElements[0];
                    button.Properties["Width"].SetValue(123.0);

                    doc.Mode = DocumentMode.Xaml;

                    Assert.That(doc.InXamlMode, Is.True);
                    Assert.That(doc.InDesignMode, Is.False);
                    StringAssert.Contains("123", doc.Text, "the live edit should be in the text");
                }
            });
        }

        [Test]
        public void AnEditMadeInXamlModeIsInTheTreeAfterSwitchingBack()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    doc.Mode = DocumentMode.Xaml;
                    doc.Text = doc.Text.Replace("Width=\"75\"", "Width=\"200\"");
                    doc.Mode = DocumentMode.Design;

                    Assert.That(doc.LoadFailed, Is.False, "the edited text should still parse");
                    var button = (Button)doc.DesignContext.RootItem.ContentProperty
                        .CollectionElements[0].Component;
                    Assert.That(button.Width, Is.EqualTo(200.0));
                }
            });
        }

        [Test]
        public void ReAssertingTheCurrentModeIsANoOpAndKeepsUnsavedText()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    doc.Mode = DocumentMode.Xaml;
                    var typed = doc.Text + "<!-- typed -->";
                    doc.Text = typed;

                    // Decision 10: without the equality guard this re-runs UpdateXaml and
                    // silently replaces the typing with a serialization of the untouched tree.
                    doc.Mode = DocumentMode.Xaml;

                    Assert.That(doc.Text, Is.EqualTo(typed));
                }
            });
        }

        [Test]
        public void MalformedTextSwitchedToDesignModeReportsAndKeepsTheText()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    doc.Mode = DocumentMode.Xaml;
                    doc.Text = MalformedXaml;

                    Assert.DoesNotThrow(() => doc.Mode = DocumentMode.Design,
                        "the write path must never throw - a throw here kills the app mid-edit");
                    Assert.That(doc.LoadFailed, Is.True);
                    Assert.That(doc.LoadErrors, Is.Not.Empty);
                    Assert.That(doc.Text, Is.EqualTo(Document.NormaliseLineEndings(MalformedXaml)),
                        "UpdateXaml early-returns on a null context, so the bad text survives");
                }
            });
        }

        [Test]
        public void TheSelectionServiceIsClosedOffInXamlMode()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    Assert.That(doc.SelectionService, Is.Not.Null, "design mode exposes it");
                    doc.Mode = DocumentMode.Xaml;
                    Assert.That(doc.SelectionService, Is.Null,
                        "Decision 15: the right column must have nothing to edit through while " +
                        "the text is the source of truth");
                    doc.Mode = DocumentMode.Design;
                    Assert.That(doc.SelectionService, Is.Not.Null);
                }
            });
        }

        [Test]
        public void AChangeThatLandsWhileInXamlModeIsReflectedIntoTheText()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    doc.Mode = DocumentMode.Xaml;
                    var before = doc.Text;

                    var button = doc.DesignContext.RootItem.ContentProperty.CollectionElements[0];
                    button.Properties["Width"].SetValue(321.0);

                    Assert.That(doc.Text, Is.Not.EqualTo(before));
                    StringAssert.Contains("321", doc.Text,
                        "Decision 15: a mutation that reaches the tree in XAML mode must be " +
                        "serialized into the text, not silently reverted on the next switch");
                }
            });
        }

        [Test]
        public void DirtyTracksTheUndoStackAndARealTextEditButNotSerializationOrLineEndings()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    Assert.That(doc.IsDirty, Is.False, "a freshly loaded document is clean");

                    doc.UpdateXaml();
                    Assert.That(doc.IsDirty, Is.False,
                        "serializing the tree is not a user edit and writes the backing field");

                    // Decision 14: TextBox hands back lone CR where XamlFormatter emitted CRLF
                    // (measured). Assigning that back must not dirty a document nobody touched.
                    doc.Text = doc.Text.Replace("\r\n", "\r");
                    Assert.That(doc.IsDirty, Is.False,
                        "a line-ending-only difference is not an edit");

                    doc.Text = doc.Text + "<!-- real edit -->";
                    Assert.That(doc.IsDirty, Is.True, "a real text change marks the document dirty");

                    doc.MarkSaved();
                    Assert.That(doc.IsDirty, Is.False);

                    var canvas = doc.DesignContext.RootItem;
                    var button = canvas.ContentProperty.CollectionElements[0];
                    button.Properties["Width"].SetValue(120.0);
                    Assert.That(doc.IsDirty, Is.True, "a committed change marks the document dirty");
                }
            });
        }

        /// <summary>
        /// The mode setter re-serializes only a document that has moved on from what was parsed.
        /// </summary>
        /// <remarks>
        /// The untouched half is the bug fix. A document that parses with recoverable errors is
        /// the only kind that fills the error strip on a freshly opened file, and its errors'
        /// Line/Column address the file's own text; an unconditional UpdateXaml() on the way into
        /// XAML mode destroyed exactly that text, so DocumentView.JumpToError's staleness guard
        /// refused every jump the strip could offer. The dirty half is the other side of the same
        /// condition: a surface edit moves the tree without touching Text, so IsDirty is the only
        /// thing that can tell the setter to serialize it. Spec Decision 10.
        /// </remarks>
        [Test]
        public void EnteringXamlModeReSerializesOnlyADocumentThatHasMovedOnFromWhatWasParsed()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, RecoverableErrorXaml))
                {
                    Assert.That(doc.LoadFailed, Is.False,
                        "a bad conversion is reported and parsing continues - this document loads");
                    Assert.That(doc.LoadErrors, Is.Not.Empty,
                        "and it is the only class of document that fills the error strip on open");
                    Assert.That(doc.IsDirty, Is.False);

                    doc.Mode = DocumentMode.Xaml;

                    // Ordinal, deliberately: "still parses the same" would be satisfied by a
                    // re-serialization, this cannot be.
                    Assert.That(doc.Text, Is.EqualTo(doc.ParsedText)
                        .Using((System.Collections.Generic.IComparer<string>)System.StringComparer.Ordinal),
                        "an untouched document must show its own text, byte for byte, or the " +
                        "coordinates its LoadErrors carry address text that no longer exists");
                    Assert.That(doc.IsDirty, Is.False, "looking at the XAML is not an edit");

                    // Proves the assertion above is not vacuous: a re-serialization really is a
                    // different string for this input, so surviving it means something.
                    doc.UpdateXaml();
                    Assert.That(doc.Text, Is.Not.EqualTo(doc.ParsedText)
                        .Using((System.Collections.Generic.IComparer<string>)System.StringComparer.Ordinal),
                        "UpdateXaml re-sorts this document's attributes, so the switch above " +
                        "would have been detectable had it run");
                }

                using (var doc = new Document(null, RecoverableErrorXaml))
                {
                    var button = doc.DesignContext.RootItem.ContentProperty.CollectionElements[0];
                    button.Properties["Height"].SetValue(44.0);
                    Assert.That(doc.IsDirty, Is.True, "a surface edit dirties the document");
                    Assert.That(doc.Text, Is.EqualTo(doc.ParsedText),
                        "and it does so without touching Text, which is why IsDirty has to be " +
                        "half of the setter's condition");

                    doc.Mode = DocumentMode.Xaml;

                    StringAssert.Contains("44", doc.Text,
                        "a dirty document must still be serialized on the way into XAML mode, " +
                        "or the user is shown XAML that predates their own edit");
                }
            });
        }

        /// <summary>
        /// An LF-only file is untouched too, and its first switch to XAML mode leaves it alone.
        /// </summary>
        /// <remarks>
        /// The constructor used to store its argument raw while UpdateDesign normalised into
        /// ParsedText, so an LF-only document had text != parsedText from the moment it was
        /// built and re-serialized on the very first switch into XAML mode - the exact case the
        /// condition exists to spare, silently exempted from it. File.ReadAllText returns
        /// whatever line endings are on disk, so this is a common file, not an exotic one.
        /// </remarks>
        [Test]
        public void AnLfOnlyFileIsNotReSerializedOnItsFirstSwitchToXamlMode()
        {
            WinUITestHost.Run(() =>
            {
                // Guards the constant itself: a stray CRLF in the literal, or an editor
                // rewriting the file, would make everything below pass for the wrong reason.
                Assert.That(LfOnlyXaml, Does.Not.Contain("\r"), "the input must be LF-only");
                Assert.That(LfOnlyXaml, Does.Contain("\n"), "and must have line breaks at all");

                using (var doc = new Document(null, LfOnlyXaml))
                {
                    Assert.That(doc.LoadFailed, Is.False);
                    Assert.That(doc.IsDirty, Is.False);

                    var expected = Document.NormaliseLineEndings(LfOnlyXaml);

                    doc.Mode = DocumentMode.Xaml;

                    Assert.That(doc.Text, Is.EqualTo(expected)
                        .Using((System.Collections.Generic.IComparer<string>)System.StringComparer.Ordinal),
                        "the constructor normalises, so text == parsedText and the first switch " +
                        "must not re-serialize - storing the argument raw exempted every LF-only " +
                        "file from the fix");
                    Assert.That(doc.IsDirty, Is.False, "and none of that is a user edit");

                    // Not vacuous: a re-serialization of this input really is a different string,
                    // so surviving the switch above means something.
                    doc.UpdateXaml();
                    Assert.That(doc.Text, Is.Not.EqualTo(expected)
                        .Using((System.Collections.Generic.IComparer<string>)System.StringComparer.Ordinal),
                        "UpdateXaml re-indents this document, so the switch above would have " +
                        "been detectable had it run");
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

                    // Phase 5 Decision 18: after an unhandled exception the tree survives in whatever
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

        [Test]
        public void RefreshRebuildsTheTreeAndKeepsAnUntouchedDocumentsOwnText()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, RecoverableErrorXaml))
                {
                    var before = doc.DesignContext;
                    var text = doc.Text;
                    var raised = new System.Collections.Generic.List<string>();
                    doc.PropertyChanged += (s, e) => raised.Add(e.PropertyName);

                    doc.Refresh();

                    Assert.That(doc.DesignContext, Is.Not.Null.And.Not.SameAs(before), "a refresh builds a new tree");
                    Assert.That(doc.Text, Is.EqualTo(text),
                        "an untouched file keeps its own text, so its errors' coordinates still address it");
                    Assert.That(doc.LoadErrors, Is.Not.Empty, "and the re-parse reports its errors again");
                    Assert.That(doc.IsDirty, Is.False, "a refresh changes no content");
                    Assert.That(raised, Does.Contain("DesignContext"), "listeners are told to re-wire");
                }
            });
        }

        [Test]
        public void RefreshCarriesASurfaceEditIntoTheRebuiltTree()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    var button = doc.DesignContext.RootItem.ContentProperty.CollectionElements[0];
                    button.Properties["Width"].SetValue(321.0);

                    doc.Refresh();

                    var rebuilt = (Button)doc.DesignContext.RootItem.ContentProperty.CollectionElements[0].Component;
                    Assert.That(rebuilt.Width, Is.EqualTo(321.0), "the edit was serialized before the reload");
                    Assert.That(doc.IsDirty, Is.True, "and the document is still unsaved");
                }
            });
        }

        [Test]
        public void RefreshInXamlModeReParsesTheTypedTextWithoutTouchingIt()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    doc.Mode = DocumentMode.Xaml;
                    doc.Text = MalformedXaml;

                    doc.Refresh();

                    Assert.That(doc.Mode, Is.EqualTo(DocumentMode.Xaml), "the user stays in the editor");
                    Assert.That(doc.Text, Is.EqualTo(MalformedXaml), "what they typed is the source, never overwritten");
                    Assert.That(doc.LoadFailed, Is.True, "and the error strip learns it does not parse");
                }
            });
        }
    }
}
