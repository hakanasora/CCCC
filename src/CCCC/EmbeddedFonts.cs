using System;
using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace CCCC
{
    // Shared WPF font settings. Default builds use installed Windows UI fonts;
    // explicitly supplying -FontDirectory retains the original embedded faces.
    // System font files are never copied, embedded, installed or downloaded.
    internal static class EmbeddedFonts
    {
        // Delay pack URI creation until the first real access, after WPF has
        // initialized its URI parser; avoid beforefieldinit eager execution.
        static EmbeddedFonts() { }
        internal static readonly FontWeight TitleWeight = FontWeight.FromOpenTypeWeight(650);
        internal static readonly FontWeight BodyWeight = FontWeight.FromOpenTypeWeight(500);
#if EMBED_VIVO_FONTS
        private static readonly Uri BaseUri = new Uri("pack://application:,,,/", UriKind.Absolute);
        private static readonly string Component = "/" + Assembly.GetExecutingAssembly().GetName().Name + ";component/Fonts/";
        internal static readonly FontFamily Family = new FontFamily(BaseUri, Component + "#vivo Sans");
#else
        internal static readonly FontFamily Family = new FontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI");
#endif
    }
}
