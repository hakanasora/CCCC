using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.UI.Composition;
using Drawing = System.Drawing;

namespace CCCC
{
    internal static class MaterialChecks
    {
        private enum Mode { Normal, Color, Off }
        private sealed class Checker : FrameworkElement
        {
            internal double DpiScale;
            internal int Phase;
            protected override void OnRender(DrawingContext drawing)
            {
                double cell = 8 / DpiScale, shift = Phase / DpiScale;
                drawing.DrawRectangle(Brushes.White, null, new Rect(RenderSize));
                for (int row = 0; row * cell < ActualHeight; row++)
                    for (int column = -2; column * cell < ActualWidth; column++)
                        if (((row + column) & 1) == 0)
                            drawing.DrawRectangle(Brushes.Black, null,
                                new Rect(column * cell + shift, row * cell, cell, cell));
            }
        }
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Stopwatch Clock = new Stopwatch();
        private static double scale = 1, opacity;
        private static volatile bool stop;
        private static int checkerPhase, nativeActive, markerActive;
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sx, int sy, uint operation);
        private static T Field<T>(object owner, string name)
        { return (T)owner.GetType().GetField(name, Private).GetValue(owner); }
        private static Feedback Sample(int count)
        { return new Feedback { Kind = FeedbackKind.Confirmed, Count = count, Title = FeedbackEngine.SuccessTitle(count), Preview = "Material check g/y", Foreground = IntPtr.Zero }; }

        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }

        private static int Lifecycle()
        {
            Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            ToastWindow toast = null;
            try
            {
                ToastProfile.SetCompact(false);
                Themes.Current = new Appearance { Dark = true, Transparent = true };
                toast = new ToastWindow(true);
                toast.Present(Sample(1), true);
                Image image = Field<Image>(toast, "background");
                Require(toast.HasNativeMaterial && image.Visibility == Visibility.Hidden, "Material background was not transferred");
                toast.SetMaterialEnabled(false);
                Require(!toast.HasNativeMaterial && toast.TestMaterialHandle == IntPtr.Zero &&
                    image.Visibility == Visibility.Visible && !toast.CurrentBackground.Acrylic, "Disabled material did not restore the opaque background");
                toast.TestFailMaterialCreation = true;
                toast.SetMaterialEnabled(true);
                Require(!toast.HasNativeMaterial && image.Visibility == Visibility.Visible &&
                    !toast.CurrentBackground.Acrylic && toast.MaterialFailure != null, "Creation failure did not restore the background");
                toast.TestFailMaterialCreation = false;
                toast.SetMaterialEnabled(true);
                Require(toast.HasNativeMaterial && image.Visibility == Visibility.Hidden, "Material did not recover after creation failure");
                int dpi = Native.WindowDpi(toast.Handle);
                BackgroundFrame invalid = CardBackgroundRenderer.Build(dpi);
                invalid.Bitmap = new System.Windows.Media.Imaging.FormatConvertedBitmap(invalid.Bitmap, PixelFormats.Bgra32, null, 0);
                CardBackgroundCache cache = Field<CardBackgroundCache>(toast, "backgrounds");
                typeof(CardBackgroundCache).GetField("current", Private).SetValue(cache, invalid);
                typeof(ToastWindow).GetMethod("ApplyBackground", Private).Invoke(toast, new object[] { dpi, dpi / 96.0 });
                Require(!toast.HasNativeMaterial && toast.TestMaterialHandle == IntPtr.Zero && image.Visibility == Visibility.Visible &&
                    !toast.CurrentBackground.Acrylic && toast.MaterialFailure != null, "Upload failure did not restore the opaque background");
                toast.SetMaterialEnabled(true);
                Require(toast.HasNativeMaterial && image.Visibility == Visibility.Hidden, "Material did not recover after upload failure");
                Console.WriteLine("PASS: material disable, creation failure, upload failure, and recovery");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally { if (toast != null) toast.Close(); app.Shutdown(); }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "lifecycle") return Lifecycle();
            Mode mode;
            string requested = args.Length > 0 ? args[0] : "normal";
            switch (requested)
            {
                case "normal": mode = Mode.Normal; break;
                case "color": mode = Mode.Color; break;
                case "off": mode = Mode.Off; break;
                default: Console.Error.WriteLine("Modes: normal, color, off"); return 2;
            }
            string background = args.Length > 1 ? args[1] : "desktop";
            if (background != "background" && background != "checker" && background != "desktop")
            { Console.Error.WriteLine("Backgrounds: desktop, background, checker"); return 2; }
            string theme = args.Length > 3 ? args[3] : "dark";
            if (theme != "dark" && theme != "light")
            { Console.Error.WriteLine("Themes: dark, light"); return 2; }
            string output = args.Length > 2 ? Path.GetFullPath(args[2]) : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runs", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + requested + "-" + background);
            Directory.CreateDirectory(output);
            Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            ToastWindow toast = null;
            Window backing = null;
            Thread capture = null;
            CompositionColorBrush color = null;
            DispatcherTimer checkerTimer = null;
            Exception captureError = null;
            EventHandler observed = null;
            try
            {
                ToastProfile.SetCompact(false);
                Themes.Current = new Appearance { Dark = theme == "dark", Transparent = true };
                Themes.MaterialAvailable = true;
                toast = new ToastWindow(mode != Mode.Off);
                toast.CopyPressed(1, IntPtr.Zero, true);
                toast.Present(Sample(1), true);
                if (mode != Mode.Off && !toast.HasNativeMaterial) throw new Exception("Native material unavailable: " + toast.MaterialFailure);
                AcrylicHost host = Field<AcrylicHost>(toast, "acrylic");
                SpriteVisual visual = host == null ? null : Field<SpriteVisual>(host, "visual");
                if (mode == Mode.Color)
                {
                    color = Field<Compositor>(host, "compositor").CreateColorBrush(Windows.UI.Color.FromArgb(255, 255, 0, 180));
                    visual.Brush = color;
                }
                Native.Rect bounds;
                if (!Native.GetWindowRect(toast.Handle, out bounds)) throw new Exception("Toast bounds unavailable");
                int left = bounds.Left, top = bounds.Top, width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
                if (background != "desktop")
                {
                    double dpi = Native.WindowDpi(toast.Handle) / 96.0;
                    bool checker = background == "checker";
                    backing = new Window { Title = "CCCC temporary diagnostic background", Width = width / dpi * (checker ? 1.3 : .8), Height = height / dpi * 2,
                        Left = (left + width * (checker ? -.15 : .5)) / dpi, Top = (top - height / 2) / dpi, ShowActivated = false,
                        WindowStartupLocation = WindowStartupLocation.Manual, Background = Brushes.SteelBlue, ShowInTaskbar = false };
                    if (checker)
                    {
                        Checker pattern = new Checker { DpiScale = dpi, ClipToBounds = true };
                        RenderOptions.SetEdgeMode(pattern, EdgeMode.Aliased);
                        backing.Content = pattern;
                        checkerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                        checkerTimer.Tick += delegate {
                            pattern.Phase = (pattern.Phase + 1) % 16;
                            Volatile.Write(ref checkerPhase, pattern.Phase);
                            pattern.InvalidateVisual();
                        };
                        checkerTimer.Start();
                    }
                    backing.Show();
                    Native.SetWindowPos(new WindowInteropHelper(backing).Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0010 | 0x0001 | 0x0002 | 0x0004);
                }
                File.WriteAllText(Path.Combine(output, "run.txt"), "mode=" + requested + "\r\nbackground=" + background + "\r\ntheme=" + theme + "\r\nchecker_cell_px=8\r\nchecker_tick_ms=50\r\nleft=" + left + "\r\ntop=" + top + "\r\nwidth=" + width + "\r\nheight=" + height + "\r\ndpi=" + Native.WindowDpi(toast.Handle) + "\r\nmaterial=" + toast.HasNativeMaterial + "\r\noverlay=" + toast.Handle + "\r\nnative=" + (host == null ? IntPtr.Zero : host.Handle) + "\r\n");
                observed = delegate { Interlocked.Exchange(ref scale, toast.TestScale); Interlocked.Exchange(ref opacity, toast.Opacity); Volatile.Write(ref nativeActive, toast.HasNativeMaterial ? 1 : 0); Volatile.Write(ref markerActive, mode == Mode.Color && toast.HasNativeMaterial && Object.ReferenceEquals(visual.Brush, color) ? 1 : 0); };
                System.Windows.Media.CompositionTarget.Rendering += observed;
                Clock.Start();
                capture = new Thread(delegate()
                {
                    try
                    {
                        using (StreamWriter log = new StreamWriter(Path.Combine(output, "frames.csv")))
                        using (Drawing.Bitmap bitmap = new Drawing.Bitmap(width, height, Drawing.Imaging.PixelFormat.Format32bppArgb))
                        using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(bitmap))
                        {
                            log.WriteLine("frame,capture_start_ms,capture_end_ms,scale,opacity,file,checker_phase_px,native_active,marker_active");
                            int index = 0;
                            while (!stop)
                            {
                                double start = Clock.Elapsed.TotalMilliseconds;
                                double currentScale = Interlocked.CompareExchange(ref scale, 0, 0);
                                double currentOpacity = Interlocked.CompareExchange(ref opacity, 0, 0);
                                int phase = Volatile.Read(ref checkerPhase);
                                IntPtr screen = GetDC(IntPtr.Zero), target = IntPtr.Zero;
                                try
                                {
                                    target = graphics.GetHdc();
                                    if (!BitBlt(target, 0, 0, width, height, screen, left, top, 0x40CC0020))
                                        throw new System.ComponentModel.Win32Exception();
                                }
                                finally
                                {
                                    if (target != IntPtr.Zero) graphics.ReleaseHdc(target);
                                    if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
                                }
                                double end = Clock.Elapsed.TotalMilliseconds;
                                string filename = "frame-" + index.ToString("D5") + ".bmp";
                                bitmap.Save(Path.Combine(output, filename), Drawing.Imaging.ImageFormat.Bmp);
                                log.WriteLine(String.Format(CultureInfo.InvariantCulture, "{0},{1:F3},{2:F3},{3:F6},{4:F6},{5},{6},{7},{8}", index++, start, end, currentScale, currentOpacity, filename, phase, Volatile.Read(ref nativeActive), Volatile.Read(ref markerActive)));
                                Thread.Sleep(1);
                            }
                        }
                    }
                    catch (Exception error) { captureError = error; }
                });
                capture.IsBackground = true;
                capture.Start();
                int count = 1;
                DispatcherTimer pulse = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(340) };
                pulse.Tick += delegate { toast.CopyPressed(++count, IntPtr.Zero, true); toast.Present(Sample(count), true); };
                DispatcherTimer finish = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                finish.Tick += delegate { finish.Stop(); pulse.Stop(); app.Shutdown(); };
                pulse.Start(); finish.Start(); app.Run();
                stop = true;
                capture.Join();
                if (captureError != null) throw captureError;
                Console.WriteLine(output);
                return 0;
            }
            catch (Exception error) { File.WriteAllText(Path.Combine(output, "error.txt"), error.ToString()); Console.Error.WriteLine(error); return 1; }
            finally
            {
                stop = true;
                if (capture != null) capture.Join(5000);
                if (observed != null) System.Windows.Media.CompositionTarget.Rendering -= observed;
                if (checkerTimer != null) checkerTimer.Stop();
                if (toast != null) toast.Close();
                if (backing != null) backing.Close();
                if (color != null) color.Dispose();
                app.Shutdown();
            }
        }
    }
}
