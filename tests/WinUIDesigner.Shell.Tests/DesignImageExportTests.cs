using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Shell.Tests
{
    /// <summary>
    /// File > Export as Image: a document's root element drawn to a PNG file (export-image spec).
    /// </summary>
    [TestFixture]
    public class DesignImageExportTests
    {
        const string RedCanvas =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "Width=\"80\" Height=\"60\" Background=\"Red\" />";

        string path;

        [SetUp]
        public void NewPath()
        {
            path = Path.Combine(Path.GetTempPath(), "wd-export-" + Guid.NewGuid().ToString("N") + ".png");
        }

        [TearDown]
        public void Clean()
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        [Test]
        public void ExportAsync_WritesTheRootElementAsAPng()
        {
            WinUITestHost.RunWithWindowAsync(async window =>
            {
                using (var doc = new Document(null, RedCanvas))
                {
                    window.Content = doc.DesignSurface;
                    doc.DesignSurface.UpdateLayout();
                    double scale = doc.DesignSurface.XamlRoot.RasterizationScale;

                    var error = await DesignImageExport.ExportAsync(doc, path);

                    Assert.That(error, Is.Null);
                    var bytes = File.ReadAllBytes(path);
                    Assert.That(bytes.Length, Is.GreaterThan(8));
                    Assert.That(new[] { bytes[0], bytes[1], bytes[2], bytes[3] },
                        Is.EqualTo(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }));

                    var decoded = await DecodeAsync(bytes);
                    Assert.That(decoded.Width, Is.EqualTo((uint)Math.Round(80 * scale)).Within(1));
                    Assert.That(decoded.Height, Is.EqualTo((uint)Math.Round(60 * scale)).Within(1));
                    // The middle pixel is the canvas's red, opaque (BGRA).
                    int i = (int)(((decoded.Height / 2) * decoded.Width + decoded.Width / 2) * 4);
                    Assert.That(new[] { decoded.Pixels[i], decoded.Pixels[i + 1], decoded.Pixels[i + 2], decoded.Pixels[i + 3] },
                        Is.EqualTo(new byte[] { 0, 0, 255, 255 }));
                }
            });
        }

        /// <remarks>
        /// RenderTargetBitmap draws only what is painted, so without the temporary background
        /// this came out as the 10x10 child alone (measured on the runner).
        /// </remarks>
        [Test]
        public void ExportAsync_RootWithNoBackground_IsDrawnWholeAndKeepsItsNullBackground()
        {
            WinUITestHost.RunWithWindowAsync(async window =>
            {
                using (var doc = new Document(null,
                    "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Width=\"80\" Height=\"60\">" +
                    "<Border Canvas.Left=\"5\" Canvas.Top=\"5\" Width=\"10\" Height=\"10\" Background=\"Red\" /></Canvas>"))
                {
                    window.Content = doc.DesignSurface;
                    doc.DesignSurface.UpdateLayout();
                    double scale = doc.DesignSurface.XamlRoot.RasterizationScale;

                    var error = await DesignImageExport.ExportAsync(doc, path);

                    Assert.That(error, Is.Null);
                    var decoded = await DecodeAsync(File.ReadAllBytes(path));
                    Assert.That(decoded.Width, Is.EqualTo((uint)Math.Round(80 * scale)).Within(1));
                    Assert.That(decoded.Height, Is.EqualTo((uint)Math.Round(60 * scale)).Within(1));
                    var root = (Microsoft.UI.Xaml.Controls.Canvas)doc.DesignContext.RootItem.View;
                    Assert.That(root.Background, Is.Null, "the temporary background is removed again");
                    Assert.That(doc.Text, Does.Not.Contain("Background=\"Transparent\""));
                }
            });
        }

        [Test]
        public void ExportAsync_InXamlMode_ReportsAndWritesNothing()
        {
            WinUITestHost.RunAsync(async () =>
            {
                using (var doc = new Document(null, RedCanvas))
                {
                    doc.Mode = DocumentMode.Xaml;

                    var error = await DesignImageExport.ExportAsync(doc, path);

                    Assert.That(error, Does.Contain("Design view"));
                    Assert.That(File.Exists(path), Is.False);
                }
            });
        }

        [Test]
        public void ExportAsync_DocumentThatDidNotLoad_ReportsAndWritesNothing()
        {
            WinUITestHost.RunAsync(async () =>
            {
                using (var doc = new Document(null, "<Canvas"))
                {
                    var error = await DesignImageExport.ExportAsync(doc, path);

                    Assert.That(error, Does.Contain("no design"));
                    Assert.That(File.Exists(path), Is.False);
                }
            });
        }

        [Test]
        public void ExportAsync_RootWithNoSize_ReportsAndWritesNothing()
        {
            WinUITestHost.RunWithWindowAsync(async window =>
            {
                using (var doc = new Document(null,
                    "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Width=\"0\" Height=\"0\" />"))
                {
                    window.Content = doc.DesignSurface;
                    doc.DesignSurface.UpdateLayout();

                    var error = await DesignImageExport.ExportAsync(doc, path);

                    Assert.That(error, Does.StartWith("The design could not be drawn"));
                    Assert.That(File.Exists(path), Is.False);
                }
            });
        }

        [Test]
        public void Problem_NoDocument()
        {
            Assert.That(DesignImageExport.Problem(null), Is.Not.Null);
        }

        sealed class Decoded
        {
            public uint Width;
            public uint Height;
            public byte[] Pixels;
        }

        static async Task<Decoded> DecodeAsync(byte[] png)
        {
            using (var stream = new InMemoryRandomAccessStream())
            {
                using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(png);
                    await writer.StoreAsync();
                    await writer.FlushAsync();
                    writer.DetachStream();
                }
                var decoder = await BitmapDecoder.CreateAsync(stream);
                var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
                return new Decoded
                {
                    Width = decoder.PixelWidth,
                    Height = decoder.PixelHeight,
                    Pixels = data.DetachPixelData()
                };
            }
        }
    }
}
