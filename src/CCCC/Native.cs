using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CCCC
{
    internal static class Native
    {
        internal const int WM_CLIPBOARDUPDATE = 0x031D;
        internal const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101;
        internal const int WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
        internal const uint CF_TEXT = 1, CF_BITMAP = 2, CF_DIB = 8, CF_UNICODETEXT = 13,
            CF_HDROP = 15, CF_DIBV5 = 17;
        internal delegate IntPtr HookCallback(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] internal struct KeyboardData
        { internal uint Key, ScanCode, Flags, Time; internal UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect
        { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo
        { internal uint Size; internal Rect Monitor, Work; internal uint Flags; }

        [DllImport("user32.dll", SetLastError = true)] internal static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] internal static extern IntPtr GetClipboardOwner();
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool OpenClipboard(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool CloseClipboard();
        [DllImport("user32.dll")] internal static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("user32.dll")] internal static extern IntPtr GetClipboardData(uint format);
        [DllImport("user32.dll")] internal static extern int CountClipboardFormats();
        [DllImport("kernel32.dll")] internal static extern IntPtr GlobalLock(IntPtr data);
        [DllImport("kernel32.dll")] internal static extern bool GlobalUnlock(IntPtr data);
        [DllImport("kernel32.dll")] internal static extern UIntPtr GlobalSize(IntPtr data);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] internal static extern uint DragQueryFile(IntPtr drop, uint index, StringBuilder buffer, uint size);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int hook, HookCallback callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLong64(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLong64(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong32(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect bounds);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
        internal static bool Down(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }
        internal static uint ProcessFor(IntPtr window)
        { uint value; GetWindowThreadProcessId(window, out value); return value; }
        internal static long Now { get { return (long)(Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency)); } }
        internal static long ExStyle(IntPtr hwnd)
        { return IntPtr.Size == 8 ? GetWindowLong64(hwnd, -20).ToInt64() : GetWindowLong32(hwnd, -20); }
        internal static void MakePassive(IntPtr hwnd)
        {
            long style = ExStyle(hwnd) | 0x08000000L | 0x00000080L | 0x00000020L;
            SetExStyle(hwnd, style);
        }
        internal static void SetExStyle(IntPtr hwnd, long style)
        {
            if (IntPtr.Size == 8) SetWindowLong64(hwnd, -20, new IntPtr(style));
            else SetWindowLong32(hwnd, -20, (int)style);
        }
        internal static void SetOwner(IntPtr hwnd, IntPtr owner)
        {
            if (IntPtr.Size == 8) SetWindowLong64(hwnd, -8, owner);
            else SetWindowLong32(hwnd, -8, owner.ToInt32());
            if (GetWindow(hwnd, 4) != owner) throw new Win32Exception("Could not set the passive window owner.");
        }
        internal static int WindowDpi(IntPtr hwnd)
        {
            try { return (int)GetDpiForWindow(hwnd); }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
            return 0;
        }
    }

    internal interface IClipboardAccess
    {
        bool Open(IntPtr window);
        void Close();
        uint Sequence { get; }
        uint OwnerProcess { get; }
        bool Available(uint format);
        IntPtr Data(uint format);
        int FormatCount { get; }
    }

    internal sealed class WindowsClipboardAccess : IClipboardAccess
    {
        internal static readonly WindowsClipboardAccess Instance = new WindowsClipboardAccess();
        public bool Open(IntPtr window) { return Native.OpenClipboard(window); }
        public void Close() { Native.CloseClipboard(); }
        public uint Sequence { get { return Native.GetClipboardSequenceNumber(); } }
        public uint OwnerProcess { get { return Native.ProcessFor(Native.GetClipboardOwner()); } }
        public bool Available(uint format) { return Native.IsClipboardFormatAvailable(format); }
        public IntPtr Data(uint format) { return Native.GetClipboardData(format); }
        public int FormatCount { get { return Native.CountClipboardFormats(); } }
    }

    internal static class ClipboardReader
    {
        // Only standard clipboard handles are read. No OLE deserialization, file reads,
        // bitmap decoding, HTML loading, or custom-format execution is performed.
        internal static ClipboardSnapshot Read(IntPtr window, uint expected)
        { return Read(window, expected, WindowsClipboardAccess.Instance); }

        internal static ClipboardSnapshot Read(IntPtr window, uint expected, IClipboardAccess access)
        {
            if (!access.Open(window)) return null;
            try
            {
                uint before = access.Sequence;
                if (before != expected) return null;
                ClipboardSnapshot result = new ClipboardSnapshot {
                    Sequence = before, OwnerProcess = access.OwnerProcess,
                    Kind = ContentKind.Other, Preview = "暂不支持预览", TypeLabel = "其他格式" };
                if (access.Available(Native.CF_HDROP))
                {
                    IntPtr handle = access.Data(Native.CF_HDROP);
                    if (handle == IntPtr.Zero) return null;
                    uint count = Native.DragQueryFile(handle, UInt32.MaxValue, null, 0);
                    if (count == 0) return null;
                    StringBuilder name = new StringBuilder(32768);
                    if (Native.DragQueryFile(handle, 0, name, (uint)name.Capacity) == 0) return null;
                    string file = Path.GetFileName(name.ToString().TrimEnd('\\'));
                    if (String.IsNullOrEmpty(file)) file = name.ToString();
                    result.Preview = PreviewText.Shorten(file) + (count > 1 ? " · 共 " + count + " 项" : "");
                    result.TypeLabel = count > 1 ? "文件 · " + count + " 项" : "文件";
                    result.Kind = ContentKind.Files;
                    result.Supported = true;
                }
                else if (access.Available(Native.CF_UNICODETEXT) || access.Available(Native.CF_TEXT))
                {
                    bool unicode = access.Available(Native.CF_UNICODETEXT);
                    IntPtr handle = access.Data(unicode ? Native.CF_UNICODETEXT : Native.CF_TEXT);
                    if (handle == IntPtr.Zero) return null;
                    ulong size = Native.GlobalSize(handle).ToUInt64();
                    if (size == 0) return null;
                    IntPtr pointer = Native.GlobalLock(handle);
                    if (pointer == IntPtr.Zero) return null;
                    try
                    {
                        int maximum = (int)Math.Min(size / (unicode ? 2UL : 1UL), 65536UL);
                        int length = 0;
                        while (length < maximum && (unicode ? Marshal.ReadInt16(pointer, length * 2) != 0 : Marshal.ReadByte(pointer, length) != 0)) length++;
                        string text = unicode ? Marshal.PtrToStringUni(pointer, length) : Marshal.PtrToStringAnsi(pointer, length);
                        result.Preview = PreviewText.Shorten(text);
                    }
                    finally { Native.GlobalUnlock(handle); }
                    result.TypeLabel = "文字";
                    result.Kind = ContentKind.Text;
                    result.Supported = true;
                }
                else if (access.Available(Native.CF_DIBV5) || access.Available(Native.CF_DIB) || access.Available(Native.CF_BITMAP))
                {
                    uint format = access.Available(Native.CF_DIBV5) ? Native.CF_DIBV5 :
                        access.Available(Native.CF_DIB) ? Native.CF_DIB : Native.CF_BITMAP;
                    if (access.Data(format) == IntPtr.Zero) return null;
                    result.Preview = result.TypeLabel = "图片";
                    result.Kind = ContentKind.Image;
                    result.Supported = true;
                }
                else if (access.FormatCount <= 0) return null;
                return access.Sequence == before ? result : null;
            }
            finally { access.Close(); }
        }
    }

    internal sealed class ReadRequest
    {
        internal uint Sequence;
        internal long EventTime;
        internal int Generation;
        internal IntPtr Foreground;
    }

    // A single background STA owns potentially delayed clipboard reads. It keeps only
    // the newest queued request, and its results never block the UI or keyboard hook.
    internal sealed class ClipboardWorker : IDisposable
    {
        private readonly object gate = new object();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly IntPtr hwnd;
        private readonly Action<ReadRequest, ClipboardSnapshot> complete;
        private ReadRequest latest;
        private bool stopped;
        internal ClipboardWorker(IntPtr hwnd, Action<ReadRequest, ClipboardSnapshot> complete)
        {
            this.hwnd = hwnd; this.complete = complete;
            Thread thread = new Thread(Work) { IsBackground = true, Name = "CCCC clipboard reader" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
        internal void Enqueue(ReadRequest request)
        {
            lock (gate) { if (stopped) return; latest = request; wake.Set(); }
        }
        private void Work()
        {
            for (;;)
            {
                wake.WaitOne();
                ReadRequest request;
                lock (gate) { if (stopped) return; request = latest; latest = null; }
                if (request == null) continue;
                ClipboardSnapshot result = null;
                int[] delays = { 0, 40, 80, 160, 240, 320, 400 };
                foreach (int delay in delays)
                {
                    if (delay > 0) Thread.Sleep(delay);
                    lock (gate) { if (stopped) return; if (latest != null) break; }
                    if (Native.Now - request.EventTime >= FeedbackEngine.ConfirmMilliseconds ||
                        Native.GetClipboardSequenceNumber() != request.Sequence) break;
                    try { result = ClipboardReader.Read(hwnd, request.Sequence); }
                    catch (Exception) { result = null; }
                    if (result != null) break;
                }
                if (result != null && Native.Now - request.EventTime < FeedbackEngine.ConfirmMilliseconds)
                    complete(request, result);
            }
        }
        public void Dispose()
        {
            lock (gate) { stopped = true; latest = null; wake.Set(); }
            // A background read may be waiting on a source app's delayed rendering.
            // Do not join it or close the event while the worker can still use it.
        }
    }
}
