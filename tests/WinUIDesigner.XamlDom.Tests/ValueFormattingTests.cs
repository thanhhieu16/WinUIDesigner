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
    /// <para>
    /// Every test runs on the WinUI dispatcher thread (see <see cref="WinUITestHost"/>) because
    /// every object involved is a thread-affine WinRT object.
    /// </para>
    /// <para>
    /// Not every rejection branch in <c>XamlValueFormatter</c> has a test here: a live
    /// <c>FontFamily</c> with an empty <c>Source</c> cannot be constructed on this SDK through any
    /// path (the constructor throws, and so does <c>XamlReader.Load</c>), so that guard clause is
    /// verified by inspection at its declaration rather than by a test that could pass with the
    /// clause removed.
    /// </para>
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

        [Test]
        public void RefusesPolySegmentsWhoseCountIsNotAMultipleOfTheirCommandArity()
        {
            WinUITestHost.Run(() => {
                var badBezierPoints = new PointCollection();
                badBezierPoints.Add(new Point(0, 0));
                badBezierPoints.Add(new Point(10, 0));
                badBezierPoints.Add(new Point(20, 10));
                badBezierPoints.Add(new Point(30, 10));
                var badBezierFigure = new PathFigure { StartPoint = new Point(0, 0) };
                badBezierFigure.Segments.Add(new PolyBezierSegment { Points = badBezierPoints });
                var badBezierGeometry = new PathGeometry();
                badBezierGeometry.Figures.Add(badBezierFigure);
                Assert.IsFalse(GeometryMiniLanguageWriter.CanWrite(badBezierGeometry),
                    "A PolyBezierSegment with a point count that isn't a multiple of 3 would " +
                    "silently drop its trailing points");

                var badQuadPoints = new PointCollection();
                badQuadPoints.Add(new Point(0, 0));
                badQuadPoints.Add(new Point(10, 0));
                badQuadPoints.Add(new Point(20, 10));
                var badQuadFigure = new PathFigure { StartPoint = new Point(0, 0) };
                badQuadFigure.Segments.Add(new PolyQuadraticBezierSegment { Points = badQuadPoints });
                var badQuadGeometry = new PathGeometry();
                badQuadGeometry.Figures.Add(badQuadFigure);
                Assert.IsFalse(GeometryMiniLanguageWriter.CanWrite(badQuadGeometry),
                    "A PolyQuadraticBezierSegment with a point count that isn't a multiple of 2 " +
                    "would silently drop its trailing point");
            });
        }

        static string Format(Type declaredType, object value)
        {
            Assert.IsTrue(XamlValueFormatter.HasFormatter(declaredType),
                "No formatter for " + declaredType.FullName);
            Assert.IsTrue(XamlValueFormatter.CanFormat(declaredType, value),
                "The formatter refused a value it should accept: " + value);
            return XamlValueFormatter.Format(declaredType, value);
        }

        static object ReadBack(Type declaredType, string text)
        {
            return XamlValueConverter.TryCreate(declaredType)
                .ConvertFrom(null, CultureInfo.InvariantCulture, text);
        }

        [Test]
        public void FormatsEveryValueTypeInTheTableAndReadsItBackEqual()
        {
            WinUITestHost.Run(() => {
                var cases = new object[,] {
                    { typeof(Microsoft.UI.Xaml.Thickness), new Microsoft.UI.Xaml.Thickness(1, 2.5, 3, 4), "1,2.5,3,4" },
                    { typeof(Microsoft.UI.Xaml.CornerRadius), new Microsoft.UI.Xaml.CornerRadius(1, 2, 3, 4), "1,2,3,4" },
                    { typeof(Microsoft.UI.Xaml.GridLength), new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Auto), "Auto" },
                    { typeof(Microsoft.UI.Xaml.GridLength), new Microsoft.UI.Xaml.GridLength(40), "40" },
                    { typeof(Microsoft.UI.Xaml.GridLength), new Microsoft.UI.Xaml.GridLength(2, Microsoft.UI.Xaml.GridUnitType.Star), "2*" },
                    { typeof(Windows.UI.Color), Microsoft.UI.Colors.Blue, "#FF0000FF" },
                    { typeof(Windows.UI.Text.FontWeight), Microsoft.UI.Text.FontWeights.Bold, "Bold" },
                    { typeof(Point), new Point(1.5, 2.5), "1.5,2.5" },
                    { typeof(Rect), new Rect(1, 2, 3.5, 4), "1,2,3.5,4" },
                    { typeof(Size), new Size(3.5, 4), "3.5,4" },
                    { typeof(Microsoft.UI.Xaml.Duration), new Microsoft.UI.Xaml.Duration(TimeSpan.FromSeconds(1)), "00:00:01" },
                    { typeof(Microsoft.UI.Xaml.Duration), Microsoft.UI.Xaml.Duration.Forever, "Forever" },
                    { typeof(Microsoft.UI.Xaml.Duration), Microsoft.UI.Xaml.Duration.Automatic, "Automatic" },
                    { typeof(Matrix), new Matrix(1, 2, 3, 4, 5.5, 6), "1,2,3,4,5.5,6" },
                    { typeof(System.Numerics.Vector3), new System.Numerics.Vector3(1, 2.5f, 3), "1,2.5,3" },
                };

                for (int i = 0; i < cases.GetLength(0); i++) {
                    var declared = (Type)cases[i, 0];
                    var value = cases[i, 1];
                    var expected = (string)cases[i, 2];
                    var text = Format(declared, value);
                    Assert.AreEqual(expected, text, "Wrong text for " + declared.Name + " " + value);
                    Assert.AreEqual(value, ReadBack(declared, text),
                        "Round trip changed the value for " + declared.Name);
                }
            });
        }

        [Test]
        public void FormatsEveryReferenceTypeInTheTableAndReadsItBackWithTheSameContent()
        {
            WinUITestHost.Run(() => {
                // None of these five implements value equality, so a correct round trip still
                // compares unequal under Equals. Assert on content.
                var family = new FontFamily("Segoe UI");
                Assert.AreEqual("Segoe UI", Format(typeof(FontFamily), family));
                Assert.AreEqual("Segoe UI", ((FontFamily)ReadBack(typeof(FontFamily), "Segoe UI")).Source);

                var points = new PointCollection();
                points.Add(new Point(1, 2));
                points.Add(new Point(3.5, 4));
                Assert.AreEqual("1,2 3.5,4", Format(typeof(PointCollection), points));
                var readPoints = (PointCollection)ReadBack(typeof(PointCollection), "1,2 3.5,4");
                Assert.AreEqual(2, readPoints.Count);
                Assert.AreEqual(3.5, readPoints[1].X);

                var doubles = new DoubleCollection();
                doubles.Add(1);
                doubles.Add(2.5);
                doubles.Add(3);
                Assert.AreEqual("1,2.5,3", Format(typeof(DoubleCollection), doubles));
                var readDoubles = (DoubleCollection)ReadBack(typeof(DoubleCollection), "1,2.5,3");
                Assert.AreEqual(3, readDoubles.Count);
                Assert.AreEqual(2.5, readDoubles[1]);

                var brush = new SolidColorBrush(Microsoft.UI.Colors.Blue);
                Assert.AreEqual("Blue", Format(typeof(Brush), brush),
                    "A named colour must stay named, as XamlDocument's own dictionary emits it");
                Assert.AreEqual(Microsoft.UI.Colors.Blue,
                    ((SolidColorBrush)ReadBack(typeof(Brush), "Blue")).Color);

                var unnamed = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 1, 2, 3));
                Assert.AreEqual("#FF010203", Format(typeof(Brush), unnamed));

                var geometry = BuildAllSegmentGeometry();
                Assert.AreEqual(GeometryMiniLanguageWriter.Write(geometry),
                    Format(typeof(Geometry), geometry));
            });
        }

        [Test]
        public void FormatsTheSameUnderACommaDecimalCulture()
        {
            WinUITestHost.Run(() => {
                var previous = CultureInfo.CurrentCulture;
                try {
                    CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                    // Point.ToString() yields '1,5;2,5' under this culture, which is why no
                    // formatter is allowed to reach ToString().
                    Assert.AreEqual("1.5,2.5", Format(typeof(Point), new Point(1.5, 2.5)));
                    Assert.AreEqual("1,2.5,3,4",
                        Format(typeof(Microsoft.UI.Xaml.Thickness), new Microsoft.UI.Xaml.Thickness(1, 2.5, 3, 4)));
                    Assert.AreEqual("1,2,3.5,4", Format(typeof(Rect), new Rect(1, 2, 3.5, 4)));
                    Assert.AreEqual("00:00:01",
                        Format(typeof(Microsoft.UI.Xaml.Duration), new Microsoft.UI.Xaml.Duration(TimeSpan.FromSeconds(1))));
                    Assert.AreEqual("1,2.5,3",
                        Format(typeof(System.Numerics.Vector3), new System.Numerics.Vector3(1, 2.5f, 3)));

                    var doubles = new DoubleCollection();
                    doubles.Add(2.5);
                    Assert.AreEqual("2.5", Format(typeof(DoubleCollection), doubles));
                } finally {
                    CultureInfo.CurrentCulture = previous;
                }
            });
        }

        [Test]
        public void RefusesInstancesItCannotFormatFaithfully()
        {
            WinUITestHost.Run(() => {
                Assert.IsTrue(XamlValueFormatter.HasFormatter(typeof(Brush)),
                    "The type-level answer stays true so the PropertyGrid text box is enabled");
                Assert.IsFalse(XamlValueFormatter.CanFormat(typeof(Brush), new LinearGradientBrush()),
                    "A gradient brush has no attribute form");
                Assert.IsFalse(XamlValueFormatter.CanFormat(typeof(Geometry), new RectangleGeometry()));

                // No case here exercises FontFamily's empty-Source rejection: a live FontFamily
                // with an empty Source cannot be constructed on this SDK at all (see the comment
                // beside the predicate in XamlValueFormatter.cs), so that branch is verified by
                // inspection, not by a test.
                Assert.IsFalse(
                    XamlValueFormatter.CanFormat(typeof(Windows.UI.Text.FontWeight),
                        new Windows.UI.Text.FontWeight { Weight = 137 }),
                    "137 has no name, and Phase A cannot read the numeric form back");
                Assert.IsFalse(XamlValueFormatter.CanFormat(typeof(Point), null),
                    "A null value has no text form");
            });
        }

        [Test]
        public void HasNoFormatterForTypesOutsideTheTable()
        {
            WinUITestHost.Run(() => {
                Assert.IsFalse(XamlValueFormatter.HasFormatter(typeof(object)));
                Assert.IsFalse(XamlValueFormatter.HasFormatter(typeof(System.Numerics.Vector2)),
                    "Vector2 has no host entry either, so it could not be round-trip tested");
                Assert.IsFalse(XamlValueFormatter.HasFormatter(typeof(Microsoft.UI.Xaml.Controls.Button)));
            });
        }

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

        [Test]
        public void SavesAGeometryAsAttributeTextInsteadOfAnEmptyElement()
        {
            WinUITestHost.Run(() => {
                var document = ParseDocument("<Path xmlns='" + PresentationNs + "'/>");
                var geometry = BuildAllSegmentGeometry();
                SetThroughTheDocument(document, "Data", geometry);

                var saved = SaveToString(document);
                StringAssert.DoesNotContain("<PathGeometry", saved,
                    "The geometry was still serialized as an empty element");
                StringAssert.Contains("A5,7.5 30 1 1 140,10", saved);

                var reloaded = ParseDocument(saved);
                var reloadedGeometry = reloaded.RootElement.FindOrCreateProperty("Data").ValueOnInstance as PathGeometry;
                Assert.IsNotNull(reloadedGeometry, "The saved document did not reload a PathGeometry");
                Assert.AreEqual(GeometryMiniLanguageWriter.Write(geometry),
                    GeometryMiniLanguageWriter.Write(reloadedGeometry));
            });
        }

        [Test]
        public void SavesAPointCollectionAsAttributeTextInsteadOfAnEmptyElement()
        {
            WinUITestHost.Run(() => {
                var document = ParseDocument("<Polyline xmlns='" + PresentationNs + "'/>");
                var points = new PointCollection();
                points.Add(new Point(10, 10));
                points.Add(new Point(50, 10));
                points.Add(new Point(50, 40));
                SetThroughTheDocument(document, "Points", points);

                var saved = SaveToString(document);
                StringAssert.Contains("10,10 50,10 50,40", saved);

                var reloaded = ParseDocument(saved);
                var reloadedPoints = reloaded.RootElement.FindOrCreateProperty("Points").ValueOnInstance as PointCollection;
                Assert.IsNotNull(reloadedPoints);
                Assert.AreEqual(3, reloadedPoints.Count);
                Assert.AreEqual(50.0, reloadedPoints[2].X);
                Assert.AreEqual(40.0, reloadedPoints[2].Y);
            });
        }

        [Test]
        public void SavesAThicknessAsAttributeText()
        {
            WinUITestHost.Run(() => {
                var document = ParseDocument("<Grid xmlns='" + PresentationNs + "'/>");
                SetThroughTheDocument(document, "Margin", new Microsoft.UI.Xaml.Thickness(1, 2.5, 3, 4));
                StringAssert.Contains("Margin=\"1,2.5,3,4\"", SaveToString(document));
            });
        }

        [Test]
        public void KeepsSerializingANestedControlAsAnElement()
        {
            WinUITestHost.Run(() => {
                // ContentControl.Content is declared `object`. Resolving through
                // GetCustomTypeConverter would hand back StringConverter, whose
                // ConvertToInvariantString(new Button()) is the literal text
                // 'Microsoft.UI.Xaml.Controls.Button' - the whole subtree, replaced by a type name.
                var document = ParseDocument("<Button xmlns='" + PresentationNs + "'/>");
                SetThroughTheDocument(document, "Content", new Microsoft.UI.Xaml.Controls.TextBlock());

                var saved = SaveToString(document);
                StringAssert.Contains("<TextBlock", saved, "The nested control was not serialized as an element");
                StringAssert.DoesNotContain("Microsoft.UI.Xaml.Controls.TextBlock", saved,
                    "The nested control was serialized as its type name");
            });
        }

        [Test]
        public void StillParsesADocumentContainingMarkupExtensions()
        {
            WinUITestHost.Run(() => {
                // CreatePropertyValue is also CreateObject's body, and MarkupExtensionParser.cs:403
                // runs it for every markup extension at parse time.
                var document = ParseDocument(
                    "<Grid xmlns='" + PresentationNs + "' " +
                    "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' " +
                    "Tag='{Binding Path=Whatever}'/>");
                Assert.IsNotNull(document.RootElement);
            });
        }

        [Test]
        public void StillSavesANamedColourByName()
        {
            WinUITestHost.Run(() => {
                var document = ParseDocument("<Grid xmlns='" + PresentationNs + "'/>");
                SetThroughTheDocument(document, "Background", new SolidColorBrush(Microsoft.UI.Colors.Blue));
                var saved = SaveToString(document);
                StringAssert.Contains("Background=\"Blue\"", saved);
            });
        }

        [Test]
        public void LeavesAnUnformattableInstanceOnTheElementPath()
        {
            WinUITestHost.Run(() => {
                var document = ParseDocument("<Grid xmlns='" + PresentationNs + "'/>");
                SetThroughTheDocument(document, "Background", new LinearGradientBrush());
                var saved = SaveToString(document);
                StringAssert.Contains("<LinearGradientBrush", saved,
                    "A gradient brush has no attribute form and must keep the element form");
            });
        }

        [Test]
        public void PreservesTheLiveGeometryInstanceAndItsSegmentTypesAfterASetValue()
        {
            WinUITestHost.Run(() => {
                var document = ParseDocument("<Path xmlns='" + PresentationNs + "'/>");

                var points = new PointCollection();
                points.Add(new Point(20, 20));
                points.Add(new Point(30, 20));
                points.Add(new Point(40, 10));
                var figure = new PathFigure { StartPoint = new Point(10, 10) };
                figure.Segments.Add(new PolyLineSegment { Points = points });
                var geometry = new PathGeometry();
                geometry.Figures.Add(figure);

                var property = document.RootElement.FindOrCreateProperty("Data");
                property.PropertyValue = document.CreatePropertyValue(geometry, property);

                var saved = SaveToString(document);
                StringAssert.Contains(GeometryMiniLanguageWriter.Write(geometry), saved,
                    "The XML must still carry the mini-language attribute text - that is Phase B's whole point");

                Assert.AreSame(geometry, property.ValueOnInstance,
                    "The live geometry instance must survive a SetValue - every Designer extension holds a reference into it");
                var liveFigure = ((PathGeometry)property.ValueOnInstance).Figures[0];
                Assert.IsInstanceOf<PolyLineSegment>(liveFigure.Segments[0],
                    "A PolyLineSegment must not be normalized into individual segments by round-tripping through the mini-language");
            });
        }

        [Test]
        public void RewritingTheTextInvalidatesTheCachedOriginalValue()
        {
            WinUITestHost.Run(() => {
                var document = ParseDocument("<Path xmlns='" + PresentationNs + "'/>");
                var property = document.RootElement.FindOrCreateProperty("Data");

                var original = BuildAllSegmentGeometry();
                property.PropertyValue = document.CreatePropertyValue(original, property);
                Assert.AreSame(original, property.ValueOnInstance, "sanity: the cached instance is in place first");

                var replacementFigure = new PathFigure { StartPoint = new Point(0, 0) };
                replacementFigure.Segments.Add(new LineSegment { Point = new Point(5, 5) });
                var replacementGeometry = new PathGeometry();
                replacementGeometry.Figures.Add(replacementFigure);
                var newText = GeometryMiniLanguageWriter.Write(replacementGeometry);

                // Simulate a PropertyGrid text-box commit rewriting the attribute in place, the way
                // XamlModelProperty.cs:239's own comment on SetThroughTheDocument describes -
                // except here the EXISTING XamlTextValue's Text is mutated directly, which is
                // exactly the path the cached originalValue must not survive.
                ((XamlTextValue)property.PropertyValue).Text = newText;
                property.UpdateValueOnInstance();

                var reparsed = property.ValueOnInstance as PathGeometry;
                Assert.IsNotNull(reparsed, "the rewritten text must still parse to a PathGeometry");
                Assert.AreNotSame(original, reparsed,
                    "the stale cached geometry must not be returned once the text it was cached for has changed");
                Assert.AreEqual(newText, GeometryMiniLanguageWriter.Write(reparsed));
            });
        }

        sealed class CollectingErrorSink : IXamlErrorSink
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

        static CollectingErrorSink SetCollectingErrors(string xaml, string propertyName, object value)
        {
            var sink = new CollectingErrorSink();
            var settings = new XamlParserSettings();
            settings.ServiceProvider = new SingleServiceProvider(typeof(IXamlErrorSink), sink);
            var document = XamlParser.Parse(new System.IO.StringReader(xaml), settings);
            var property = document.RootElement.FindOrCreateProperty(propertyName);
            property.PropertyValue = document.CreatePropertyValue(value, property);
            return sink;
        }

        [Test]
        public void ReportsAValueItCannotWriteAsAttributeText()
        {
            WinUITestHost.Run(() => {
                var sink = SetCollectingErrors(
                    "<Grid xmlns='" + PresentationNs + "'/>", "Background", new LinearGradientBrush());
                Assert.AreEqual(1, sink.Messages.Count, "The unwritable value was not reported");
                StringAssert.Contains("Background", sink.Messages[0]);
                StringAssert.Contains("LinearGradientBrush", sink.Messages[0]);
            });
        }

        [Test]
        public void ReportsAGeometryTheMiniLanguageCannotExpress()
        {
            WinUITestHost.Run(() => {
                var sink = SetCollectingErrors(
                    "<Path xmlns='" + PresentationNs + "'/>", "Data", new RectangleGeometry());
                Assert.AreEqual(1, sink.Messages.Count);
                StringAssert.Contains("RectangleGeometry", sink.Messages[0]);
            });
        }

        [Test]
        public void DoesNotReportAValueItWroteSuccessfully()
        {
            WinUITestHost.Run(() => {
                var sink = SetCollectingErrors(
                    "<Grid xmlns='" + PresentationNs + "'/>", "Margin",
                    new Microsoft.UI.Xaml.Thickness(1, 2, 3, 4));
                Assert.AreEqual(0, sink.Messages.Count,
                    "A value that serialized fine must not report anything");
            });
        }

        [Test]
        public void DoesNotReportAValueThatWasNeverInTheTable()
        {
            WinUITestHost.Run(() => {
                // A nested control has always been serialized as an element and always will be.
                // Reporting it would blame a document that is not at fault.
                var sink = SetCollectingErrors(
                    "<Button xmlns='" + PresentationNs + "'/>", "Content",
                    new Microsoft.UI.Xaml.Controls.TextBlock());
                Assert.AreEqual(0, sink.Messages.Count);
            });
        }
    }
}
