using System;
using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace CCCC
{
    // Original embedded TTF resources are used by WPF only. Native menus use system fonts.
    // No font installation, temporary font files, or runtime network access.
    internal static class EmbeddedFonts
    {
        // Delay pack URI creation until the first real access, after WPF has
        // initialized its URI parser; avoid beforefieldinit eager execution.
        static EmbeddedFonts() { }
        internal const string MediumFile = "vivoSans-Medium.ttf";
        internal const string BoldFile = "vivoSans-Bold.ttf";
        internal static readonly FontWeight TitleWeight = FontWeight.FromOpenTypeWeight(650);
        internal static readonly FontWeight BodyWeight = FontWeight.FromOpenTypeWeight(500);
        private static readonly Uri BaseUri = new Uri("pack://application:,,,/", UriKind.Absolute);
        private static readonly string Component = "/" + Assembly.GetExecutingAssembly().GetName().Name + ";component/Fonts/";
        internal static readonly FontFamily Family = new FontFamily(BaseUri, Component + "#vivo Sans");
    }
}
