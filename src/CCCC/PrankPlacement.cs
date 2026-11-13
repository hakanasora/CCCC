using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace CCCC
{
    // All monitor coordinates and SetWindowPos arguments are physical pixels.
    // Only sizes cross into WPF DIPs. Never divide a virtual-screen origin by DPI.
    internal static class PrankPlacement
    {
        [StructLayout(LayoutKind.Sequential)] private struct Point32 { internal int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct PixelRect
        { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo
        { internal uint Size; internal PixelRect Monitor, Work; internal uint Flags; }
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point32 point, uint flags);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point32 point);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out PixelRect bounds);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

        internal static IntPtr SelectMonitor(IntPtr foreground)
        {
            IntPtr monitor = foreground != IntPtr.Zero && IsWindow(foreground) ? MonitorFromWindow(foreground, 0) : IntPtr.Zero;
            Point32 mouse;
            if (monitor == IntPtr.Zero && GetCursorPos(out mouse)) monitor = MonitorFromPoint(mouse, 0);
            return monitor == IntPtr.Zero ? MonitorFromPoint(new Point32(), 1) : monitor;
        }
        internal static PixelRect WorkArea(IntPtr monitor)
        {
            MonitorInfo info = new MonitorInfo { Size = (uint)Marshal.SizeOf(typeof(MonitorInfo)) };
            if (!GetMonitorInfo(monitor, ref info))
            {
                monitor = MonitorFromPoint(new Point32(), 1);
                if (!GetMonitorInfo(monitor, ref info)) throw new InvalidOperationException("无法获取显示器工作区。");
            }
            return info.Work;
        }
        internal static Rect MainBounds(PixelRect work)
        {
            double width = Math.Max(1, work.Right - work.Left), height = Math.Max(1, work.Bottom - work.Top);
            double w = Math.Max(1, Math.Floor(width * .58)), h = Math.Max(1, Math.Floor(height * .63));
            return new Rect(Math.Round(work.Left + (width - w) / 2), Math.Round(work.Top + (height - h) / 2), w, h);
        }
        internal static Rect ConfirmBounds(Rect parent, PixelRect work, double dpi)
        {
            double w = Math.Max(1, Math.Min(430 * dpi, Math.Min(parent.Width * .82, work.Right - work.Left)));
            double h = Math.Max(1, Math.Min(230 * dpi, Math.Min(parent.Height * .70, work.Bottom - work.Top)));
            return new Rect(Math.Round(Math.Max(work.Left, Math.Min(work.Right - w, parent.X + (parent.Width - w) / 2))),
                Math.Round(Math.Max(work.Top, Math.Min(work.Bottom - h, parent.Y + (parent.Height - h) / 2))), Math.Round(w), Math.Round(h));
        }
        internal static Rect Bounds(Window window)
        {
            PixelRect result;
            if (!GetWindowRect(new WindowInteropHelper(window).Handle, out result)) return Rect.Empty;
            return new Rect(result.Left, result.Top, result.Right - result.Left, result.Bottom - result.Top);
        }
        internal static double Scale(Window window)
        {
            try
            {
                uint dpi = GetDpiForWindow(new WindowInteropHelper(window).Handle);
                if (dpi != 0) return dpi / 96.0;
            }
            catch (EntryPointNotFoundException) { }
            HwndSource source = PresentationSource.FromVisual(window) as HwndSource;
            return source != null && source.CompositionTarget != null ? source.CompositionTarget.TransformToDevice.M11 : 1;
        }
        internal static void Apply(Window window, Rect pixels)
        {
            IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
            // Moving first lets WPF process the monitor's WM_DPICHANGED before
            // converting the final dimensions. Neither operation activates it.
            SetWindowPos(hwnd, IntPtr.Zero, (int)pixels.X, (int)pixels.Y, 0, 0, 0x0015);
            double scale = Scale(window);
            window.Width = pixels.Width / scale; window.Height = pixels.Height / scale;
            SetWindowPos(hwnd, IntPtr.Zero, (int)pixels.X, (int)pixels.Y, (int)pixels.Width, (int)pixels.Height, 0x0014);
        }
    }

    // Coalesces display/DPI changes and removes its HWND hook on close. WPF
    // remains responsible for its own DPI transform and font rasterization.
    internal sealed class PrankPlacementSubscription : IDisposable
    {
        private HwndSource source;
        private readonly Window window;
        private readonly Action refresh;
        private bool queued, disposed;
        internal PrankPlacementSubscription(Window window, Action refresh)
        {
            this.window = window; this.refresh = refresh;
            source = HwndSource.FromHwnd(new WindowInteropHelper(window).EnsureHandle());
            source.AddHook(Message);
        }
        private IntPtr Message(IntPtr hwnd, int message, IntPtr w, IntPtr l, ref bool handled)
        {
            if ((message == 0x02E0 || message == 0x007E || message == 0x001A) && !disposed && !queued)
            {
                queued = true;
                window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
                { queued = false; if (!disposed) refresh(); }));
            }
            return IntPtr.Zero;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (source != null) { source.RemoveHook(Message); source = null; }
        }
    }
}
