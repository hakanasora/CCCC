using System;
using System.IO;
using System.Reflection;
using System.Threading;

namespace CCCC
{
    internal static class QualityChecks
    {
        private sealed class ClipboardAccess : IClipboardAccess
        {
            internal bool Opens = true, Fail;
            internal int Closed;
            public bool Open(IntPtr window) { return Opens; }
            public void Close() { Closed++; }
            public uint Sequence { get { return 42; } }
            public uint OwnerProcess { get { return 1; } }
            public bool Available(uint format) { if (Fail) throw new IOException("Injected"); return false; }
            public IntPtr Data(uint format) { return IntPtr.Zero; }
            public int FormatCount { get { return 1; } }
        }
        private sealed class StartupStore : IStartupStore
        {
            internal string Value;
            public string Read() { return Value; }
            public void Write(string command) { Value = command; }
            public void Delete() { Value = null; }
        }
        private static int count;
        private static void Check(bool value, string name)
        { if (!value) throw new Exception(name); count++; }
        [STAThread]
        internal static int Main()
        {
            string directory = Path.Combine(Path.GetTempPath(), "CCCC-quality-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "settings.txt");
                Preferences p = new Preferences(path);
                Check(p.ShowPreview && p.UseMaterial, "missing settings defaults");
                p.ShowPreview = false; p.UseMaterial = false; p.UseTaskbar = false;
                Check(p.Save(), "initial save");
                Preferences loaded = new Preferences(path);
                Check(!loaded.ShowPreview && !loaded.UseMaterial && !loaded.UseTaskbar, "round trip");
                p.UseMaterial = true; Check(p.Save(), "atomic overwrite");
                Check(new Preferences(path).UseMaterial, "overwrite persisted");
                using (FileStream held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    p.ShowPreview = true;
                    Check(!p.Save() && p.LastError != null, "locked settings reports failure");
                }
                Check(!new Preferences(path).ShowPreview, "failure preserves old file");
                Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "failed write cleans temporary file");
                File.WriteAllText(path, "garbage\nmaterial=off\npreview=on\nlayout=card\n");
                loaded = new Preferences(path);
                Check(!loaded.UseMaterial && loaded.ShowPreview && !loaded.UseTaskbar, "unknown settings ignored");
                p.Corner = ToastCorner.TopRight;
                Check(p.Save() && new Preferences(path).Corner == ToastCorner.TopRight, "corner round trip");
                File.WriteAllText(path, "garbage\nposition=bottomleft\n");
                Check(new Preferences(path).Corner == ToastCorner.BottomLeft, "corner parse");
                File.WriteAllText(path, "position=nonsense\n");
                Check(new Preferences(path).Corner == ToastCorner.BottomLeft, "unknown corner falls back to default");
                CopyKeyDetector keys = new CopyKeyDetector();
                Check(keys.Feed(0x43, true, true, false), "Ctrl C detected");
                Check(!keys.Feed(0x43, true, true, false), "held key suppressed");
                keys.Reset(); Check(keys.Feed(0x43, true, true, false), "reset restores key detection");
                keys.Feed(0x43, false, false, false);
                Check(!keys.Feed(0x43, true, true, true), "modified shortcut ignored");
                Check(PreviewText.Shorten(null) == "空白文字", "null preview");
                Check(PreviewText.Shorten(" a\r\n b ") == "a b", "whitespace preview");
                Check(PreviewText.Shorten("a\u202eb") == "ab", "bidi control removed");
                Check(PreviewText.Shorten(new string('x', 37)) == new string('x', 36) + "…", "preview length bound");
                StartupStore store = new StartupStore();
                StartupRegistration startup = new StartupRegistration(store, Path.Combine(directory, "space name.exe"));
                Check(!startup.Read().Enabled, "startup initially disabled");
                Check(startup.SetEnabled(true).CurrentLocation && store.Value.StartsWith("\"") && store.Value.EndsWith("\""), "startup path quoted");
                Check(startup.SetEnabled(true).Enabled, "repeated startup enable");
                Check(!startup.SetEnabled(false).Enabled && !startup.SetEnabled(false).Enabled, "repeated startup disable");
                FeedbackEngine engine = new FeedbackEngine();
                engine.Reset(1); engine.CopyPressed(0, new IntPtr(1), 9, 1);
                Check(engine.Observe(null, 1, 1, IntPtr.Zero) == null, "null observation");
                Feedback result = engine.Observe(new ClipboardSnapshot { Sequence = 2, OwnerProcess = 9, Supported = true }, 10, 10, new IntPtr(1));
                Check(result != null && result.Count == 1 && result.Kind == FeedbackKind.Confirmed, "verified copy");
                Check(engine.Observe(new ClipboardSnapshot { Sequence = 2 }, 11, 11, IntPtr.Zero) == null, "duplicate observation");
                engine.CopyPressed(100, new IntPtr(1), 9, 2);
                Check(engine.Expire(1599, 2) == null, "confirmation deadline boundary");
                Check(engine.Expire(1600, 2).Kind == FeedbackKind.Retained, "retained clipboard confirmation");
                engine.Reset(2); engine.CopyPressed(2000, new IntPtr(1), 9, 2);
                Check(engine.Expire(3500, 2).Kind == FeedbackKind.Unconfirmed, "unconfirmed copy");
                ClipboardAccess access = new ClipboardAccess { Opens = false };
                Check(ClipboardReader.Read(IntPtr.Zero, 42, access) == null && access.Closed == 0, "busy clipboard not closed by nonowner");
                access = new ClipboardAccess();
                Check(ClipboardReader.Read(IntPtr.Zero, 43, access) == null && access.Closed == 1, "stale clipboard read closes handle");
                access = new ClipboardAccess();
                Check(!ClipboardReader.Read(IntPtr.Zero, 42, access).Supported && access.Closed == 1, "unsupported format closes handle");
                access = new ClipboardAccess { Fail = true };
                try { ClipboardReader.Read(IntPtr.Zero, 42, access); Check(false, "expected injected read error"); }
                catch (IOException) { Check(access.Closed == 1, "exception still releases clipboard"); }
                ClipboardWorker worker = new ClipboardWorker(IntPtr.Zero, delegate { throw new Exception("unexpected callback"); });
                Thread thread = (Thread)typeof(ClipboardWorker).GetField("thread", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(worker);
                worker.Dispose(); worker.Dispose();
                Check(thread.Join(2000), "idle worker exits without blocking UI");
                worker.Enqueue(new ReadRequest());
                Check(!thread.IsAlive, "enqueue after disposal ignored");
                AutoResetEvent wake = (AutoResetEvent)typeof(ClipboardWorker).GetField("wake", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(worker);
                try { wake.WaitOne(0); Check(false, "expected disposed worker event"); }
                catch (ObjectDisposedException) { Check(true, "worker releases event"); }
                for (int i = 0; i < 10; i++)
                {
                    worker = new ClipboardWorker(IntPtr.Zero, delegate { throw new Exception("unexpected stale read"); });
                    thread = (Thread)typeof(ClipboardWorker).GetField("thread", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(worker);
                    worker.Enqueue(new ReadRequest { EventTime = Int64.MinValue / 2 });
                    worker.Dispose();
                    Check(thread.Join(2000), "queued worker stop " + i);
                }
                Console.WriteLine("PASS: " + count + " quality assertions; no user settings, startup registration or clipboard content changed.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally { foreach (string file in Directory.GetFiles(directory)) File.Delete(file); Directory.Delete(directory); }
        }
    }
}
