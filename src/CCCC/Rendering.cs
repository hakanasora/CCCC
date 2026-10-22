using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CCCC
{
    internal static class CardOutline
    {
        internal const double CornerExtent = 26;
        internal const double FlattenTolerance = 0.02; // Final physical pixels.

        internal static Point[] Controls(double c)
        {
            // Extended endpoint tangents soften the straight-to-curve junction.
            // Equal endpoint control spacing preserves zero endpoint curvature.
            return new Point[] { new Point(0, 0), new Point(.36 * c, 0), new Point(.72 * c, 0),
                new Point(c, .28 * c), new Point(c, .64 * c), new Point(c, c) };
        }

        internal static Point Evaluate(Point[] controls, double t)
        {
            Point[] values = (Point[])controls.Clone();
            for (int n = values.Length - 1; n > 0; n--)
                for (int i = 0; i < n; i++) values[i] = Lerp(values[i], values[i + 1], t);
            return values[0];
        }

        internal static List<Point> Flatten(Point[] controls, double tolerance)
        {
            List<Point> result = new List<Point> { controls[0] };
            FlattenInto(controls, tolerance, 0, result);
            return result;
        }

        private static void FlattenInto(Point[] p, double tolerance, int depth, List<Point> points)
        {
            double error = 0;
            Vector chord = p[p.Length - 1] - p[0];
            double length = chord.Length;
            for (int i = 1; i < p.Length - 1; i++)
            {
                Vector v = p[i] - p[0];
                double distance = length > 0 ? Math.Abs(Vector.CrossProduct(chord, v)) / length : v.Length;
                error = Math.Max(error, distance);
            }
            // A Bezier curve lies in its control hull. This bounds deviation from
            // the chord for this monotonic convex corner, rather than guessing steps.
            if (error <= tolerance || depth >= 20) { points.Add(p[p.Length - 1]); return; }
            Point[] row = (Point[])p.Clone(), left = new Point[p.Length], right = new Point[p.Length];
            left[0] = row[0]; right[p.Length - 1] = row[p.Length - 1];
            for (int n = p.Length - 1, level = 1; n > 0; n--, level++)
            {
                for (int i = 0; i < n; i++) row[i] = Lerp(row[i], row[i + 1], .5);
                left[level] = row[0]; right[p.Length - 1 - level] = row[n - 1];
            }
            FlattenInto(left, tolerance, depth + 1, points);
            FlattenInto(right, tolerance, depth + 1, points);
        }

        private static Point Lerp(Point a, Point b, double t)
        { return new Point(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t); }

        internal static Geometry Create(Rect rect, double scale)
        {
            if (ToastProfile.Compact)
            {
                RectangleGeometry capsule = new RectangleGeometry(rect, rect.Height / 2, rect.Height / 2);
                capsule.Freeze(); return capsule;
            }
            double c = Math.Min(CornerExtent * scale, Math.Min(rect.Width, rect.Height) / 2);
            List<Point> corner = Flatten(Controls(c), FlattenTolerance);
            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(new Point(rect.Left + c, rect.Top), true, true);
                context.LineTo(new Point(rect.Right - c, rect.Top), true, false);
                AddCorner(context, corner, new Point(rect.Right - c, rect.Top), 0);
                context.LineTo(new Point(rect.Right, rect.Bottom - c), true, false);
                AddCorner(context, corner, new Point(rect.Right, rect.Bottom - c), 1);
                context.LineTo(new Point(rect.Left + c, rect.Bottom), true, false);
                AddCorner(context, corner, new Point(rect.Left + c, rect.Bottom), 2);
                context.LineTo(new Point(rect.Left, rect.Top + c), true, false);
                AddCorner(context, corner, new Point(rect.Left, rect.Top + c), 3);
            }
            geometry.Freeze(); return geometry;
        }

        private static void AddCorner(StreamGeometryContext context, List<Point> points, Point start, int turn)
        {
            for (int i = 1; i < points.Count; i++)
            {
                Point p = points[i];
                Point rotated = turn == 0 ? p : turn == 1 ? new Point(-p.Y, p.X) :
                    turn == 2 ? new Point(-p.X, -p.Y) : new Point(p.Y, -p.X);
                context.LineTo(new Point(start.X + rotated.X, start.Y + rotated.Y), true, true);
            }
        }
    }

    internal sealed class BackgroundFrame
    {
        internal BitmapSource Bitmap;
        internal Geometry Outline;
        internal int Dpi, Width, Height;
        internal bool Dark, Acrylic, Compact, TaskbarBlend;
        internal double BuildMilliseconds;
        internal int HighBufferBytes;
    }

    internal sealed class CardBackgroundCache
    {
        private BackgroundFrame current;
        internal int BuildCount { get; private set; }
        internal int Count { get { return current == null ? 0 : 1; } }
        internal BackgroundFrame Get(int dpi)
        {
            bool blend = ToastProfile.Compact && Themes.MaterialEnabled && Themes.Current.Transparent;
            if (current == null || current.Dpi != dpi || current.Dark != Themes.Current.Dark || current.Acrylic != Themes.UseAcrylic ||
                current.Compact != ToastProfile.Compact || current.TaskbarBlend != blend)
            {
                BackgroundFrame replacement = CardBackgroundRenderer.Build(dpi);
                current = replacement; BuildCount++;
            }
            return current;
        }
    }

    internal static class CardBackgroundRenderer
    {
        internal static int LogicalWidth { get { return ToastProfile.Compact ? 289 : 424; } }
        internal static int LogicalHeight { get { return ToastProfile.Compact ? 96 : 144; } }
        private const int Sampling = 2;

        internal static BackgroundFrame Build(int dpi)
        {
            if (dpi <= 0 || dpi > 960) throw new ArgumentOutOfRangeException("dpi");
            Stopwatch watch = Stopwatch.StartNew();
            double scale = dpi / 96.0;
            int width = (int)Math.Round(LogicalWidth * scale), height = (int)Math.Round(LogicalHeight * scale);
            int highWidth = width * Sampling, highHeight = height * Sampling;
            int margin = (int)Math.Round(32 * scale);
            Rect bounds = new Rect(margin + .5, margin + .5, width - 2 * margin - 1, height - 2 * margin - 1);
            Geometry outline = CardOutline.Create(bounds, scale);
            bool taskbarBlend = ToastProfile.Compact && Themes.MaterialEnabled && Themes.Current.Transparent;
            double taskbarShadow = taskbarBlend ? Themes.Current.TaskbarShadow : 0;

            // The drawing coordinates here are FINAL physical pixels. Only this
            // detached visual is rendered at 2x; text is never in this bitmap.
            DrawingVisual visual = new DrawingVisual();
            VisualTreeHelper.SetRootDpi(visual, new DpiScale(Sampling, Sampling));
            using (DrawingContext draw = visual.RenderOpen())
            {
                Color color = Themes.Current.Fill;
                color.A = Themes.UseAcrylic ? (byte)Math.Round(255 * Themes.Current.Tint) : (byte)255;
                if (taskbarBlend)
                {
                    // User-selected tint over the taskbar's existing material;
                    // never create another backdrop sampler or add grain.
                    color = Themes.Current.TaskbarFill;
                    color.A = (byte)Math.Round(255 * Themes.Current.TaskbarTint);
                }
                SolidColorBrush fill = new SolidColorBrush(color); fill.Freeze();
                SolidColorBrush line = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Themes.Current.Rim)); line.Freeze();
                Pen pen = new Pen(line, 1); pen.Freeze();
                draw.DrawGeometry(fill, ToastProfile.Compact ? null : pen, outline);
            }
            RenderTargetBitmap high = new RenderTargetBitmap(highWidth, highHeight, 192, 192, PixelFormats.Pbgra32);
            high.Render(visual);
            byte[] body = new byte[highWidth * highHeight * 4];
            high.CopyPixels(body, highWidth * 4, 0);

            // A shadow-only alpha layer derived from exactly the same contour.
            // For the ordinary card, three separable box passes approximate
            // sigma=3 logical pixels with the existing 15% shadow opacity.
            // The compact light surface uses the separately selected weak shadow.
            // Running sums make each blur pass O(pixel count), independent of DPI.
            byte[] alpha = new byte[highWidth * highHeight], temporary = new byte[alpha.Length];
            bool drawShadow = !ToastProfile.Compact || taskbarShadow > 0;
            double coverageTint = taskbarBlend ? Themes.Current.TaskbarTint : (Themes.UseAcrylic ? Themes.Current.Tint : 1);
            if (drawShadow)
                for (int i = 0; i < alpha.Length; i++) alpha[i] = (byte)Math.Min(255, Math.Round(body[i * 4 + 3] / coverageTint));
            int radius = Math.Max(1, (int)Math.Round(ToastProfile.Compact ? 3.5 * scale * Sampling : 3 * scale * Sampling - .5));
            if (drawShadow)
                for (int pass = 0; pass < 3; pass++) BoxBlur(alpha, temporary, highWidth, highHeight, radius);
            int offset = (int)Math.Round(3 / Math.Sqrt(2) * scale * Sampling);
            // Match the tuning preview's weak vertical shadow: 0px 2px 7px.
            // The existing separable blur is a software approximation of that
            // browser blur, cached once per appearance/DPI, never per frame.
            byte[] pixels = ToastProfile.Compact ? CompositeAndDownsample(body, alpha, highWidth, highHeight,
                0, (int)Math.Round(2 * scale * Sampling), taskbarShadow, coverageTint) :
                CompositeAndDownsample(body, alpha, highWidth, highHeight, offset);
            // 1.2% extra grain, cached with the background. Preserve premultiplied
            // alpha and avoid affecting the transparent shadow outside the card.
            for (int y = margin + 2; !ToastProfile.Compact && Themes.UseAcrylic && y < height - margin - 2; y++)
                for (int x = margin + 2; x < width - margin - 2; x++)
                {
                    int p = (y * width + x) * 4; if (pixels[p + 3] < 150) continue;
                    uint hash = unchecked((uint)(x * 374761393 + y * 668265263)); hash = (hash ^ (hash >> 13)) * 1274126177;
                    double grain = (((hash >> 24) / 255.0) - .5) * 255 * .012 * pixels[p + 3] / 255.0;
                    for (int channel = 0; channel < 3; channel++) pixels[p + channel] = (byte)Math.Max(0, Math.Min(pixels[p + 3], Math.Round(pixels[p + channel] + grain)));
                }
            BitmapSource bitmap = BitmapSource.Create(width, height, dpi, dpi, PixelFormats.Pbgra32, null, pixels, width * 4);
            bitmap.Freeze(); watch.Stop();
            return new BackgroundFrame { Bitmap = bitmap, Outline = outline, Dpi = dpi, Width = width,
                Height = height, BuildMilliseconds = watch.Elapsed.TotalMilliseconds,
                HighBufferBytes = body.Length, Dark = Themes.Current.Dark, Acrylic = Themes.UseAcrylic,
                Compact = ToastProfile.Compact, TaskbarBlend = ToastProfile.Compact && Themes.MaterialEnabled && Themes.Current.Transparent };
        }

        private static void BoxBlur(byte[] values, byte[] temporary, int width, int height, int radius)
        {
            int diameter = 2 * radius + 1;
            for (int y = 0; y < height; y++)
            {
                int start = y * width, sum = 0;
                for (int x = 0; x <= radius && x < width; x++) sum += values[start + x];
                for (int x = 0; x < width; x++)
                {
                    temporary[start + x] = (byte)((sum + diameter / 2) / diameter);
                    int remove = x - radius, add = x + radius + 1;
                    if (remove >= 0) sum -= values[start + remove];
                    if (add < width) sum += values[start + add];
                }
            }
            for (int x = 0; x < width; x++)
            {
                int sum = 0;
                for (int y = 0; y <= radius && y < height; y++) sum += temporary[y * width + x];
                for (int y = 0; y < height; y++)
                {
                    values[y * width + x] = (byte)((sum + diameter / 2) / diameter);
                    int remove = y - radius, add = y + radius + 1;
                    if (remove >= 0) sum -= temporary[remove * width + x];
                    if (add < height) sum += temporary[add * width + x];
                }
            }
        }

        internal static byte[] CompositeAndDownsample(byte[] body, byte[] shadow, int width, int height, int offset)
        {
            return CompositeAndDownsample(body, shadow, width, height, offset, offset, .15,
                Themes.UseAcrylic ? Themes.Current.Tint : 1);
        }

        private static byte[] CompositeAndDownsample(byte[] body, byte[] shadow, int width, int height,
            int offsetX, int offsetY, double shadowStrength, double coverageTint)
        {
            int outputWidth = width / 2, outputHeight = height / 2;
            byte[] output = new byte[outputWidth * outputHeight * 4];
            for (int y = 0; y < outputHeight; y++)
                for (int x = 0; x < outputWidth; x++)
                {
                    int blue = 0, green = 0, red = 0, alpha = 0;
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                        {
                            int highX = x * 2 + sx, highY = y * 2 + sy;
                            int index = (highY * width + highX) * 4;
                            int shadowX = highX - offsetX, shadowY = highY - offsetY;
                            int shadowAlpha = shadowX >= 0 && shadowY >= 0 && shadowX < width && shadowY < height ?
                                (int)(shadow[shadowY * width + shadowX] * shadowStrength + .5) : 0;
                            int coverage = Math.Min(255, (int)Math.Round(body[index + 3] / coverageTint));
                            int inverse = 255 - coverage;
                            int visibleShadow = (shadowAlpha * inverse + 127) / 255;
                            blue += body[index];
                            green += body[index + 1];
                            red += body[index + 2];
                            alpha += body[index + 3] + visibleShadow;
                        }
                    int target = (y * outputWidth + x) * 4;
                    output[target] = (byte)((blue + 2) / 4);
                    output[target + 1] = (byte)((green + 2) / 4);
                    output[target + 2] = (byte)((red + 2) / 4);
                    output[target + 3] = (byte)((alpha + 2) / 4);
                }
            return output;
        }
    }
}
