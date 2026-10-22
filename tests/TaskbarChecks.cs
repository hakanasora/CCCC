using System;
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
                Point fallback = TaskbarPlacement.Resolve(monitor, work, 168);
                Check(Near(fallback.X, 28, .001) && Near(fallback.Y, 1446.5, .001), "unknown snapshot fallback");

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
                toast.Close(); app.Shutdown();
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
