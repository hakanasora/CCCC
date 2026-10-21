using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

[assembly: AssemblyTitle("CCCC 复制确认")]
[assembly: AssemblyDescription("Windows 复制确认与连按反馈工具")]
[assembly: AssemblyCompany("CCCC")]
[assembly: AssemblyProduct("CCCC")]
[assembly: AssemblyVersion("2.4.6.0")]
[assembly: AssemblyFileVersion("2.4.6.0")]
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace CCCC
{
    internal static class Program
    {
        [STAThread]
        internal static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--render-preview")
            {
                Application previewApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                ToastWindow sample = new ToastWindow();
                sample.RenderSample(args[1]);
                sample.Close();
                previewApp.Shutdown();
                return 0;
            }
            bool first;
            using (Mutex single = new Mutex(true, "Local\\CCCC.CopyFeedback.v1", out first))
            {
                if (!first) return 0;
                try
                {
                    Forms.Application.EnableVisualStyles();
                    Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    using (CopyApplication host = new CopyApplication(app))
                    {
                        app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
                        {
                            e.Handled = true;
                            host.Fault();
                        };
                        host.Start();
                        app.Run();
                    }
                    return 0;
                }
                catch (Exception)
                {
                    MessageBox.Show("CCCC 无法启动复制检测。请确认程序运行在正常的 Windows 桌面会话中，再重新打开。",
                        "CCCC", MessageBoxButton.OK, MessageBoxImage.Information);
                    return 1;
                }
                finally { single.ReleaseMutex(); }
            }
        }
    }

    internal sealed class Preferences
    {
        internal bool ShowPreview = true;
        internal bool UseMaterial = true;
        private readonly string path;
        internal Preferences() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCCC", "settings.txt")) { }
        internal Preferences(string path)
        {
            this.path = path;
            try
            {
                if (File.Exists(path))
                    foreach (string raw in File.ReadAllLines(path))
                    {
                        string line = raw.Trim();
                        if (line == "preview=off") ShowPreview = false;
                        else if (line == "preview=on") ShowPreview = true;
                        else if (line == "material=off") UseMaterial = false;
                        else if (line == "material=on") UseMaterial = true;
                    }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        internal void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, (ShowPreview ? "preview=on" : "preview=off") + Environment.NewLine +
                    (UseMaterial ? "material=on" : "material=off") + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal sealed class CopyApplication : IDisposable
    {
        private readonly Application app;
        private readonly FeedbackEngine engine = new FeedbackEngine();
        private readonly CopyKeyDetector keys = new CopyKeyDetector();
        private readonly Preferences preferences = new Preferences();
        private readonly ToastWindow toast;
        private readonly StartupRegistration startup = new StartupRegistration(new WindowsStartupStore(), Assembly.GetExecutingAssembly().Location);
        private HwndSource source;
        private ClipboardWorker worker;
        private Native.HookCallback hookCallback;
        private IntPtr hook;
        private Forms.NotifyIcon tray;
        private Forms.MenuItem pauseItem, previewItem, materialItem, startupItem;
        private System.Drawing.Icon trayIcon;
        private DispatcherTimer deadlineTimer;
        private bool paused, disposed, faulted;
        private int generation;
        internal CopyApplication(Application app)
        { this.app = app; toast = new ToastWindow(preferences.UseMaterial); }

        internal void Start()
        {
            HwndSourceParameters parameters = new HwndSourceParameters("CCCC Clipboard Listener") {
                WindowStyle = unchecked((int)0x80000000), Width = 0, Height = 0 };
            source = new HwndSource(parameters);
            source.AddHook(WindowMessage);
            if (!Native.AddClipboardFormatListener(source.Handle)) throw new Win32Exception();
            engine.Reset(Native.GetClipboardSequenceNumber());
            worker = new ClipboardWorker(source.Handle, ReadComplete);
            hookCallback = KeyboardEvent;
            hook = Native.SetWindowsHookEx(13, hookCallback, Native.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) throw new Win32Exception();
            CreateTray();
            deadlineTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
            deadlineTimer.Tick += delegate
            {
                if (!paused)
                {
                    Show(engine.Expire(Native.Now, engine.HasPending ? Native.GetClipboardSequenceNumber() : 0));
                    if (!engine.HasPending && engine.Count == 0) deadlineTimer.Stop();
                }
                else deadlineTimer.Stop();
            };
            deadlineTimer.Start();
            Status("CCCC 已启动", "现在复制试试吧 · 右键托盘图标可设置");
        }

        private void CreateTray()
        {
            trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "CCCC · 复制确认", Visible = true };
            Forms.ContextMenu menu = new Forms.ContextMenu();
            Forms.MenuItem heading = new Forms.MenuItem("CCCC 复制确认 · 2.4.6") { Enabled = false };
            pauseItem = new Forms.MenuItem("暂停提示") { Checked = paused };
            pauseItem.Click += delegate { SetPaused(!paused); };
            previewItem = new Forms.MenuItem("显示内容预览") { Checked = preferences.ShowPreview };
            previewItem.Click += delegate
            {
                preferences.ShowPreview = !preferences.ShowPreview;
                previewItem.Checked = preferences.ShowPreview;
                preferences.Save();
                toast.HideAndClear();
                Status(preferences.ShowPreview ? "内容预览已开启" : "内容预览已隐藏",
                    preferences.ShowPreview ? "复制后显示一小段内容" : "复制后仅显示内容类型和次数");
            };
            materialItem = new Forms.MenuItem("高级材质") { Checked = preferences.UseMaterial };
            materialItem.Click += delegate
            {
                preferences.UseMaterial = !preferences.UseMaterial;
                materialItem.Checked = preferences.UseMaterial;
                preferences.Save();
                toast.SetMaterialEnabled(preferences.UseMaterial);
                Status(preferences.UseMaterial ? "高级材质已开启" : "高级材质已关闭",
                    preferences.UseMaterial ? "系统允许时显示 Acrylic 材质" : "使用纯色背景，保留文字与动画");
            };
            Forms.MenuItem exit = new Forms.MenuItem("退出 CCCC");
            startupItem = new Forms.MenuItem("开机自启动");
            startupItem.Click += delegate
            {
                try
                {
                    StartupState state = startup.SetEnabled(!startup.Read().Enabled);
                    SetStartupItem(state);
                    Status(state.Enabled ? "开机自启动已开启" : "开机自启动已关闭",
                        state.Enabled ? "下次登录 Windows 时启动 CCCC" : "需要时双击程序启动");
                }
                catch (Exception error)
                {
                    if (!StartupSettingError(error)) throw;
                    RefreshStartupItem();
                    Show(new Feedback { Kind = FeedbackKind.Unconfirmed, Title = "未能更改开机自启动",
                        Preview = "设置未能保存，请稍后重试", Foreground = Native.GetForegroundWindow() });
                }
            };
            exit.Click += delegate { app.Shutdown(); };
            menu.MenuItems.Add(heading); menu.MenuItems.Add(new Forms.MenuItem("-"));
            menu.MenuItems.Add(pauseItem); menu.MenuItems.Add(previewItem); menu.MenuItems.Add(startupItem);
            menu.MenuItems.Add(materialItem);
            menu.MenuItems.Add(new Forms.MenuItem("-")); menu.MenuItems.Add(exit);
            menu.Popup += delegate
            {
                if (Themes.Refresh()) toast.RefreshAppearance();
                ApplyMenuTheme();
                pauseItem.Checked = paused;
                previewItem.Checked = preferences.ShowPreview;
                materialItem.Checked = preferences.UseMaterial;
                RefreshStartupItem();
            };
            RefreshStartupItem();
            tray.ContextMenu = menu;
            ApplyMenuTheme();
            tray.DoubleClick += delegate { Status(paused ? "CCCC 已暂停" : "CCCC 正在运行",
                paused ? "取消勾选托盘菜单的“暂停提示”即可恢复" : "复制时自动提示 · 右键托盘图标可设置"); };
        }

        private static bool StartupSettingError(Exception error)
        { return error is IOException || error is UnauthorizedAccessException || error is SecurityException || error is ArgumentException; }

        private void SetStartupItem(StartupState state)
        {
            startupItem.Enabled = true;
            startupItem.Checked = state.Enabled;
            // Native menus have no ToolStrip tooltip; retain the stale-path
            // notice in the item's label without altering its checked state.
            startupItem.Text = state.Enabled && !state.CurrentLocation ? "开机自启动（其他位置）" : "开机自启动";
        }

        private void RefreshStartupItem()
        {
            try { SetStartupItem(startup.Read()); }
            catch (Exception error)
            {
                if (!StartupSettingError(error)) throw;
                startupItem.Checked = false; startupItem.Enabled = false;
                startupItem.Text = "开机自启动（读取失败）";
            }
        }

        private IntPtr KeyboardEvent(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && !disposed)
            {
                int type = message.ToInt32();
                if (type == Native.WM_KEYDOWN || type == Native.WM_SYSKEYDOWN ||
                    type == Native.WM_KEYUP || type == Native.WM_SYSKEYUP)
                {
                    Native.KeyboardData key = (Native.KeyboardData)Marshal.PtrToStructure(data, typeof(Native.KeyboardData));
                    bool down = type == Native.WM_KEYDOWN || type == Native.WM_SYSKEYDOWN;
                    bool detected = keys.Feed((int)key.Key, down, Native.Down(0x11),
                        Native.Down(0x10) || Native.Down(0x12) || Native.Down(0x5B) || Native.Down(0x5C));
                    if (detected && !paused)
                    {
                        long time = Native.Now;
                        IntPtr foreground = Native.GetForegroundWindow();
                        uint process = Native.ProcessFor(foreground);
                        uint sequence = Native.GetClipboardSequenceNumber();
                        int expectedGeneration = generation;
                        // Schedule before the source app processes the key. No clipboard
                        // access or visual work is performed in the low-level hook.
                        app.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(delegate
                        {
                            if (!disposed && !paused && expectedGeneration == generation)
                            {
                                engine.CopyPressed(time, foreground, process, sequence);
                                toast.CopyPressed(engine.Count, foreground, preferences.ShowPreview, time);
                                deadlineTimer.Start();
                            }
                        }));
                    }
                }
            }
            return Native.CallNextHookEx(hook, code, message, data);
        }

        private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr w, IntPtr l, ref bool handled)
        {
            if ((message == 0x001A || message == 0x031A || message == 0x0320 || message == 0x0218) && !disposed)
            {
                if (Themes.Refresh()) { toast.RefreshAppearance(); ApplyMenuTheme(); }
            }
            if (message == Native.WM_CLIPBOARDUPDATE && !paused && !disposed && worker != null)
            {
                worker.Enqueue(new ReadRequest { Sequence = Native.GetClipboardSequenceNumber(),
                    EventTime = Native.Now, Generation = generation, Foreground = Native.GetForegroundWindow() });
            }
            return IntPtr.Zero;
        }

        private void ApplyMenuTheme()
        {
            NativeMenuTheme.Apply(Themes.Current.Dark);
        }

        private void ReadComplete(ReadRequest request, ClipboardSnapshot snapshot)
        {
            if (disposed || app.Dispatcher.HasShutdownStarted) return;
            try
            {
                app.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(delegate
                {
                    if (disposed || paused || request.Generation != generation ||
                        snapshot.Sequence != Native.GetClipboardSequenceNumber() ||
                        Native.Now - request.EventTime >= FeedbackEngine.ConfirmMilliseconds) return;
                    Show(engine.Observe(snapshot, request.EventTime, Native.Now, request.Foreground));
                }));
            }
            catch (InvalidOperationException) { }
        }

        private void Show(Feedback feedback)
        { if (feedback != null && !disposed) toast.Present(feedback, preferences.ShowPreview); }

        private void Status(string title, string detail)
        {
            Show(new Feedback { Kind = FeedbackKind.Status, Title = title, Preview = detail,
                Foreground = Native.GetForegroundWindow() });
        }

        private void SetPaused(bool value)
        {
            paused = value;
            generation++;
            engine.Reset(Native.GetClipboardSequenceNumber());
            pauseItem.Checked = paused;
            tray.Text = paused ? "CCCC · 已暂停" : "CCCC · 复制确认";
            toast.HideAndClear();
            Status(paused ? "CCCC 已暂停" : "CCCC 已恢复", paused ? "需要时可从托盘恢复提示" : "现在复制试试吧");
        }

        internal void Fault()
        {
            if (faulted) { app.Shutdown(1); return; }
            faulted = true;
            if (pauseItem != null) SetPaused(true);
            if (tray != null) tray.ShowBalloonTip(5000, "CCCC 已暂停", "检测遇到异常，请退出后重新打开。", Forms.ToolTipIcon.Info);
        }


        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (deadlineTimer != null) deadlineTimer.Stop();
            if (hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
            if (worker != null) worker.Dispose();
            if (source != null) { Native.RemoveClipboardFormatListener(source.Handle); source.Dispose(); }
            if (tray != null) { tray.Visible = false; if (tray.ContextMenu != null) tray.ContextMenu.Dispose(); tray.Dispose(); }
            if (trayIcon != null) trayIcon.Dispose();
            toast.Close();
        }
    }
}
