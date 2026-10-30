using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace WinUIDesigner.Shell
{
    /// <summary>
    /// Saves what a document's design looks like as a PNG: its root element, drawn by
    /// <see cref="RenderTargetBitmap"/> without the adorners, which live in a sibling layer.
    /// Export-image spec.
    /// </summary>
    internal static class DesignImageExport
    {
        /// <summary>
        /// Why <paramref name="doc"/> cannot be exported right now, or null if it can. Checked
        /// before the save picker opens, so the user is not asked for a file name first.
        /// </summary>
        internal static string Problem(Document doc)
        {
            if (doc == null)
                return "There is no document to export.";
            // The surface is collapsed in XAML mode, and RenderTargetBitmap draws nothing that
            // is not visible. Switching views here could fail on the text (spec Decision 3).
            if (doc.InXamlMode)
                return "Switch to the Design view to export an image.";
            if (doc.LoadFailed || RootView(doc) == null)
                return "The document has no design to export. Fix the errors in its XAML first.";
            return null;
        }

        /// <summary>
        /// Renders <paramref name="doc"/>'s root element and writes it to <paramref name="path"/>
        /// as a PNG. Returns the error to report, or null on success; nothing is written on
        /// failure.
        /// </summary>
        internal static async Task<string> ExportAsync(Document doc, string path)
        {
            var problem = Problem(doc);
            if (problem != null)
                return problem;

            byte[] png;
            try
            {
                png = await RenderPngAsync(RootView(doc));
            }
            catch (Exception ex)
            {
                return "The design could not be drawn: " + ex.Message;
            }

            try
            {
                File.WriteAllBytes(path, png);
            }
            catch (Exception ex)
            {
                return "The image could not be saved to " + path + ": " + ex.Message;
            }
            return null;
        }

        /// <summary>
        /// Draws <paramref name="element"/> at the display's scale and encodes it as PNG, with its
        /// transparency kept. The element must be in a live, visible tree.
        /// </summary>
        internal static async Task<byte[]> RenderPngAsync(UIElement element)
        {
            var bitmap = await RenderWholeAsync(element);
            if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0)
                throw new InvalidOperationException("it has no size on screen.");

            var pixels = await bitmap.GetPixelsAsync();
            double dpi = 96 * (element.XamlRoot != null ? element.XamlRoot.RasterizationScale : 1.0);

            using (var stream = new InMemoryRandomAccessStream())
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                // RenderTargetBitmap's pixels are BGRA8 with premultiplied alpha.
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, dpi, dpi, pixels.ToArray());
                await encoder.FlushAsync();

                using (var reader = new DataReader(stream.GetInputStreamAt(0)))
                {
                    await reader.LoadAsync((uint)stream.Size);
                    var bytes = new byte[stream.Size];
                    reader.ReadBytes(bytes);
                    return bytes;
                }
            }
        }

        /// <summary>
        /// Draws <paramref name="element"/> at its full size. RenderTargetBitmap draws only the
        /// bounds of what is visibly painted, so a panel with a null Background - every new
        /// document's root Canvas - came out cropped to its children, or empty with none
        /// (measured on the runner, thumbnail plan Results). For the render, such an element
        /// gets a Transparent background on the live object only, restored afterwards; the
        /// document's XAML is serialized from its model and never sees it.
        /// </summary>
        internal static async Task<RenderTargetBitmap> RenderWholeAsync(UIElement element)
        {
            var bitmap = new RenderTargetBitmap();
            var property = NullBackgroundProperty(element);
            if (property != null)
                element.SetValue(property, new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent));
            try
            {
                await bitmap.RenderAsync(element);
            }
            finally
            {
                if (property != null)
                    element.ClearValue(property);
            }
            return bitmap;
        }

        /// <summary>The Background property of a Panel, Border or Control whose Background is
        /// unset (a local value is left alone), or null.</summary>
        static DependencyProperty NullBackgroundProperty(UIElement element)
        {
            DependencyProperty property = null;
            if (element is Microsoft.UI.Xaml.Controls.Panel)
                property = Microsoft.UI.Xaml.Controls.Panel.BackgroundProperty;
            else if (element is Microsoft.UI.Xaml.Controls.Border)
                property = Microsoft.UI.Xaml.Controls.Border.BackgroundProperty;
            else if (element is Microsoft.UI.Xaml.Controls.Control)
                property = Microsoft.UI.Xaml.Controls.Control.BackgroundProperty;
            if (property == null || element.ReadLocalValue(property) != DependencyProperty.UnsetValue
                || element.GetValue(property) != null)
                return null;
            return property;
        }

        static UIElement RootView(Document doc)
        {
            var context = doc.DesignContext;
            if (context == null || context.RootItem == null)
                return null;
            return context.RootItem.View;
        }
    }
}
