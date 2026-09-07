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
    }
}
