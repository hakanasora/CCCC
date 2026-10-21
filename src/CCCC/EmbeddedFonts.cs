using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace CCCC
{
    // Original TTF resources are shared by WPF and the process-private GDI face.
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
        private static IntPtr menuRegistration;
        private static IntPtr menuMemory;
        private static System.Drawing.Text.PrivateFontCollection menuCollection;
        private static System.Drawing.FontFamily menuFamily;

        internal static Uri FontUri(string file)
        { return new Uri(BaseUri, Component + file); }

        internal static Stream Open(string file)
        {
            System.Windows.Resources.StreamResourceInfo resource = Application.GetResourceStream(FontUri(file));
            if (resource == null) throw new IOException("Missing embedded font: " + file);
            return resource.Stream;
        }

        internal static System.Drawing.Font CreateMenuFont()
        {
            if (menuRegistration == IntPtr.Zero)
            {
                byte[] bytes;
                using (Stream input = Open(MediumFile))
                using (MemoryStream buffer = new MemoryStream())
                { input.CopyTo(buffer); bytes = buffer.ToArray(); }
                menuMemory = Marshal.AllocHGlobal(bytes.Length);
                try
                {
                    Marshal.Copy(bytes, 0, menuMemory, bytes.Length);
                    uint count;
                    menuRegistration = AddFontMemResourceEx(menuMemory, (uint)bytes.Length, IntPtr.Zero, out count);
                    if (menuRegistration == IntPtr.Zero || count == 0)
                        throw new InvalidOperationException("Unable to load the embedded menu font.");
                    // WinForms ToolStrip measures/draws through GDI; Font itself
                    // is a GDI+ object. Register with both systems so neither
                    // silently substitutes Microsoft Sans Serif.
                    menuCollection = new System.Drawing.Text.PrivateFontCollection();
                    menuCollection.AddMemoryFont(menuMemory, bytes.Length);
                    menuFamily = menuCollection.Families[0];
                    if (menuFamily.Name != "vivo Sans Medium")
                        throw new InvalidOperationException("Unexpected embedded menu font family.");
                }
                catch { ReleaseMenuFont(); throw; }
            }
            // GDI uses legacy family name (name ID 1). This is the real Medium
            // face, whose legacy subfamily is Regular; do not synthesize Bold.
            return new System.Drawing.Font(menuFamily, 9F, System.Drawing.FontStyle.Regular,
                System.Drawing.GraphicsUnit.Point);
        }

        internal static void ReleaseMenuFont()
        {
            if (menuFamily != null) { menuFamily.Dispose(); menuFamily = null; }
            if (menuCollection != null) { menuCollection.Dispose(); menuCollection = null; }
            if (menuRegistration != IntPtr.Zero) RemoveFontMemResourceEx(menuRegistration);
            menuRegistration = IntPtr.Zero;
            if (menuMemory != IntPtr.Zero) { Marshal.FreeHGlobal(menuMemory); menuMemory = IntPtr.Zero; }
        }

        [DllImport("gdi32.dll")]
        private static extern IntPtr AddFontMemResourceEx(IntPtr data, uint size, IntPtr reserved, out uint count);
        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveFontMemResourceEx(IntPtr registration);
    }
}
