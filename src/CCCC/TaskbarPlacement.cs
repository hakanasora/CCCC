#if TASKBAR_EXPERIMENT
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;

namespace CCCC
{
    // Explorer is queried, never parented, injected into, resized or restyled.
    // UIA providers can stall: only one background query may exist, and the UI
    // never waits for it. Unknown/expired data uses the compact fallback.
    internal static class TaskbarPlacement
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct AppBarData
        {
            internal uint Size;
            internal IntPtr Window;
            internal uint Callback, Edge;
            internal Native.Rect Bounds;
            internal IntPtr Parameter;
        }
        [DllImport("shell32.dll")] private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessage(string message);
        internal static readonly int RecreatedMessage = RegisterWindowMessage("TaskbarCreated");
        private sealed class Snapshot
        {
            internal Native.Rect Monitor;
            internal Rect Bar;
            internal Rect[] Occupied;
            internal long Time;
        }
        private static readonly object Gate = new object();
        private static Snapshot snapshot;
        private static bool busy;
        private static int generation;
        private static long lastRequest = -10000;

        internal static void Invalidate()
        {
            lock (Gate) { snapshot = null; generation++; lastRequest = -10000; }
        }

        internal static void RequestRefresh()
        {
            int expected;
            lock (Gate)
            {
                long now = Native.Now;
                if (busy || now - lastRequest < 2000) return;
                busy = true; lastRequest = now; expected = generation;
            }
            Thread probe = new Thread(delegate()
            {
                Snapshot result = null;
                try { result = Probe(); }
                catch (Exception) { /* Missing/inaccessible Shell provider: fallback. */ }
                finally
                {
                    lock (Gate)
                    {
                        if (generation == expected) snapshot = result;
                        busy = false;
                    }
                }
            });
            probe.IsBackground = true;
            probe.Name = "CCCC taskbar snapshot";
            probe.SetApartmentState(ApartmentState.MTA);
            probe.Start();
        }

        private static Snapshot Probe()
        {
            long started = Native.Now;
            AppBarData data = new AppBarData { Size = (uint)Marshal.SizeOf(typeof(AppBarData)) };
            if ((SHAppBarMessage(4, ref data).ToUInt64() & 1) != 0) return null; // auto-hide configured
            if (SHAppBarMessage(5, ref data) == UIntPtr.Zero || data.Edge != 3) return null;
            IntPtr window = FindWindow("Shell_TrayWnd", null);
            if (window == IntPtr.Zero || !IsWindowVisible(window)) return null;
            Native.Rect nativeBounds;
            if (!Native.GetWindowRect(window, out nativeBounds)) return null;
            Rect bar = ToRect(data.Bounds);
            if (bar.IsEmpty || bar.Width < 100 || bar.Height < 20 ||
                Math.Abs(nativeBounds.Top - bar.Top) > 2 || Math.Abs(nativeBounds.Left - bar.Left) > 2 ||
                Math.Abs(nativeBounds.Right - bar.Right) > 2 || Math.Abs(nativeBounds.Bottom - bar.Bottom) > 2) return null;
            Native.MonitorInfo info = new Native.MonitorInfo { Size = (uint)Marshal.SizeOf(typeof(Native.MonitorInfo)) };
            if (!Native.GetMonitorInfo(Native.MonitorFromWindow(window, 2), ref info)) return null;
            AutomationElement root = AutomationElement.FromHandle(window);
            if (root == null) return null;
            Queue<AutomationElement> pending = new Queue<AutomationElement>();
            pending.Enqueue(root);
            List<Rect> occupied = new List<Rect>();
            bool foundStart = false;
            int visited = 0;
            TreeWalker walker = TreeWalker.ControlViewWalker;
            while (pending.Count != 0)
            {
                if (++visited > 256 || Native.Now - started > 350) return null; // partial results are unsafe
                AutomationElement item = pending.Dequeue();
                AutomationElement.AutomationElementInformation current = item.Current;
                if (!current.IsOffscreen && item != root)
                {
                    Rect r = current.BoundingRectangle;
                    ControlType type = current.ControlType;
                    bool leaf = type == ControlType.Button || type == ControlType.Text || type == ControlType.Edit ||
                        type == ControlType.ListItem || type == ControlType.MenuItem || type == ControlType.Image;
                    if (leaf && !r.IsEmpty && r.Width > 0 && r.Height > 0 && r.IntersectsWith(bar))
                    {
                        if (r.Width >= bar.Width * .8) return null;
                        r.Intersect(bar); occupied.Add(r);
                        string id = current.AutomationId ?? "", name = current.Name ?? "";
                        if (type == ControlType.Button && (id.IndexOf("start", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name == "开始" || name == "Start")) foundStart = true;
                    }
                }
                for (AutomationElement child = walker.GetFirstChild(item); child != null; child = walker.GetNextSibling(child))
                {
                    if (pending.Count + visited >= 256 || Native.Now - started > 350) return null;
                    pending.Enqueue(child);
                }
            }
            if (!foundStart || occupied.Count < 3 || Native.Now - started > 350) return null;
            return new Snapshot { Monitor = info.Monitor, Bar = bar, Occupied = occupied.ToArray(), Time = started };
        }

        internal static Point Resolve(Native.Rect monitor, Native.Rect work, int dpi)
        {
            Snapshot value;
            lock (Gate) value = snapshot;
            double scale = dpi / 96.0;
            Point fallback = new Point(work.Left + 16 * scale, work.Bottom - 42 * scale);
            if (value == null || Native.Now - value.Time > 5000 || !SameMonitor(monitor, value.Monitor)) return fallback;
            Point inside;
            return TryFindSlot(value.Bar, value.Occupied, scale, out inside) ? inside : fallback;
        }

        // Pure geometry: checks include pulse growth and the shadow's support.
        internal static bool TryFindSlot(Rect bar, Rect[] occupied, double scale, out Point position)
        {
            position = new Point();
            if (scale <= 0 || bar.Width <= 0 || bar.Height < (32 * 1.06 + 4) * scale) return false;
            double bleed = 17 * scale, width = 225 * scale, gap = 8 * scale;
            double cursor = bar.Left + 8 * scale;
            double limit = bar.Right - 120 * scale; // also reserve the notification area
            List<Rect> sorted = new List<Rect>(occupied);
            sorted.Sort(delegate(Rect a, Rect b) { return a.Left.CompareTo(b.Left); });
            foreach (Rect r in sorted)
            {
                if (!r.IntersectsWith(bar)) continue;
                if (cursor + width + 2 * bleed + gap <= r.Left) break;
                if (cursor < r.Right + gap) cursor = r.Right + gap;
            }
            if (cursor + width + 2 * bleed > limit) return false;
            position = new Point(cursor + bleed, bar.Top + (bar.Height - 32 * scale) / 2);
            return true;
        }

        private static Rect ToRect(Native.Rect value)
        { return value.Right > value.Left && value.Bottom > value.Top ? new Rect(value.Left, value.Top, value.Right - value.Left, value.Bottom - value.Top) : Rect.Empty; }
        private static bool SameMonitor(Native.Rect a, Native.Rect b)
        { return a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom; }
    }
}
#endif
