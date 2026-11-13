using System;

namespace CCCC
{
    internal interface IPrankPaymentView
    {
        event EventHandler Closed;
        void Present(IntPtr foreground);
        void CloseForCleanup();
    }

    // The application owns the clipboard epoch. This controller owns exactly one
    // UI session; repeated confirmations deliberately do nothing to that session.
    internal sealed class PrankPaymentController : IDisposable
    {
        internal const string RepositoryUrl = "https://github.com/hakanasora/CCCC";
        private readonly Func<IPrankPaymentView> create;
        private IPrankPaymentView current;
        private bool disposed;
        internal PrankPaymentController() : this(delegate { return new PrankPaymentWindow(); }) { }
        internal PrankPaymentController(Func<IPrankPaymentView> create) { this.create = create; }
        internal bool Handle(Feedback feedback)
        {
            if (disposed || feedback == null || feedback.Kind != FeedbackKind.Confirmed) return false;
            if (current != null) return true;
            IPrankPaymentView window = create();
            current = window;
            window.Closed += ViewClosed;
            try { window.Present(feedback.Foreground); }
            catch { Clear(); throw; }
            return true;
        }
        private void ViewClosed(object sender, EventArgs args)
        {
            IPrankPaymentView window = (IPrankPaymentView)sender;
            window.Closed -= ViewClosed;
            if (ReferenceEquals(current, window)) current = null;
        }
        internal void Clear()
        {
            IPrankPaymentView window = current;
            current = null;
            if (window == null) return;
            window.Closed -= ViewClosed;
            window.CloseForCleanup();
        }
        public void Dispose() { if (disposed) return; disposed = true; Clear(); }
    }
}
