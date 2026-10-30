using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace WinUIDesigner.Shell
{
    /// <summary>
    /// A small picture of the current document's design with a frame around the part the
    /// surface is showing. Pressing or dragging on it scrolls the surface there. Thumbnail spec.
    /// </summary>
    public sealed class ThumbnailView : Grid
    {
        /// <summary>How long the design must stay unchanged before it is drawn again.</summary>
        static readonly TimeSpan RenderDelay = TimeSpan.FromMilliseconds(300);

        readonly Image image = new Image { Stretch = Stretch.Fill };
        readonly Canvas overlay = new Canvas();
        readonly Rectangle frame = new Rectangle
        {
            Stroke = new SolidColorBrush(Colors.DodgerBlue),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(0x30, 0x1E, 0x90, 0xFF)),
            IsHitTestVisible = false
        };
        readonly DispatcherQueueTimer timer;

        Document document;
        ScrollViewer scroll;
        FrameworkElement scrolled;
        Size contentSize;
        Rect imageRect = Rect.Empty;
        bool rendering;
        bool renderAgain;
        bool dragging;

        public ThumbnailView()
        {
            // A Background so the empty parts of the pane take pointer input too.
            Background = new SolidColorBrush(Colors.Transparent);
            image.HorizontalAlignment = HorizontalAlignment.Left;
            image.VerticalAlignment = VerticalAlignment.Top;
            overlay.Children.Add(image);
            overlay.Children.Add(frame);
            Children.Add(overlay);
            frame.Visibility = Visibility.Collapsed;

            timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            timer.Interval = RenderDelay;
            timer.IsRepeating = false;
            timer.Tick += (s, e) => Fire(RenderNowAsync);

            SizeChanged += (s, e) => Layout();
            PointerPressed += OnPointerPressed;
            PointerMoved += OnPointerMoved;
            PointerReleased += OnPointerReleased;
            PointerCaptureLost += (s, e) => dragging = false;
        }

        /// <summary>Follows <paramref name="documentView"/>, or shows nothing when it is null.</summary>
        public void Attach(DocumentView documentView)
        {
            if (documentView == null)
                Attach(null, null, null);
            else
                Attach(documentView.Document, documentView.SurfaceScroll, documentView.SurfaceContent);
        }

        /// <summary>
        /// Follows <paramref name="doc"/>, shown by <paramref name="surfaceScroll"/> whose content
        /// is <paramref name="surfaceContent"/>. The seam tests use, without a DocumentView and its
        /// TextBox in the test window (CLAUDE.md).
        /// </summary>
        internal void Attach(Document doc, ScrollViewer surfaceScroll, FrameworkElement surfaceContent)
        {
            if (ReferenceEquals(document, doc) && ReferenceEquals(scroll, surfaceScroll))
                return;
            Detach();
            if (doc == null)
                return;
            document = doc;
            scroll = surfaceScroll;
            scrolled = surfaceContent;
            document.DesignChanged += OnDesignChanged;
            document.PropertyChanged += OnDocumentPropertyChanged;
            scroll.ViewChanged += OnViewChanged;
            scroll.SizeChanged += OnScrollSizeChanged;
            ScheduleRender();
        }

        void Detach()
        {
            if (document != null)
            {
                document.DesignChanged -= OnDesignChanged;
                document.PropertyChanged -= OnDocumentPropertyChanged;
                scroll.ViewChanged -= OnViewChanged;
                scroll.SizeChanged -= OnScrollSizeChanged;
            }
            document = null;
            scroll = null;
            scrolled = null;
            timer.Stop();
            Clear();
        }

        void OnDesignChanged(object sender, EventArgs e)
        {
            ScheduleRender();
        }

        void OnDocumentPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // A mode switch or Refresh builds a new tree; in XAML mode there is nothing to draw.
            if (e.PropertyName == "Mode" || e.PropertyName == "DesignContext")
                ScheduleRender();
        }

        void OnViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            UpdateFrame();
        }

        void OnScrollSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateFrame();
        }

        void ScheduleRender()
        {
            timer.Stop();
            timer.Start();
        }

        /// <summary>
        /// Draws the design now. The timer calls this once changes settle; tests call it directly.
        /// </summary>
        internal async Task RenderNowAsync()
        {
            if (rendering)
            {
                renderAgain = true;
                return;
            }
            rendering = true;
            try
            {
                do
                {
                    renderAgain = false;
                    await RenderOnceAsync();
                } while (renderAgain);
            }
            finally
            {
                rendering = false;
            }
        }

        async Task RenderOnceAsync()
        {
            var root = RootView();
            if (root == null)
            {
                Clear();
                return;
            }

            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(root);
            // The view may have changed while the bitmap was drawn.
            if (!ReferenceEquals(root, RootView()))
                return;
            if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0)
            {
                Clear();
                return;
            }

            // A root with no size of its own is drawn at its content's bounds (export-image plan
            // Results), so the picture's own size is the fallback.
            double scale = root.XamlRoot != null ? root.XamlRoot.RasterizationScale : 1.0;
            var fe = root as FrameworkElement;
            contentSize = fe != null && fe.ActualWidth > 0 && fe.ActualHeight > 0
                ? new Size(fe.ActualWidth, fe.ActualHeight)
                : new Size(bitmap.PixelWidth / scale, bitmap.PixelHeight / scale);
            image.Source = bitmap;
            Layout();
        }

        void Clear()
        {
            image.Source = null;
            contentSize = new Size(0, 0);
            imageRect = Rect.Empty;
            frame.Visibility = Visibility.Collapsed;
        }

        UIElement RootView()
        {
            if (document == null || !document.InDesignMode)
                return null;
            var context = document.DesignContext;
            if (context == null || context.RootItem == null)
                return null;
            return context.RootItem.View;
        }

        void Layout()
        {
            imageRect = ImageRect(new Size(ActualWidth, ActualHeight), contentSize);
            if (imageRect.IsEmpty)
            {
                frame.Visibility = Visibility.Collapsed;
                return;
            }
            Canvas.SetLeft(image, imageRect.X);
            Canvas.SetTop(image, imageRect.Y);
            image.Width = imageRect.Width;
            image.Height = imageRect.Height;
            UpdateFrame();
        }

        void UpdateFrame()
        {
            var visible = VisibleRegion();
            var rect = imageRect.IsEmpty || visible == null
                ? Rect.Empty
                : FrameRect(imageRect, contentSize, visible.Value);
            if (rect.IsEmpty)
            {
                frame.Visibility = Visibility.Collapsed;
                return;
            }
            Canvas.SetLeft(frame, rect.X);
            Canvas.SetTop(frame, rect.Y);
            frame.Width = rect.Width;
            frame.Height = rect.Height;
            frame.Visibility = Visibility.Visible;
        }

        /// <summary>The part of the root the surface shows, in the root's own coordinates.</summary>
        Rect? VisibleRegion()
        {
            var root = RootView();
            if (root == null)
                return null;
            Point origin;
            try
            {
                origin = root.TransformToVisual(scrolled).TransformPoint(new Point(0, 0));
            }
            catch (Exception)
            {
                // Not in the same live tree yet.
                return null;
            }
            double zoom = scroll.ZoomFactor;
            return new Rect(
                scroll.HorizontalOffset / zoom - origin.X,
                scroll.VerticalOffset / zoom - origin.Y,
                scroll.ViewportWidth / zoom,
                scroll.ViewportHeight / zoom);
        }

        void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (imageRect.IsEmpty)
                return;
            dragging = CapturePointer(e.Pointer);
            ScrollTo(e.GetCurrentPoint(this).Position);
            e.Handled = true;
        }

        void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!dragging)
                return;
            ScrollTo(e.GetCurrentPoint(this).Position);
            e.Handled = true;
        }

        void OnPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!dragging)
                return;
            dragging = false;
            ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }

        /// <summary>
        /// Scrolls the surface so the point under <paramref name="thumbnailPoint"/> is in the
        /// middle of the view, as far as the scroll range allows.
        /// </summary>
        internal void ScrollTo(Point thumbnailPoint)
        {
            var root = RootView();
            if (root == null || imageRect.IsEmpty)
                return;
            var target = ContentPoint(imageRect, contentSize, thumbnailPoint);
            Point origin;
            try
            {
                origin = root.TransformToVisual(scrolled).TransformPoint(new Point(0, 0));
            }
            catch (Exception)
            {
                return;
            }
            double zoom = scroll.ZoomFactor;
            scroll.ChangeView(
                Math.Max(0, (target.X + origin.X) * zoom - scroll.ViewportWidth / 2),
                Math.Max(0, (target.Y + origin.Y) * zoom - scroll.ViewportHeight / 2),
                null, true);
        }

        /// <summary>
        /// Where a picture of <paramref name="content"/> sits in a pane of size
        /// <paramref name="pane"/>: scaled to fit, never enlarged, centred.
        /// </summary>
        internal static Rect ImageRect(Size pane, Size content)
        {
            if (pane.Width <= 0 || pane.Height <= 0 || content.Width <= 0 || content.Height <= 0)
                return Rect.Empty;
            double scale = Math.Min(1.0, Math.Min(pane.Width / content.Width, pane.Height / content.Height));
            double w = content.Width * scale, h = content.Height * scale;
            return new Rect((pane.Width - w) / 2, (pane.Height - h) / 2, w, h);
        }

        /// <summary>
        /// The frame for <paramref name="visible"/> (in content coordinates) over a picture of
        /// <paramref name="content"/> drawn at <paramref name="image"/>, clipped to the picture.
        /// Empty when none of the content is visible.
        /// </summary>
        internal static Rect FrameRect(Rect image, Size content, Rect visible)
        {
            if (image.IsEmpty || content.Width <= 0 || content.Height <= 0)
                return Rect.Empty;
            double left = Math.Max(0, visible.X), top = Math.Max(0, visible.Y);
            double right = Math.Min(content.Width, visible.X + visible.Width);
            double bottom = Math.Min(content.Height, visible.Y + visible.Height);
            if (right <= left || bottom <= top)
                return Rect.Empty;
            double s = image.Width / content.Width;
            return new Rect(image.X + left * s, image.Y + top * s, (right - left) * s, (bottom - top) * s);
        }

        /// <summary>The content point under <paramref name="thumbnailPoint"/>, clamped to the content.</summary>
        internal static Point ContentPoint(Rect image, Size content, Point thumbnailPoint)
        {
            double s = image.Width / content.Width;
            double x = (thumbnailPoint.X - image.X) / s, y = (thumbnailPoint.Y - image.Y) / s;
            return new Point(Math.Max(0, Math.Min(content.Width, x)), Math.Max(0, Math.Min(content.Height, y)));
        }

        /// <summary>Test seams.</summary>
        internal ImageSource ImageSourceForTest { get { return image.Source; } }
        internal Rect ImageRectForTest { get { return imageRect; } }
        internal Visibility FrameVisibilityForTest { get { return frame.Visibility; } }
        internal Rect FrameRectForTest
        {
            get { return new Rect(Canvas.GetLeft(frame), Canvas.GetTop(frame), frame.Width, frame.Height); }
        }

        /// <summary>
        /// Starts an async operation from an event handler. An exception would otherwise end the
        /// process from an async void; a thumbnail that cannot be drawn is not worth that.
        /// </summary>
        static async void Fire(Func<Task> operation)
        {
            try
            {
                await operation();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[ThumbnailView] " + ex.Message);
            }
        }
    }
}
