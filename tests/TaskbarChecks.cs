using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;

namespace CCCC
{
    // Never invokes Program.Main, Show/Present, UIA, clipboard hooks or material
    // creation. Geometry and detached WPF layout only; safe during other work.
    internal static class TaskbarChecks
    {
        private static int checks;
        private static void Check(bool ok, string label)
        { if (!ok) throw new InvalidOperationException(label); checks++; }
        private static bool Near(double a, double b, double epsilon)
        { return Math.Abs(a - b) <= epsilon; }

        [STAThread]
        internal static int Main()
        {
            try
            {
                Check(ToastProfile.Compact && ToastProfile.SettingsFile != "settings.txt", "isolated profile");
                Point spot;
                Rect bar = new Rect(0, 950, 1920, 50);
                Check(TaskbarPlacement.TryFindSlot(bar, new[] { new Rect(700, 950, 500, 50) }, 1, out spot) && spot.X < 300, "centered taskbar");
                Check(TaskbarPlacement.TryFindSlot(bar, new[] { new Rect(0, 950, 170, 50), new Rect(700, 950, 500, 50) }, 1, out spot) && spot.X > 170, "weather avoidance");
                Check(TaskbarPlacement.TryFindSlot(bar, new[] { new Rect(0, 950, 650, 50) }, 1, out spot) && spot.X > 650, "left-aligned buttons");
                Check(!TaskbarPlacement.TryFindSlot(bar, new[] { bar }, 1, out spot), "full taskbar fallback");
                Check(!TaskbarPlacement.TryFindSlot(new Rect(0, 970, 1920, 30), new Rect[0], 1, out spot), "short taskbar fallback");
                Native.Rect monitor = new Native.Rect { Left = 0, Top = 0, Right = 2560, Bottom = 1600 };
                Native.Rect work = monitor; work.Bottom = 1520;
                Rect monitorRect = new Rect(0, 0, 2560, 1600), workRect = new Rect(0, 0, 2560, 1520);
                string reason;
                Point fallback = TaskbarPlacement.ResolveForBar(monitorRect, workRect, Rect.Empty, null, 1.75, out reason);
                Check(Near(fallback.X, 28, .001) && Near(fallback.Y, 1446.5, .001), "unknown snapshot fallback");
                Rect currentBar = new Rect(0, 1520, 2560, 80);
                Point cold = TaskbarPlacement.ResolveForBar(monitorRect, workRect, currentBar, null, 1.75, out reason);
                Check(Near(cold.X, 28, .001) && Near(cold.Y, 1532, .001) && reason.Contains("尚未确认"), "cold UIA still uses taskbar trial anchor");
                Point crowded = TaskbarPlacement.ResolveForBar(monitorRect, workRect, currentBar, new[] { currentBar }, 1.75, out reason);
                Check(crowded == fallback && reason.Contains("不足"), "confirmed crowded fallback");
                Check(!TaskbarPlacement.ValidBottomBar(monitorRect, monitorRect, currentBar), "auto-hide work area rejected");
                Check(!TaskbarPlacement.ValidBottomBar(new Rect(2560, 0, 2560, 1600), new Rect(2560, 0, 2560, 1520), currentBar), "other monitor rejected");

                Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                ToastWindow toast = new ToastWindow(false);
                Check(toast.TestFontBinding(), "font and weights retained");
                foreach (int dpi in new[] { 96, 154, 168 })
                foreach (int count in new[] { 2, 6, 55, 100 })
                {
                    ToastWindow.LayoutSnapshot layout = toast.TestLayout(count, dpi);
                    double epsilon = 1.2 / (dpi / 96.0);
                    Check(Near(layout.Badge.Width, 18, epsilon) && Near(layout.Badge.Height, 18, epsilon), "18 DIP badge");
                    Check(Near(layout.Badge.X + layout.Badge.Width / 2, 48, epsilon) &&
                        Near(layout.Badge.Y + layout.Badge.Height / 2, 48, epsilon), "concentric badge");
                    Check(Near(layout.Counter.Width, 36, epsilon) && Near(layout.Counter.Height, 20, epsilon), "36x20 counter");
                    Check(Near(layout.Counter.Y + layout.Counter.Height / 2, 48, epsilon), "counter vertical center");
                    Check(Near(layout.Group.X + layout.Group.Width / 2, layout.Counter.X + layout.Counter.Width / 2, epsilon), "counter group center");
                    Check(layout.Group.Width < layout.Counter.Width && layout.Title.Width > 100, "counter and title fit");
                    Check(layout.NumberText == (count > 99 ? "×99" : "×" + count) && layout.HasOverflow == (count > 99), "count text");
                    Check(layout.Detail.Height == 0, "preview collapsed");
                }
                Check(new WindowInteropHelper(toast).Handle == IntPtr.Zero, "no HWND created");
                Check(toast.TestMaterialHandle == IntPtr.Zero, "no material host created");
                toast = CopyApplication.ReplaceLayout(toast, false, false);
                ToastWindow.LayoutSnapshot normal = toast.TestLayout(100, 168);
                Check(!ToastProfile.Compact && ToastWindow.OuterWidth == 424 && ToastWindow.OuterHeight == 144, "original card size restored");
                Check(Near(normal.Badge.Width, 32, 1) && Near(normal.Counter.Width, 48, 1) && Near(normal.Counter.Height, 28, 1), "original badge and counter restored");
                Check(normal.Detail.Height > 0 && toast.TestFontBinding(), "original preview and fonts restored");
                Check(new WindowInteropHelper(toast).Handle == IntPtr.Zero && toast.TestMaterialHandle == IntPtr.Zero, "switch does not show windows in offscreen check");
                toast = CopyApplication.ReplaceLayout(toast, true, true);
                ToastWindow.LayoutSnapshot compactAgain = toast.TestLayout(55, 168);
                Check(ToastProfile.Compact && Near(compactAgain.Counter.Width, 36, 1) && compactAgain.Detail.Height == 0, "switch back to compact");
                Check(toast.TestMaterialHandle == IntPtr.Zero, "compact has no material host");

                foreach (bool dark in new[] { false, true })
                {
                    Themes.Current = new Appearance { Dark = dark, Transparent = true };
                    Themes.MaterialEnabled = true; Themes.MaterialAvailable = false;
                    CardBackgroundCache cache = new CardBackgroundCache();
                    BackgroundFrame blend = cache.Get(96);
                    byte[] center = new byte[4], outside = new byte[4];
                    blend.Bitmap.CopyPixels(new Int32Rect(100, 48, 1, 1), center, 4, 0);
                    blend.Bitmap.CopyPixels(new Int32Rect(28, 48, 1, 1), outside, 4, 0);
                    Check(center[3] == (dark ? 10 : 6), "faint taskbar tint only");
                    Check(outside[3] == 0, "no outer shadow");
                    Themes.MaterialEnabled = false;
                    BackgroundFrame opaque = cache.Get(96);
                    opaque.Bitmap.CopyPixels(new Int32Rect(100, 48, 1, 1), center, 4, 0);
                    Check(center[3] == 255 && cache.BuildCount == 2, "blend toggle invalidates background cache");
                }
                toast.Close(); app.Shutdown();
                string settings = Path.Combine(Path.GetTempPath(), "CCCC-taskbar-check-" + Guid.NewGuid().ToString("N"), "settings.txt");
                try
                {
                    Preferences saved = new Preferences(settings) { UseTaskbar = false, UseMaterial = false, ShowPreview = true };
                    saved.Save(); Preferences loaded = new Preferences(settings);
                    Check(!loaded.UseTaskbar && !loaded.UseMaterial && loaded.ShowPreview, "card choice saved without losing preview preference");
                    saved.UseTaskbar = true; saved.Save();
                    Check(new Preferences(settings).UseTaskbar, "taskbar choice saved");
                }
                finally { if (File.Exists(settings)) File.Delete(settings); Directory.Delete(Path.GetDirectoryName(settings)); }
                WindowFade fade = new WindowFade();
                fade.BeginIn(0, 0); fade.Advance(100);
                Check(Near(fade.Opacity, .5, .0001), "200ms fade retained");
                CopyMotion pulse = new CopyMotion(); pulse.Pulse(0);
                Check(Near(pulse.Value(55), 1, .0001) && Near(pulse.Value(210), 0, .0001), "55/155 pulse retained");
                Console.WriteLine("PASS: " + checks + " offscreen assertions; no windows shown, Shell queries or hooks started.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
