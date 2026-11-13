using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CCCC
{
    internal static class PrankChecks
    {
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        private static int checks;
        private static void Check(bool value, string label)
        { if (!value) throw new InvalidOperationException(label); checks++; }
        private sealed class FakeView : IPrankPaymentView
        {
            public event EventHandler Closed;
            internal int Presented, Cleaned;
            internal IntPtr Source;
            public void Present(IntPtr source) { Presented++; Source = source; }
            public void CloseForCleanup() { Cleaned++; Finish(); }
            internal void Finish() { if (Closed != null) Closed(this, EventArgs.Empty); }
        }
        private static void Pump()
        { Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate { })); }
        private static T Find<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            T found = parent as T;
            if (found != null && found.Name == name) return found;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            { found = Find<T>(VisualTreeHelper.GetChild(parent, i), name); if (found != null) return found; }
            return null;
        }
        private static void Click(Window window, string name)
        {
            Button button = Find<Button>(window, name);
            Check(button != null && button.IsEnabled, "clickable " + name);
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
        }
        [STAThread]
        internal static int Main(string[] args)
        {
            Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            if (args.Length > 0 && args[0] == "--preview")
            {
                PrankPaymentWindow preview = new PrankPaymentWindow();
                preview.Closed += delegate { app.Shutdown(); };
                preview.Present(GetForegroundWindow());
                app.Run(); return 0;
            }
            string temporary = Path.Combine(Path.GetTempPath(), "CCCC-prank-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            try
            {
                string path = Path.Combine(temporary, "settings.txt");
                Preferences p = new Preferences(path);
                Check(!p.UsePrank && p.UseTaskbar == ToastProfile.IsExperiment, "original first-use defaults");
                File.WriteAllText(path, "preview=off\nmaterial=off\nlayout=card\n");
                p = new Preferences(path);
                Check(!p.UsePrank && !p.ShowPreview && !p.UseMaterial && !p.UseTaskbar, "legacy card settings");
                p.UsePrank = true; bool saved = p.Save(); Check(saved, "save opt-in: " + p.LastError); p = new Preferences(path);
                Check(p.UsePrank && !p.UseTaskbar && !p.ShowPreview && !p.UseMaterial, "opt-in retains old settings");
                p.UsePrank = false; Check(p.Save(), "disable override");
                Check(!new Preferences(path).UsePrank && !new Preferences(path).UseTaskbar, "previous card restored");
                if (ToastProfile.IsExperiment)
                {
                    p.UseTaskbar = true; p.UsePrank = true; Check(p.Save(), "save taskbar override");
                    p = new Preferences(path); Check(p.UseTaskbar && p.UsePrank, "remember taskbar underneath");
                    p.UsePrank = false; p.Save(); Check(new Preferences(path).UseTaskbar, "restore previous taskbar");
                }
                List<FakeView> views = new List<FakeView>();
                PrankPaymentController controller = new PrankPaymentController(delegate
                { FakeView view = new FakeView(); views.Add(view); return view; });
                foreach (FeedbackKind kind in new[] { FeedbackKind.Unconfirmed, FeedbackKind.Retained, FeedbackKind.Updated, FeedbackKind.Status })
                    Check(!controller.Handle(new Feedback { Kind = kind }) && views.Count == 0, "non-success routes to original toast " + kind);
                Feedback copy = new Feedback { Kind = FeedbackKind.Confirmed, Foreground = new IntPtr(123) };
                Check(controller.Handle(copy) && views.Count == 1 && views[0].Source == copy.Foreground, "confirmed source monitor");
                for (int i = 0; i < 5; i++) controller.Handle(copy);
                Check(views.Count == 1 && views[0].Presented == 1, "repeated copy leaves UI session alone");
                views[0].Finish(); controller.Handle(copy);
                Check(views.Count == 2, "new session after closing");
                controller.Clear(); controller.Clear();
                Check(views[1].Cleaned == 1, "cleanup idempotent");
                controller.Handle(copy); controller.Dispose(); controller.Dispose();
                Check(!controller.Handle(copy) && views.Count == 3 && views[2].Cleaned == 1, "disposed callbacks cannot reopen");

                foreach (double dpi in new[] { 1.0, 1.25, 1.5, 2.0 })
                foreach (PrankPlacement.PixelRect work in new[] {
                    new PrankPlacement.PixelRect { Left = 0, Top = 0, Right = 1920, Bottom = 1040 },
                    new PrankPlacement.PixelRect { Left = -2560, Top = -900, Right = 0, Bottom = 540 },
                    new PrankPlacement.PixelRect { Left = -600, Top = 0, Right = 200, Bottom = 560 } })
                {
                    Rect bounds = PrankPlacement.MainBounds(work);
                    Check(bounds.Left >= work.Left && bounds.Right <= work.Right && bounds.Top >= work.Top && bounds.Bottom <= work.Bottom, "physical bounds fit work area");
                    Check(Math.Abs(bounds.X + bounds.Width / 2 - (work.Left + work.Right) / 2.0) <= .5 &&
                        Math.Abs(bounds.Y + bounds.Height / 2 - (work.Top + work.Bottom) / 2.0) <= .5, "center including negative coordinates");
                    Rect child = PrankPlacement.ConfirmBounds(bounds, work, dpi);
                    Check(bounds.Contains(child), "confirmation fits parent at DPI " + dpi);
                }
                string[] modules = PrankQrCode.ReadModules();
                Check(modules.Length == 37, "QR resource loaded from assembly, independent of working directory");
                bool quiet = true;
                for (int i = 0; i < modules.Length; i++)
                    for (int j = 0; j < 4; j++)
                        quiet &= modules[i][j] == '0' && modules[i][modules.Length - j - 1] == '0' && modules[j][i] == '0' && modules[modules.Length - j - 1][i] == '0';
                Check(quiet, "four-module quiet zone");
                PrankPaymentWindow detached = new PrankPaymentWindow(delegate { throw new Exception("unexpected browser launch"); });
                detached.SelectPayment(2); detached.ToggleHelp(); detached.ToggleHelp();
                Check(detached.SelectedPayment == 2 && detached.State == PrankPaymentState.Main, "local selection and help toggle");
                string output = args.Length > 0 ? args[args.Length - 1] : temporary;
                if (!output.StartsWith("--")) { RenderQr(output); RenderWindow(detached, output); }
                detached.CloseForCleanup();
                if (Array.IndexOf(args, "--ui") >= 0) UiChecks();
                Console.WriteLine("PASS: " + checks + " prank assertions (" + (ToastProfile.IsExperiment ? "taskbar" : "card") + "); no clipboard, tray, startup or user settings changes.");
                app.Shutdown(); return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); app.Shutdown(1); return 1; }
            finally { foreach (string file in Directory.GetFiles(temporary)) File.Delete(file); Directory.Delete(temporary); }
        }
        private static void RenderQr(string directory)
        {
            Directory.CreateDirectory(directory);
            foreach (double dpi in new[] { 1.0, 1.25, 1.5, 2.0 })
            {
                PrankQrCode qr = new PrankQrCode { Width = 270, Height = 270 };
                qr.Measure(new Size(270, 270)); qr.Arrange(new Rect(0, 0, 270, 270));
                RenderTargetBitmap bitmap = new RenderTargetBitmap((int)(270 * dpi), (int)(270 * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
                bitmap.Render(qr); PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (FileStream file = File.Create(Path.Combine(directory, "qr-render-" + (int)(dpi * 100) + ".png"))) encoder.Save(file);
            }
        }
        private static void RenderWindow(PrankPaymentWindow window, string directory)
        {
            FrameworkElement root = (FrameworkElement)window.Content;
            window.SelectPayment(0);
            foreach (Size size in new[] { new Size(960, 620), new Size(700, 450), new Size(460, 350) })
            {
                root.Measure(size); root.Arrange(new Rect(new Point(), size)); root.UpdateLayout();
                RenderTargetBitmap bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root); PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (FileStream file = File.Create(Path.Combine(directory, "layout-preview-" + (int)size.Width + ".png"))) encoder.Save(file);
                Check(VisualTreeHelper.GetDescendantBounds(root).Width <= size.Width + 1 &&
                    VisualTreeHelper.GetDescendantBounds(root).Height <= size.Height + 1, "layout fits constrained work area");
            }
        }
        private static void UiChecks()
        {
            int launches = 0;
            PrankPaymentWindow window = new PrankPaymentWindow(delegate(string url)
            { launches++; Check(url == PrankPaymentController.RepositoryUrl, "exact URL"); throw new Win32Exception("injected browser failure"); });
            try
            {
                IntPtr foreground = GetForegroundWindow();
                window.Present(foreground); Pump();
                Check(GetForegroundWindow() == foreground, "initial display does not activate");
                Check(window.IsHitTestVisible && window.Focusable, "interactive main window");
                window.SelectPayment(1);
                Click(window, "ToggleHelp"); Check(launches == 0 && window.State == PrankPaymentState.Help, "help before browser");
                Click(window, "OpenRepository");
                Check(launches == 1 && window.HelpError.Contains(PrankPaymentController.RepositoryUrl), "browser failure shows URL without copying it");
                Click(window, "Freeload");
                FreeloadConfirmWindow child = window.Confirmation;
                Check(window.State == PrankPaymentState.Confirmation && child.Owner == window && child.IsVisible && window.IsVisible, "owned modeless confirmation");
                Check(Find<Button>(child, "CancelFreeload").IsKeyboardFocused, "safe initial keyboard focus");
                window.ToggleHelp(); window.ShowDecorativeHint(); window.RequestFreeload();
                Check(window.Confirmation == child && !window.HintVisible && !Find<Button>(window, "Freeload").IsEnabled, "parent actions disabled during confirmation");
                Click(child, "CancelFreeload");
                Check(window.State == PrankPaymentState.Main && window.IsVisible && window.SelectedPayment == 1, "cancel preserves parent and method");
                window.RequestFreeload(); Pump(); child = window.Confirmation;
                child.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(child), 0, Key.Escape)
                    { RoutedEvent = UIElement.PreviewKeyDownEvent }); Pump();
                Check(window.State == PrankPaymentState.Main && window.SelectedPayment == 1, "Escape cancels only child");
                Click(window, "DecorativeClose"); Click(window, "DecorativeClose");
                Check(window.IsVisible && window.HintVisible, "decorative X keeps main open");
                window.Close(); Pump(); Check(window.Confirmation != null, "system close routes to confirmation");
                Click(window.Confirmation, "ConfirmFreeload");
                Check(window.State == PrankPaymentState.Closed && !window.IsVisible && window.Confirmation == null, "accept closes both windows");
            }
            finally { window.CloseForCleanup(); }
            window = new PrankPaymentWindow(delegate { return true; });
            try
            {
                window.Present(IntPtr.Zero); Pump();
                Click(window, "ToggleHelp"); Click(window, "OpenRepository");
                Check(window.State == PrankPaymentState.Main && window.IsVisible, "successful launch collapses only help");
                window.RequestFreeload(); Pump();
                FreeloadConfirmWindow child = window.Confirmation;
                window.CloseForCleanup(); Pump();
                Check(!window.IsVisible && !child.IsVisible && window.State == PrankPaymentState.Closed, "internal cleanup bypasses user confirmation");
            }
            finally { window.CloseForCleanup(); }
            window = new PrankPaymentWindow(delegate { return true; });
            try
            {
                window.Present(IntPtr.Zero); Pump(); window.ShowDecorativeHint();
                window.Close(); window.CloseForCleanup(); Pump();
                Check(window.State == PrankPaymentState.Closed && window.Confirmation == null && !window.HintVisible,
                    "queued system-close confirmation cannot reopen after cleanup");
            }
            finally { window.CloseForCleanup(); }
        }
    }
}
