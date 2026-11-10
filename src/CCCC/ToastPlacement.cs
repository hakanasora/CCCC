using System;
using System.Windows;
using Forms = System.Windows.Forms;

namespace CCCC
{
    internal enum ToastPosition
    { Automatic, BottomLeft, TopLeft, BottomRight, TopRight, TopCenter, Center, BottomCenter, Custom }

    internal static class ToastPlacement
    {
        internal static readonly ToastPosition[] Presets = { ToastPosition.BottomLeft, ToastPosition.TopLeft,
            ToastPosition.BottomRight, ToastPosition.TopRight, ToastPosition.TopCenter, ToastPosition.Center, ToastPosition.BottomCenter };

        internal static string Key(ToastPosition position)
        {
            switch (position)
            {
                case ToastPosition.BottomLeft: return "bottom-left";
                case ToastPosition.TopLeft: return "top-left";
                case ToastPosition.BottomRight: return "bottom-right";
                case ToastPosition.TopRight: return "top-right";
                case ToastPosition.TopCenter: return "top-center";
                case ToastPosition.Center: return "center";
                case ToastPosition.BottomCenter: return "bottom-center";
                case ToastPosition.Custom: return "custom";
                default: return "automatic";
            }
        }

        internal static bool TryParse(string value, out ToastPosition position)
        {
            foreach (ToastPosition candidate in Enum.GetValues(typeof(ToastPosition)))
                if (Key(candidate) == value) { position = candidate; return true; }
            position = ToastPosition.Automatic;
            return false;
        }

        internal static string Label(ToastPosition position)
        {
            switch (position)
            {
                case ToastPosition.TopLeft: return "左上";
                case ToastPosition.BottomRight: return "右下";
                case ToastPosition.TopRight: return "右上";
                case ToastPosition.TopCenter: return "中上";
                case ToastPosition.Center: return "正中";
                case ToastPosition.BottomCenter: return "中下";
                case ToastPosition.Custom: return "自定义位置";
                case ToastPosition.Automatic: return "任务栏自动定位";
                default: return "左下";
            }
        }

        internal static bool ValidFraction(double value)
        { return !Double.IsNaN(value) && !Double.IsInfinity(value) && value >= 0 && value <= 1; }

        private static double Clamp(double value)
        { return Double.IsNaN(value) ? 0 : Math.Max(0, Math.Min(1, value)); }

        // Coordinates refer to the visible card, excluding its 32-DIP shadow.
        // Custom positions use the available travel range so they survive DPI,
        // work-area and layout changes without placing the card off-screen.
        internal static Point Resolve(ToastPosition position, Rect work, double width, double height,
            double scale, double customX, double customY)
        {
            double bleed = Math.Round(32 * scale);
            double travelX = Math.Max(0, work.Width - Math.Max(0, width - 2 * bleed));
            double travelY = Math.Max(0, work.Height - Math.Max(0, height - 2 * bleed));
            double x, y;
            if (position == ToastPosition.Custom)
            { x = travelX * Clamp(customX); y = travelY * Clamp(customY); }
            else
            {
                double marginX = Math.Min(Math.Round(20 * scale), travelX / 2);
                double marginY = Math.Min(Math.Round(20 * scale), travelY / 2);
                bool right = position == ToastPosition.TopRight || position == ToastPosition.BottomRight;
                bool center = position == ToastPosition.TopCenter || position == ToastPosition.Center || position == ToastPosition.BottomCenter;
                bool top = position == ToastPosition.TopLeft || position == ToastPosition.TopRight || position == ToastPosition.TopCenter;
                x = center ? travelX / 2 : right ? travelX - marginX : marginX;
                y = position == ToastPosition.Center ? travelY / 2 : top ? marginY : travelY - marginY;
            }
            return new Point(Math.Round(work.Left + x - bleed), Math.Round(work.Top + y - bleed));
        }

        internal static Point Normalize(Rect work, Rect window, double scale)
        {
            double bleed = Math.Round(32 * scale);
            double travelX = Math.Max(0, work.Width - Math.Max(0, window.Width - 2 * bleed));
            double travelY = Math.Max(0, work.Height - Math.Max(0, window.Height - 2 * bleed));
            return new Point(travelX == 0 ? 0 : Clamp((window.Left + bleed - work.Left) / travelX),
                travelY == 0 ? 0 : Clamp((window.Top + bleed - work.Top) / travelY));
        }

        internal static IntPtr Monitor(Preferences preferences, IntPtr foreground)
        {
            if (preferences.Position == ToastPosition.Custom && preferences.HasCustomPosition)
                foreach (Forms.Screen screen in Forms.Screen.AllScreens)
                    if (String.Equals(screen.DeviceName, preferences.CustomMonitor, StringComparison.OrdinalIgnoreCase))
                    {
                        System.Drawing.Rectangle bounds = screen.Bounds;
                        Native.Rect rect = new Native.Rect { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom };
                        return Native.MonitorFromRect(ref rect, 2);
                    }
            return Native.MonitorFromWindow(foreground, 2);
        }

        internal static Rect WorkArea(Native.MonitorInfo info)
        { return new Rect(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top); }
    }
}
