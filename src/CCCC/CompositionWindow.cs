using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CCCC
{
    // This HWND has no WPF render target and no GDI background brush. Only the
    // Windows compositor draws into it, so window messages cannot clear it white.
    internal sealed class CompositionWindow : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr w, IntPtr l);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WindowClass
        {
            internal uint Size, Style;
            internal IntPtr Procedure;
            internal int ClassExtra, WindowExtra;
            internal IntPtr Instance, Icon, Cursor, Background;
            [MarshalAs(UnmanagedType.LPWStr)] internal string MenuName;
            [MarshalAs(UnmanagedType.LPWStr)] internal string ClassName;
            internal IntPtr SmallIcon;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassEx(ref WindowClass definition);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string title, uint style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
        private const string ClassName = "CCCC.PassiveComposition.v211";
        private static readonly WindowProcedure Procedure = ProcessMessage;
        private static bool registered;
        private IntPtr handle;
        internal IntPtr Handle { get { return handle; } }
#if TESTS
        internal static Action<IntPtr, uint> ObservedMessage;
#endif
        internal CompositionWindow()
        {
            if (!registered)
            {
                WindowClass definition = new WindowClass { Size = (uint)Marshal.SizeOf(typeof(WindowClass)),
                    Procedure = Marshal.GetFunctionPointerForDelegate(Procedure), Instance = Native.GetModuleHandle(null),
                    ClassName = ClassName, Background = IntPtr.Zero };
                if (RegisterClassEx(ref definition) == 0 && Marshal.GetLastWin32Error() != 1410)
                    throw new Win32Exception();
                registered = true;
            }
            // NOACTIVATE | NOREDIRECTIONBITMAP | TOOLWINDOW | TRANSPARENT | TOPMOST.
            handle = CreateWindowEx(0x082000A8, ClassName, "CCCC " + ToastProfile.Label + " Acrylic", 0x80000000,
                0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, Native.GetModuleHandle(null), IntPtr.Zero);
            if (handle == IntPtr.Zero) throw new Win32Exception();
        }
        private static IntPtr ProcessMessage(IntPtr hwnd, uint message, IntPtr w, IntPtr l)
        {
#if TESTS
            if (ObservedMessage != null) ObservedMessage(hwnd, message);
#endif
            if (message == 0x14) return new IntPtr(1); // No GDI background erase.
            if (message == 0x21) return new IntPtr(3); // MA_NOACTIVATE.
            if (message == 0x84) return new IntPtr(-1); // HTTRANSPARENT.
            return DefWindowProc(hwnd, message, w, l);
        }
        public void Dispose()
        { if (handle != IntPtr.Zero) { DestroyWindow(handle); handle = IntPtr.Zero; } }
    }
}
