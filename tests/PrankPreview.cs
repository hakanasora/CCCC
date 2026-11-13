using System;
using System.Windows;

namespace CCCC
{
    // Explicit UI-only preview. No clipboard listener, key hook, tray, settings
    // or original toast is constructed; does not require the original fonts.
    internal static class PrankPreview
    {
        [STAThread]
        internal static void Main()
        {
            Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            PrankPaymentWindow window = new PrankPaymentWindow();
            window.Closed += delegate { app.Shutdown(); };
            app.SessionEnding += delegate { window.CloseForCleanup(); };
            window.Present(IntPtr.Zero);
            app.Run();
        }
    }
}
