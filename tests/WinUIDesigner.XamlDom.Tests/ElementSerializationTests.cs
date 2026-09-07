using System;
using Microsoft.UI.Xaml.Media;
using NUnit.Framework;
using Windows.Foundation;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.XamlDom.Tests
{
    /// <summary>
    /// Phase C: writing a WinRT value's own properties into the element that carries it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Phases A and B made a value round-trip when it has an attribute text form. Everything
    /// else was emitted as an empty element and lost - measured on seven types, one of which
    /// (<c>SkewTransform</c>) is a shipped designer feature whose output was silently discarded
    /// on every save.
    /// </para>
    /// <para>
    /// Every test runs on the WinUI dispatcher thread (see <see cref="WinUITestHost"/>) because
    /// every object involved is a thread-affine WinRT object. Assertions are written against a
    /// reparse of the saved text rather than against the text itself wherever the point is that
    /// the value survives; attribute-text assertions are used only where the point is the shape
    /// of the output.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class ElementSerializationTests
    {
        const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        static XamlDocument ParseDocument(string xaml)
        {
            return XamlParser.Parse(new System.IO.StringReader(xaml));
        }

        static string SaveToString(XamlDocument document)
        {
            var text = new System.IO.StringWriter();
            var settings = new System.Xml.XmlWriterSettings();
            settings.OmitXmlDeclaration = true;
            using (var writer = System.Xml.XmlWriter.Create(text, settings))
                document.Save(writer);
            return text.ToString();
        }

        /// <summary>Sets a property the way XamlModelProperty.cs:239 sets it.</summary>
        static void SetThroughTheDocument(XamlDocument document, string propertyName, object value)
        {
            var property = document.RootElement.FindOrCreateProperty(propertyName);
            property.PropertyValue = document.CreatePropertyValue(value, property);
        }

        /// <summary>Sets the property, saves, and reparses - the whole round trip in one call.</summary>
        static Microsoft.UI.Xaml.Controls.Grid RoundTripOnAGrid(string propertyName, object value, out string saved)
        {
            var document = ParseDocument("<Grid xmlns='" + PresentationNs + "'/>");
            SetThroughTheDocument(document, propertyName, value);
            saved = SaveToString(document);
            var reparsed = ParseDocument(saved);
            return (Microsoft.UI.Xaml.Controls.Grid)reparsed.RootInstance;
        }

        [Test]
        public void ASkewTransformKeepsBothAnglesAcrossASaveAndReload()
        {
            WinUITestHost.Run(() => {
                string saved;
                var grid = RoundTripOnAGrid(
                    "RenderTransform", new SkewTransform { AngleX = 30, AngleY = 10 }, out saved);

                var skew = grid.RenderTransform as SkewTransform;
                Assert.That(skew, Is.Not.Null,
                    "the transform did not survive the round trip at all; saved as: " + saved);
                Assert.That(skew.AngleX, Is.EqualTo(30).Within(0.001));
                Assert.That(skew.AngleY, Is.EqualTo(10).Within(0.001));
            });
        }

        [Test]
        public void ARotateTransformKeepsItsAngle()
        {
            WinUITestHost.Run(() => {
                string saved;
                var grid = RoundTripOnAGrid("RenderTransform", new RotateTransform { Angle = 45 }, out saved);
                var rotate = grid.RenderTransform as RotateTransform;
                Assert.That(rotate, Is.Not.Null, "saved as: " + saved);
                Assert.That(rotate.Angle, Is.EqualTo(45).Within(0.001));
            });
        }

        [Test]
        public void AScaleTransformKeepsBothScales()
        {
            WinUITestHost.Run(() => {
                string saved;
                var grid = RoundTripOnAGrid("RenderTransform", new ScaleTransform { ScaleX = 2, ScaleY = 3 }, out saved);
                var scale = grid.RenderTransform as ScaleTransform;
                Assert.That(scale, Is.Not.Null, "saved as: " + saved);
                Assert.That(scale.ScaleX, Is.EqualTo(2).Within(0.001));
                Assert.That(scale.ScaleY, Is.EqualTo(3).Within(0.001));
            });
        }

        [Test]
        public void ATranslateTransformKeepsBothOffsets()
        {
            WinUITestHost.Run(() => {
                string saved;
                var grid = RoundTripOnAGrid("RenderTransform", new TranslateTransform { X = 5, Y = 6 }, out saved);
                var translate = grid.RenderTransform as TranslateTransform;
                Assert.That(translate, Is.Not.Null, "saved as: " + saved);
                Assert.That(translate.X, Is.EqualTo(5).Within(0.001));
                Assert.That(translate.Y, Is.EqualTo(6).Within(0.001));
            });
        }

        [Test]
        public void ARectangleGeometryKeepsItsRect()
        {
            WinUITestHost.Run(() => {
                string saved;
                var grid = RoundTripOnAGrid("Clip", new RectangleGeometry { Rect = new Rect(1, 2, 3, 4) }, out saved);
                Assert.That(grid.Clip, Is.Not.Null, "saved as: " + saved);
                Assert.That(grid.Clip.Rect, Is.EqualTo(new Rect(1, 2, 3, 4)));
            });
        }

        [Test]
        public void AnEllipseGeometryKeepsItsCentreAndRadii()
        {
            WinUITestHost.Run(() => {
                var document = ParseDocument("<Path xmlns='" + PresentationNs + "'/>");
                SetThroughTheDocument(document, "Data",
                    new EllipseGeometry { Center = new Point(5, 6), RadiusX = 7, RadiusY = 8 });
                var saved = SaveToString(document);
                var path = (Microsoft.UI.Xaml.Shapes.Path)ParseDocument(saved).RootInstance;

                var ellipse = path.Data as EllipseGeometry;
                Assert.That(ellipse, Is.Not.Null, "saved as: " + saved);
                Assert.That(ellipse.Center, Is.EqualTo(new Point(5, 6)));
                Assert.That(ellipse.RadiusX, Is.EqualTo(7).Within(0.001));
                Assert.That(ellipse.RadiusY, Is.EqualTo(8).Within(0.001));
            });
        }

        [Test]
        public void ALineGeometryKeepsBothEndpoints()
        {
            WinUITestHost.Run(() => {
                var document = ParseDocument("<Path xmlns='" + PresentationNs + "'/>");
                SetThroughTheDocument(document, "Data",
                    new LineGeometry { StartPoint = new Point(1, 2), EndPoint = new Point(3, 4) });
                var saved = SaveToString(document);
                var path = (Microsoft.UI.Xaml.Shapes.Path)ParseDocument(saved).RootInstance;

                var line = path.Data as LineGeometry;
                Assert.That(line, Is.Not.Null, "saved as: " + saved);
                Assert.That(line.StartPoint, Is.EqualTo(new Point(1, 2)));
                Assert.That(line.EndPoint, Is.EqualTo(new Point(3, 4)));
            });
        }

        [Test]
        public void AnUnsetPropertyIsNotWritten()
        {
            WinUITestHost.Run(() => {
                var document = ParseDocument("<Grid xmlns='" + PresentationNs + "'/>");
                SetThroughTheDocument(document, "RenderTransform", new SkewTransform { AngleX = 30 });
                var saved = SaveToString(document);

                StringAssert.Contains("AngleX=\"30\"", saved);
                StringAssert.DoesNotContain("CenterX", saved,
                    "an unset property must not be written, or every saved transform carries " +
                    "CenterX=\"0\" CenterY=\"0\" noise");
                StringAssert.DoesNotContain("AngleY", saved);
            });
        }

        [Test]
        public void RuntimePlumbingIsNeverWritten()
        {
            WinUITestHost.Run(() => {
                var document = ParseDocument("<Grid xmlns='" + PresentationNs + "'/>");
                SetThroughTheDocument(document, "RenderTransform", new SkewTransform { AngleX = 30, AngleY = 10 });
                var saved = SaveToString(document);

                // TypeDescriptor offers these on every WinRT type. The writable-and-backed-by-a-
                // DependencyProperty filter is what keeps them out; without it the saved XAML
                // carries members no XAML parser can read back.
                StringAssert.DoesNotContain("Inverse", saved);
                StringAssert.DoesNotContain("Dispatcher", saved);
                StringAssert.DoesNotContain("DispatcherQueue", saved);
            });
        }

        [Test]
        public void ANestedListedTypeIsWrittenByRecursion()
        {
            WinUITestHost.Run(() => {
                var document = ParseDocument("<Path xmlns='" + PresentationNs + "'/>");
                SetThroughTheDocument(document, "Data", new LineGeometry {
                    StartPoint = new Point(1, 2),
                    EndPoint = new Point(3, 4),
                    Transform = new RotateTransform { Angle = 45 }
                });
                var saved = SaveToString(document);
                var path = (Microsoft.UI.Xaml.Shapes.Path)ParseDocument(saved).RootInstance;

                // A listed type nested inside another listed type, reached by recursion back
                // through CreatePropertyValue.
                var line = (LineGeometry)path.Data;
                var rotate = line.Transform as RotateTransform;
                Assert.That(rotate, Is.Not.Null, "saved as: " + saved);
                Assert.That(rotate.Angle, Is.EqualTo(45).Within(0.001));
            });
        }

        [Test]
        public void ALinearGradientBrushKeepsItsStops()
        {
            WinUITestHost.Run(() => {
                var brush = new LinearGradientBrush();
                brush.GradientStops.Add(new GradientStop { Color = Microsoft.UI.Colors.Red, Offset = 0 });
                brush.GradientStops.Add(new GradientStop { Color = Microsoft.UI.Colors.Blue, Offset = 1 });

                string saved;
                var grid = RoundTripOnAGrid("Background", brush, out saved);

                var reloaded = grid.Background as LinearGradientBrush;
                Assert.That(reloaded, Is.Not.Null, "saved as: " + saved);
                Assert.That(reloaded.GradientStops.Count, Is.EqualTo(2),
                    "the stops did not survive; saved as: " + saved);
                Assert.That(reloaded.GradientStops[0].Color, Is.EqualTo(Microsoft.UI.Colors.Red));
                Assert.That(reloaded.GradientStops[0].Offset, Is.EqualTo(0).Within(0.001));
                Assert.That(reloaded.GradientStops[1].Color, Is.EqualTo(Microsoft.UI.Colors.Blue));
                Assert.That(reloaded.GradientStops[1].Offset, Is.EqualTo(1).Within(0.001));
            });
        }

        [Test]
        public void ATransformGroupKeepsItsChildrenInOrder()
        {
            WinUITestHost.Run(() => {
                var group = new TransformGroup();
                group.Children.Add(new RotateTransform { Angle = 45 });
                group.Children.Add(new ScaleTransform { ScaleX = 2, ScaleY = 3 });

                string saved;
                var grid = RoundTripOnAGrid("RenderTransform", group, out saved);

                var reloaded = grid.RenderTransform as TransformGroup;
                Assert.That(reloaded, Is.Not.Null, "saved as: " + saved);
                Assert.That(reloaded.Children.Count, Is.EqualTo(2), "saved as: " + saved);
                Assert.That(reloaded.Children[0], Is.InstanceOf<RotateTransform>(),
                    "order matters: a rotate then a scale is not the same transform as a scale " +
                    "then a rotate");
                Assert.That(((RotateTransform)reloaded.Children[0]).Angle, Is.EqualTo(45).Within(0.001));
                Assert.That(reloaded.Children[1], Is.InstanceOf<ScaleTransform>());
                Assert.That(((ScaleTransform)reloaded.Children[1]).ScaleX, Is.EqualTo(2).Within(0.001));
            });
        }

        [Test]
        public void AGeometryGroupKeepsItsChildrenAndFillRule()
        {
            WinUITestHost.Run(() => {
                var group = new GeometryGroup { FillRule = FillRule.Nonzero };
                group.Children.Add(new RectangleGeometry { Rect = new Rect(1, 2, 3, 4) });
                group.Children.Add(new EllipseGeometry { Center = new Point(5, 6), RadiusX = 7, RadiusY = 8 });

                var document = ParseDocument("<Path xmlns='" + PresentationNs + "'/>");
                SetThroughTheDocument(document, "Data", group);
                var saved = SaveToString(document);
                var path = (Microsoft.UI.Xaml.Shapes.Path)ParseDocument(saved).RootInstance;

                var reloaded = path.Data as GeometryGroup;
                Assert.That(reloaded, Is.Not.Null, "saved as: " + saved);
                Assert.That(reloaded.FillRule, Is.EqualTo(FillRule.Nonzero));
                Assert.That(reloaded.Children.Count, Is.EqualTo(2), "saved as: " + saved);
                Assert.That(((RectangleGeometry)reloaded.Children[0]).Rect, Is.EqualTo(new Rect(1, 2, 3, 4)));
                Assert.That(((EllipseGeometry)reloaded.Children[1]).RadiusX, Is.EqualTo(7).Within(0.001));
            });
        }

        [Test]
        public void APopulatedCollectionIsWrittenEvenThoughItNeverReadsAsLocallySet()
        {
            WinUITestHost.Run(() => {
                var brush = new LinearGradientBrush();
                brush.GradientStops.Add(new GradientStop { Color = Microsoft.UI.Colors.Red, Offset = 0 });

                // The measurement this test exists to pin: the collection is the property's
                // lazily-created default and is mutated in place, so it never registers as a
                // local value. A writer that gates collections the way it gates scalars emits
                // nothing here and every other test in this fixture still passes.
                Assert.That(
                    brush.ReadLocalValue(LinearGradientBrush.GradientStopsProperty),
                    Is.EqualTo(Microsoft.UI.Xaml.DependencyProperty.UnsetValue),
                    "if this ever starts reporting the collection as locally set, the special " +
                    "case in WriteCollection can go");

                string saved;
                var grid = RoundTripOnAGrid("Background", brush, out saved);
                Assert.That(((LinearGradientBrush)grid.Background).GradientStops.Count, Is.EqualTo(1),
                    "saved as: " + saved);
            });
        }

        [Test]
        public void AnEmptyCollectionWritesNoPropertyElement()
        {
            WinUITestHost.Run(() => {
                XamlDocument document;
                var sink = SetWithASink("<Grid xmlns='" + PresentationNs + "'/>",
                    "RenderTransform", new TransformGroup(), out document);
                var saved = SaveToString(document);

                StringAssert.DoesNotContain("TransformGroup.Children", saved,
                    "an empty collection must not produce a property element");

                // An empty TransformGroup round-trips perfectly - TryWrite refusing (nothing to
                // write) is not the same failure as "this type has no XAML attribute form", and
                // must not be reported as one. Before the HasEntry(elementType) guard on
                // CreatePropertyValue's sink condition, this listed-but-empty case produced
                // exactly that false positive.
                Assert.That(sink.Messages, Is.Empty,
                    "an empty listed value round-trips fine and must not be reported; saved as: " + saved);
            });
        }

        [Test]
        public void ADeeplyNestedGraphStopsAtTheDepthCapWithoutThrowing()
        {
            WinUITestHost.Run(() => {
                // Twenty nested groups, against a MaxDepth of 12. A genuine reference cycle is
                // what MaxDepth exists to survive, but one could not be built here to test with:
                // a TransformGroup cannot be made a descendant of itself through the public
                // WinUI API, so depth is the only reachable way to drive the guard. The
                // InProgress reference check stays as the belt to this test's braces and is
                // recorded in the spec's Known gaps as unexercised.
                var outermost = new TransformGroup();
                var current = outermost;
                for (int i = 0; i < 19; i++) {
                    var next = new TransformGroup();
                    current.Children.Add(next);
                    current = next;
                }
                current.Children.Add(new RotateTransform { Angle = 45 });

                var document = ParseDocument("<Grid xmlns='" + PresentationNs + "'/>");
                var property = document.RootElement.FindOrCreateProperty("RenderTransform");

                string saved = null;
                Assert.DoesNotThrow(() => {
                    property.PropertyValue = document.CreatePropertyValue(outermost, property);
                    saved = SaveToString(document);
                }, "the write path must never throw - a throw here strands a PlacementOperation " +
                   "and, with no Application.UnhandledException handler in src/, ends the process");
                Assert.That(saved, Is.Not.Null);
                Assert.That(saved, Does.Contain("TransformGroup"),
                    "the outer levels are still written; only the levels past the cap are not");

                // The three assertions above pass unchanged whether MaxDepth is 12 or 1000, or
                // even with the cap removed outright - twenty levels cannot overflow a stack
                // either way, so none of them can detect the guard's removal. The innermost
                // RotateTransform sits at depth 20, past the cap, so its Angle must never reach
                // the saved text; that is the guard actually biting.
                StringAssert.DoesNotContain("Angle", saved,
                    "the innermost RotateTransform is past the depth cap and must not be reached; " +
                    "saved as: " + saved);

                // Stronger form, tied to the cap's exact value rather than just "some truncation
                // happened": twelve levels write their own element and recurse into the next;
                // the thirteenth is created (CreatePropertyValue always emits an element) but its
                // own TryWrite refuses before it can recurse into a fourteenth, so it is embedded
                // as a bare, childless shell and the chain stops there - thirteen <TransformGroup>
                // elements in total. "<TransformGroup" alone over-counts: it also matches the
                // twelve "<TransformGroup.Children>" property elements, so the character right
                // after the match is checked to exclude those.
                int transformGroupCount = 0;
                for (int index = saved.IndexOf("<TransformGroup", 0);
                     index >= 0;
                     index = saved.IndexOf("<TransformGroup", index + 1)) {
                    char afterMatch = saved[index + "<TransformGroup".Length];
                    if (afterMatch == '>' || afterMatch == ' ' || afterMatch == '/')
                        transformGroupCount++;
                }
                Assert.That(transformGroupCount, Is.EqualTo(13), "saved as: " + saved);
            });
        }

        // The registration shape is the one ValueFormattingTests.cs:588-625 already uses: the
        // sink reaches XamlDocument through XamlParserSettings.ServiceProvider, set before the
        // parse. There is no way to add a service to a document after the fact.
        sealed class RecordingSink : IXamlErrorSink
        {
            public readonly System.Collections.Generic.List<string> Messages =
                new System.Collections.Generic.List<string>();

            public void ReportError(string message, int line, int column)
            {
                Messages.Add(message);
            }
        }

        sealed class SingleServiceProvider : IServiceProvider
        {
            readonly Type serviceType;
            readonly object service;

            public SingleServiceProvider(Type serviceType, object service)
            {
                this.serviceType = serviceType;
                this.service = service;
            }

            public object GetService(Type type)
            {
                return type == serviceType ? service : null;
            }
        }

        /// <summary>
        /// Parses <paramref name="xaml"/> with a recording error sink attached, sets the property,
        /// and hands back both the sink and the document so the caller can also save it.
        /// </summary>
        static RecordingSink SetWithASink(string xaml, string propertyName, object value, out XamlDocument document)
        {
            var sink = new RecordingSink();
            var settings = new XamlParserSettings();
            settings.ServiceProvider = new SingleServiceProvider(typeof(IXamlErrorSink), sink);
            document = XamlParser.Parse(new System.IO.StringReader(xaml), settings);
            var property = document.RootElement.FindOrCreateProperty(propertyName);
            property.PropertyValue = document.CreatePropertyValue(value, property);
            return sink;
        }

        [Test]
        public void AnUnlistedBrushStillSavesAsAnEmptyElementAndIsReported()
        {
            WinUITestHost.Run(() => {
                XamlDocument document;
                var sink = SetWithASink("<Grid xmlns='" + PresentationNs + "'/>",
                    "Background", new ImageBrush(), out document);
                var saved = SaveToString(document);

                StringAssert.Contains("<ImageBrush", saved,
                    "an unlisted type must keep today's behaviour, not meet untested code");
                Assert.That(sink.Messages.Count, Is.EqualTo(1), "saved as: " + saved);
                StringAssert.Contains("ImageBrush", sink.Messages[0]);
            });
        }

        [Test]
        public void AnUnlistedTransformIsNowReportedWhereItUsedToBeSilent()
        {
            WinUITestHost.Run(() => {
                XamlDocument document;
                // A CompositeTransform is a Transform this phase does not list. Before Phase C
                // the gate was HasFormatter(declared type), and Transform is not one of the
                // formatter's sixteen - so a lost transform produced no diagnostic at all.
                var sink = SetWithASink("<Grid xmlns='" + PresentationNs + "'/>",
                    "RenderTransform", new CompositeTransform { Rotation = 45 }, out document);

                Assert.That(sink.Messages.Count, Is.EqualTo(1),
                    "a transform the writer cannot handle must say so");
                StringAssert.Contains("CompositeTransform", sink.Messages[0]);
            });
        }

        [Test]
        public void AWrittenValueIsNotReported()
        {
            WinUITestHost.Run(() => {
                XamlDocument document;
                var sink = SetWithASink("<Grid xmlns='" + PresentationNs + "'/>",
                    "RenderTransform", new SkewTransform { AngleX = 30 }, out document);

                Assert.That(sink.Messages, Is.Empty,
                    "the writer handled this one; reporting it would train users to ignore the " +
                    "diagnostic");
            });
        }

        [Test]
        public void ABindingOnAListedPropertyTypeIsNeitherWrittenNorReported()
        {
            WinUITestHost.Run(() => {
                XamlDocument document;
                var sink = SetWithASink("<Grid xmlns='" + PresentationNs + "'/>", "Background",
                    new Microsoft.UI.Xaml.Data.Binding { Path = new Microsoft.UI.Xaml.PropertyPath("Fill") },
                    out document);
                var saved = SaveToString(document);

                // SetWithASink assigns property.PropertyValue, which also runs
                // UpdateValueOnInstance - that reflects the raw Binding onto the live
                // Brush-typed CLR property and fails for its own, unrelated, pre-existing reason
                // (WinUI's DependencyProperty.SetValue does not resolve a Binding the way real
                // binding application does; see ValueFormattingTests.DoesNotReportABindingMarkupExtension,
                // which isolates around exactly this). This assertion is only about
                // CreatePropertyValue's own gate, so it checks for that gate's message text
                // rather than requiring the sink to be silent altogether.
                Assert.That(sink.Messages, Has.None.Contains("Cannot save"),
                    "a Binding round-trips through XamlObject's own markup-extension path; " +
                    "reporting it as unwritable would fire on every bound document");
                StringAssert.Contains("Binding", saved);
            });
        }

        // The depth cap used to truncate in complete silence: CreatePropertyValue suppresses its
        // own "cannot save as attribute text" report for every listed type, and the guard itself
        // said nothing, so a value the writer refused was saved empty with no diagnostic anywhere.
        // The refusal is deliberate, not a failure - hence its own wording, distinct from the
        // exception path's "Failed to write".
        [Test]
        public void AValueRefusedByTheDepthCapIsReported()
        {
            WinUITestHost.Run(() => {
                var outermost = new TransformGroup();
                var current = outermost;
                for (int i = 0; i < 19; i++) {
                    var next = new TransformGroup();
                    current.Children.Add(next);
                    current = next;
                }
                current.Children.Add(new RotateTransform { Angle = 45 });

                XamlDocument document;
                var sink = SetWithASink("<Grid xmlns='" + PresentationNs + "'/>",
                    "RenderTransform", outermost, out document);
                var saved = SaveToString(document);

                Assert.That(sink.Messages, Is.Not.Empty,
                    "a listed value the writer refuses must not be lost silently; saved as: " + saved);
                Assert.That(sink.Messages, Has.Some.Contains("depth limit of 12"),
                    "the report must name the guard that bit; got: " + string.Join(" | ", sink.Messages));
                Assert.That(sink.Messages, Has.None.Contains("Failed to write"),
                    "nothing threw here - the refusal must not be reported as an exception; got: "
                    + string.Join(" | ", sink.Messages));
            });
        }

    }
}
