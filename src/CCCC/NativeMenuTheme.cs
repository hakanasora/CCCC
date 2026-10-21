using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace CCCC
{
    internal static class NativeMenuTheme
    {
        // HMENU drawing remains entirely with Windows. These optional uxtheme
        // exports select its process-local light/dark menu theme, as used by
        // Microsoft PowerToys. They are not a documented compatibility contract.
        // https://github.com/microsoft/PowerToys/blob/main/src/modules/ZoomIt/ZoomIt/Utility.cpp
        private delegate int SetAppMode(int mode);
        private delegate void ThemeAction();
        private static bool initialized;
        private static IntPtr module;
        private static SetAppMode setMode;
        private static ThemeAction flushMenus, refreshPolicy;
        private static int previousMode = -1;

        private static void Initialize()
        {
            if (initialized) return;
            initialized = true;
            // Before build 18362, ordinal 135 has a different signature.
            Version version = Environment.OSVersion.Version;
            if (version.Major < 10 || version.Build < 18362) return;
            module = LoadLibraryEx("uxtheme.dll", IntPtr.Zero, 0x00000800);
            if (module == IntPtr.Zero) return;
            IntPtr mode = GetProcAddress(module, new IntPtr(135));
            IntPtr flush = GetProcAddress(module, new IntPtr(136));
            IntPtr refresh = GetProcAddress(module, new IntPtr(104));
            if (mode != IntPtr.Zero) setMode = (SetAppMode)Marshal.GetDelegateForFunctionPointer(mode, typeof(SetAppMode));
            if (flush != IntPtr.Zero) flushMenus = (ThemeAction)Marshal.GetDelegateForFunctionPointer(flush, typeof(ThemeAction));
            if (refresh != IntPtr.Zero) refreshPolicy = (ThemeAction)Marshal.GetDelegateForFunctionPointer(refresh, typeof(ThemeAction));
        }

        internal static void Apply(bool dark)
        {
            Initialize();
            if (setMode == null) return; // Keep the platform's native menu on older Windows.
            int mode = SystemParameters.HighContrast ? 0 : dark ? 2 : 3; // Default / ForceDark / ForceLight.
            if (mode == previousMode) return;
            if (refreshPolicy != null) refreshPolicy();
            setMode(mode);
            if (flushMenus != null) flushMenus();
            previousMode = mode;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryExW")]
        private static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, IntPtr ordinal);
    }
}
