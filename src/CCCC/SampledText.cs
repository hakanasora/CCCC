using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CCCC
{
    // Keep WPF layout/baselines, but replace only the glyph drawing. Each control
    // owns one current pair of images, never a history keyed by clipboard text.
    internal sealed class SampledText : Decorator
    {
        private readonly TextBlock layout = new TextBlock { Opacity = 0 };
        private readonly DrawingVisual glyphDrawing = new DrawingVisual();
        private readonly TextPixelWorkspace workspace;
        private bool drawingAttached;
        internal SampledText() : this(new TextPixelWorkspace()) { }
        internal SampledText(TextPixelWorkspace workspace)
        {
            this.workspace = workspace;
            Child = layout;
            AddVisualChild(glyphDrawing); drawingAttached = true;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetBitmapScalingMode(glyphDrawing, BitmapScalingMode.NearestNeighbor);
        }
        protected override int VisualChildrenCount { get { return base.VisualChildrenCount + (drawingAttached ? 1 : 0); } }
        protected override Visual GetVisualChild(int index)
        {
            if (drawingAttached && index == base.VisualChildrenCount) return glyphDrawing;
            return base.GetVisualChild(index);
        }
        // The invisible TextBlock retains the original measuring, truncation and
        // baseline behavior. Only its replacement pixels are composited normally.
        private void Changed() { InvalidateMeasure(); InvalidateVisual(); }
        internal string Text { get { return layout.Text; } set { if (layout.Text == value) return; layout.Text = value; Changed(); } }
        internal FontFamily FontFamily { get { return layout.FontFamily; } set { layout.FontFamily = value; Changed(); } }
        internal FontWeight FontWeight { get { return layout.FontWeight; } set { layout.FontWeight = value; Changed(); } }
        internal FontStyle FontStyle { get { return layout.FontStyle; } set { layout.FontStyle = value; Changed(); } }
        internal FontStretch FontStretch { get { return layout.FontStretch; } set { layout.FontStretch = value; Changed(); } }
        internal double FontSize { get { return layout.FontSize; } set { layout.FontSize = value; Changed(); } }
        internal Brush Foreground
        {
            get { return layout.Foreground; }
            set { if (Object.ReferenceEquals(layout.Foreground, value)) return; layout.Foreground = value; InvalidateVisual(); }
        }
        internal TextTrimming TextTrimming { get { return layout.TextTrimming; } set { layout.TextTrimming = value; Changed(); } }
        internal TextWrapping TextWrapping { get { return layout.TextWrapping; } set { layout.TextWrapping = value; Changed(); } }
        internal double BaselineOffset { get { return layout.BaselineOffset; } }
        internal const int Sampling = 4;
        private BitmapSource high, final;
        private string key;
        private Rect destination;
        private bool moving;
        internal int BuildCount { get; private set; }
        internal double LastBuildMilliseconds { get; private set; }
        internal string LastFailure { get; private set; }
        internal int CachedImages { get { return key == null || high == null ? 0 : 2; } }
        internal int SurfaceAllocationCount { get; private set; }
        internal long SurfacePixelBytes
        {
            get { return (high == null ? 0 : (long)high.PixelWidth * high.PixelHeight * 4) +
                (final == null ? 0 : (long)final.PixelWidth * final.PixelHeight * 4); }
        }

        internal bool Moving
        {
            get { return moving; }
            set
            {
                if (moving == value) return;
                moving = value;
                RenderOptions.SetBitmapScalingMode(this, value ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor);
                RenderOptions.SetBitmapScalingMode(glyphDrawing, value ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor);
                InvalidateVisual();
            }
        }

        internal void ClearCache()
        {
            // Clear retained drawing commands even while the HWND is hidden;
            // invalidating a hidden FrameworkElement alone does not render it.
            using (DrawingContext draw = glyphDrawing.RenderOpen()) { }
            key = null; high = final = null;
            LastFailure = null; InvalidateVisual();
        }

        internal void ParkForIdle(byte[] zeroPixels)
        {
            // Keep empty surface containers under the toast's byte budget rather
            // than allocate new native images at every short hide/show cycle.
            using (DrawingContext draw = glyphDrawing.RenderOpen()) { }
            key = null; LastFailure = null;
            RenderTargetBitmap source = high as RenderTargetBitmap;
            WriteableBitmap reduced = final as WriteableBitmap;
            if (source == null || reduced == null || zeroPixels == null ||
                zeroPixels.Length < reduced.PixelWidth * reduced.PixelHeight * 4)
            { ClearCache(); return; }
            try
            {
                source.Clear();
                reduced.WritePixels(new Int32Rect(0, 0, reduced.PixelWidth, reduced.PixelHeight),
                    zeroPixels, reduced.PixelWidth * 4, 0);
            }
            catch (InvalidOperationException) { ClearCache(); }
            catch (OutOfMemoryException) { ClearCache(); }
            InvalidateVisual();
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            ClearCache(); InvalidateMeasure();
        }

        protected override void OnRender(DrawingContext drawingContext)
        { PrepareForRender(); }

        // Called after layout, before the toast starts its animation clock.
        // OnRender uses this same path as a fallback for later layout/DPI changes.
        internal void PrepareForRender()
        {
            if (String.IsNullOrEmpty(Text) || ActualWidth <= 0 || ActualHeight <= 0)
            {
                using (DrawingContext draw = glyphDrawing.RenderOpen()) { }
                high = final = null; key = null; return;
            }
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            // Align the sample grid to the untransformed window, not the moving
            // RenderTransform. Animation must not cause new glyph rasterization.
            Visual root = this;
            DependencyObject parent;
            while ((parent = VisualTreeHelper.GetParent(root)) is Visual) root = (Visual)parent;
            // Walk layout offsets only; card/count scale are composited later.
            Point origin = new Point();
            for (Visual visual = this; visual != root; )
            {
                Vector offset = VisualTreeHelper.GetOffset(visual);
                origin.X += offset.X; origin.Y += offset.Y;
                visual = VisualTreeHelper.GetParent(visual) as Visual;
                if (visual == null) break;
            }
            double fractionX = origin.X * dpi.DpiScaleX - Math.Floor(origin.X * dpi.DpiScaleX);
            double fractionY = origin.Y * dpi.DpiScaleY - Math.Floor(origin.Y * dpi.DpiScaleY);
            string next = String.Join("|", new string[] { Text, FontFamily.Source, FontWeight.ToString(),
                FontStyle.ToString(), FontStretch.ToString(), FontSize.ToString("R", CultureInfo.InvariantCulture),
                Foreground.ToString(), FlowDirection.ToString(), TextTrimming.ToString(),
                ActualWidth.ToString("R", CultureInfo.InvariantCulture), ActualHeight.ToString("R", CultureInfo.InvariantCulture),
                BaselineOffset.ToString("R", CultureInfo.InvariantCulture), dpi.DpiScaleX.ToString("R", CultureInfo.InvariantCulture),
                dpi.DpiScaleY.ToString("R", CultureInfo.InvariantCulture), fractionX.ToString("F6", CultureInfo.InvariantCulture),
                fractionY.ToString("F6", CultureInfo.InvariantCulture) });
            if (key != next)
            {
                key = next; LastFailure = null;
                using (DrawingContext draw = glyphDrawing.RenderOpen()) { }
                try { Build(dpi, fractionX, fractionY); }
                catch (ArgumentException error) { high = final = null; LastFailure = error.GetType().Name; }
                catch (InvalidOperationException error) { high = final = null; LastFailure = error.GetType().Name; }
                catch (OutOfMemoryException error) { high = final = null; workspace.Release(); LastFailure = error.GetType().Name; }
            }
            layout.Opacity = final == null ? 1 : 0;
            if (final == null) return;
            // Tight auto-sized counter cells can round below the natural advance.
            // Only explicitly trimmed labels are clipped; the untrimmed number
            // and raised plus may use their small transparent safety padding.
            bool clip = TextTrimming != TextTrimming.None;
            // Keep the horizontal ellipsis boundary, but allow real glyph ink
            // above/below the rounded layout line (g/j/p/q/y, accents, fallback).
            using (DrawingContext draw = glyphDrawing.RenderOpen())
            {
                if (clip) draw.PushClip(new RectangleGeometry(new Rect(0, destination.Top, ActualWidth, destination.Height)));
                draw.DrawImage(moving ? high : final, destination);
                if (clip) draw.Pop();
            }
        }

        private void Build(DpiScale dpi, double fractionX, double fractionY)
        {
            Stopwatch watch = Stopwatch.StartNew();
            const int pad = 1;
            FormattedText text = new FormattedText(Text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection,
                new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, Foreground,
                null, TextFormattingMode.Ideal, dpi.DpiScaleX * Sampling);
            text.MaxLineCount = 1; text.Trimming = TextTrimming;
            if (TextTrimming != TextTrimming.None) text.MaxTextWidth = Math.Max(1, ActualWidth);
            double baselineDelta = BaselineOffset - text.Baseline;
            Rect ink = text.BuildGeometry(new Point(0, baselineDelta)).Bounds;
            double inkTop = ink.IsEmpty ? 0 : Math.Min(0, ink.Top);
            double inkBottom = ink.IsEmpty ? ActualHeight : Math.Max(ActualHeight, ink.Bottom);
            int top = checked((int)Math.Floor(inkTop * dpi.DpiScaleY + fractionY) - pad);
            int bottom = checked((int)Math.Ceiling(inkBottom * dpi.DpiScaleY + fractionY) + pad);
            int neededWidth = checked((int)Math.Ceiling(ActualWidth * dpi.DpiScaleX + fractionX) + pad * 2);
            int neededHeight = checked(bottom - top);
            int width = neededWidth, height = neededHeight;
            RenderTargetBitmap reusable = high as RenderTargetBitmap;
            int highWidth = checked(width * Sampling), highHeight = checked(height * Sampling);
            int bytes = checked(highWidth * highHeight * 4);
            // Bounded text rectangles protect allocation if a future layout is invalid.
            if (bytes > 64 * 1024 * 1024) throw new ArgumentException("Text surface exceeds limit.");
            destination = new Rect(-(pad + fractionX) / dpi.DpiScaleX, (top - fractionY) / dpi.DpiScaleY,
                width / dpi.DpiScaleX, height / dpi.DpiScaleY);
            DrawingVisual visual = new DrawingVisual();
            VisualTreeHelper.SetRootDpi(visual, new DpiScale(dpi.DpiScaleX * Sampling, dpi.DpiScaleY * Sampling));
            TextOptions.SetTextFormattingMode(visual, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Grayscale);
            TextOptions.SetTextHintingMode(visual, TextHintingMode.Fixed);
            using (DrawingContext draw = visual.RenderOpen())
                draw.DrawText(text, new Point(-destination.X, -destination.Y + baselineDelta));
            RenderTargetBitmap bitmap = reusable;
            if (bitmap == null || bitmap.PixelWidth != highWidth || bitmap.PixelHeight != highHeight ||
                bitmap.DpiX != 96 * dpi.DpiScaleX * Sampling || bitmap.DpiY != 96 * dpi.DpiScaleY * Sampling)
            {
                bitmap = new RenderTargetBitmap(highWidth, highHeight,
                    96 * dpi.DpiScaleX * Sampling, 96 * dpi.DpiScaleY * Sampling, PixelFormats.Pbgra32);
                SurfaceAllocationCount++;
            }
            else bitmap.Clear();
            bitmap.Render(visual);
            workspace.Ensure(bytes, checked(width * height * 4));
            byte[] scratch = workspace.HighPixels, pixels = workspace.FinalPixels;
            bitmap.CopyPixels(scratch, highWidth * 4, 0);
            // Average premultiplied BGRA, including alpha. This is the same box
            // coverage filter as the user-approved offline four-times reference.
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                for (int c = 0; c < 4; c++)
                {
                    int sum = 0;
                    for (int sy = 0; sy < Sampling; sy++) for (int sx = 0; sx < Sampling; sx++)
                        sum += scratch[((y * Sampling + sy) * highWidth + x * Sampling + sx) * 4 + c];
                    pixels[(y * width + x) * 4 + c] = (byte)((sum + 8) / 16);
                }
            WriteableBitmap reduced = final as WriteableBitmap;
            if (reduced == null || reduced.PixelWidth != width || reduced.PixelHeight != height ||
                reduced.DpiX != 96 * dpi.DpiScaleX || reduced.DpiY != 96 * dpi.DpiScaleY)
            {
                reduced = new WriteableBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32, null);
                SurfaceAllocationCount++;
            }
            reduced.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
            high = bitmap; final = reduced;
            BuildCount++; watch.Stop(); LastBuildMilliseconds = watch.Elapsed.TotalMilliseconds;
        }
    }
}
