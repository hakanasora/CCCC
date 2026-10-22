using System;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace CCCC
{
    internal sealed class Appearance
    {
        internal bool Dark, Transparent;
        internal Color Fill { get { return Dark ? Color.FromRgb(40, 40, 43) : Color.FromRgb(255, 255, 255); } }
        internal double Tint { get { return Dark ? .77 : .80; } }
        // User-selected taskbar surface; the ordinary card keeps Fill/Tint.
        internal Color TaskbarFill { get { return Dark ? Colors.White : Color.FromRgb(240, 240, 240); } }
        internal double TaskbarTint { get { return Dark ? .06 : .60; } }
        internal double TaskbarShadow { get { return Dark ? 0 : .0235; } }
        internal string Text { get { return Dark ? "#F5F5F7" : "#1D1D1F"; } }
        internal string Detail { get { return Dark ? "#AEAEB2" : "#6E6E73"; } }
        internal string Rim { get { return Dark ? "#636366" : "#C7C7CC"; } }
        internal string Badge { get { return Dark ? "#86DDBA" : "#15976B"; } }
        internal string Green { get { return Dark ? "#244A3B" : "#DDF6EB"; } }
        internal string Counter { get { return Dark ? "#3A3A3C" : "#E9E9ED"; } }
        internal string CountText { get { return Text; } }
    }
    internal static class Themes
    {
        internal static Appearance Current = Read();
        internal static bool MaterialAvailable;
        internal static bool MaterialEnabled = true;
        internal static bool UseAcrylic { get { return MaterialEnabled && MaterialAvailable && Current.Transparent; } }
        internal static Appearance Read()
        {
            Appearance value = new Appearance { Transparent = true };
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize"))
                {
                    if (key != null) { value.Dark = Convert.ToInt32(key.GetValue(ToastProfile.Compact ? "SystemUsesLightTheme" : "AppsUseLightTheme", 1)) == 0;
                        value.Transparent = Convert.ToInt32(key.GetValue("EnableTransparency", 1)) != 0; }
                }
            }
            catch (Exception) { }
            if (SystemParameters.HighContrast) value.Transparent = false;
            try { if (Windows.System.Power.PowerManager.EnergySaverStatus == Windows.System.Power.EnergySaverStatus.On) value.Transparent = false; }
            catch (Exception) { }
            return value;
        }
        internal static bool Refresh()
        {
            Appearance next = Read();
            bool changed = next.Dark != Current.Dark || next.Transparent != Current.Transparent;
            Current = next; return changed;
        }
        internal static System.Drawing.Color Drawing(string text) { return System.Drawing.ColorTranslator.FromHtml(text); }
    }
}
