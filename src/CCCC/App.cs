using System;
using System.ComponentModel;
using System.Collections.Generic;
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
[assembly: AssemblyVersion("2.4.9.0")]
[assembly: AssemblyFileVersion("2.4.9.0")]
[assembly: AssemblyInformationalVersion("2.4.9-preview.1")]
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
                            Trace.TraceError("CCCC dispatcher: {0} (0x{1:X8})", e.Exception.GetType().Name, e.Exception.HResult);
                            host.Fault();
                        };
                        host.Start();
                        app.Run();
                    }
                    return 0;
                }
                catch (Exception error)
                {
                    Trace.TraceError("CCCC startup: {0} (0x{1:X8})", error.GetType().Name, error.HResult);
                    MessageBox.Show("CCCC 无法启动复制检测。请确认程序运行在正常的 Windows 桌面会话中，再重新打开。",
                        "CCCC", MessageBoxButton.OK, MessageBoxImage.Information);
                    return 1;
                }
                finally { single.ReleaseMutex(); }
            }
        }
    }

    internal sealed class CopyApplication : IDisposable
    {
        private readonly Application app;
        private readonly FeedbackEngine engine = new FeedbackEngine();
        private readonly CopyKeyDetector keys = new CopyKeyDetector();
        private readonly Preferences preferences = new Preferences();
        private ToastWindow toast;
        private readonly StartupRegistration startup = new StartupRegistration(new WindowsStartupStore(), Assembly.GetExecutingAssembly().Location);
        private HwndSource source;
        private ClipboardWorker worker;
        private Native.HookCallback hookCallback;
        private IntPtr hook;
        private Forms.NotifyIcon tray;
        private Forms.MenuItem pauseItem, previewItem, materialItem, startupItem;
        private Forms.MenuItem automaticPositionItem, customPositionItem, cancelPositionItem;
        private readonly Dictionary<ToastPosition, Forms.MenuItem> positionItems = new Dictionary<ToastPosition, Forms.MenuItem>();
#if TASKBAR_EXPERIMENT
        private Forms.MenuItem cardStyleItem, taskbarStyleItem, positionStatusItem;
#endif
        private System.Drawing.Icon trayIcon;
        private DispatcherTimer deadlineTimer;
        private bool paused, disposed, faulted;
        private int generation;
        internal CopyApplication(Application app)
        {
            this.app = app;
            ToastProfile.SetCompact(preferences.UseTaskbar);
            Themes.Refresh();
            toast = new ToastWindow(preferences.UseMaterial, preferences);
            toast.PositionEditFinished += FinishPositionEdit;
        }

        internal void Start()
        {
#if TASKBAR_EXPERIMENT
            if (ToastProfile.Compact) TaskbarPlacement.RequestRefresh();
#endif
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
            Forms.MenuItem heading = new Forms.MenuItem("CCCC 复制确认 · " + ToastProfile.Label) { Enabled = false };
            pauseItem = new Forms.MenuItem("暂停提示") { Checked = paused };
            pauseItem.Click += delegate { SetPaused(!paused); };
            previewItem = new Forms.MenuItem("显示内容预览") { Checked = preferences.ShowPreview };
            previewItem.Click += delegate
            {
                toast.EndPositionEdit(false);
                preferences.ShowPreview = !preferences.ShowPreview;
                previewItem.Checked = preferences.ShowPreview;
                SavePreferences();
                toast.HideAndClear();
                Status(preferences.ShowPreview ? "内容预览已开启" : "内容预览已隐藏",
                    preferences.ShowPreview ? "复制后显示一小段内容" : "复制后仅显示内容类型和次数");
            };
            materialItem = new Forms.MenuItem("高级材质") { Checked = preferences.UseMaterial };
            materialItem.Click += delegate
            {
                toast.EndPositionEdit(false);
                preferences.UseMaterial = !preferences.UseMaterial;
                materialItem.Checked = preferences.UseMaterial;
                SavePreferences();
                toast.SetMaterialEnabled(preferences.UseMaterial);
                Status(preferences.UseMaterial ? "高级材质已开启" : "高级材质已关闭",
                    preferences.UseMaterial ? (ToastProfile.Compact ? "借用任务栏背景，不叠加额外模糊" : "系统允许时显示 Acrylic 材质") : "使用纯色背景，保留文字与动画");
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
            menu.MenuItems.Add(CreatePositionMenu());
#if TASKBAR_EXPERIMENT
            Forms.MenuItem styleMenu = new Forms.MenuItem("提示样式");
            cardStyleItem = new Forms.MenuItem("普通卡片") { RadioCheck = true };
            taskbarStyleItem = new Forms.MenuItem("任务栏单行") { RadioCheck = true };
            cardStyleItem.Click += delegate { SwitchLayout(false); };
            taskbarStyleItem.Click += delegate { SwitchLayout(true); };
            styleMenu.MenuItems.Add(cardStyleItem); styleMenu.MenuItems.Add(taskbarStyleItem);
            positionStatusItem = new Forms.MenuItem() { Enabled = false };
            menu.MenuItems.Add(styleMenu); menu.MenuItems.Add(positionStatusItem);
#endif
            menu.MenuItems.Add(new Forms.MenuItem("-")); menu.MenuItems.Add(exit);
            menu.Popup += delegate
            {
                if (Themes.Refresh()) toast.RefreshAppearance();
                ApplyMenuTheme();
                pauseItem.Checked = paused;
                RefreshLayoutItems();
                materialItem.Checked = preferences.UseMaterial;
                RefreshStartupItem();
            };
            RefreshStartupItem();
            RefreshLayoutItems();
            tray.ContextMenu = menu;
            ApplyMenuTheme();
            tray.DoubleClick += delegate { Status(paused ? "CCCC 已暂停" : "CCCC 正在运行",
                paused ? "取消勾选托盘菜单的“暂停提示”即可恢复" : "复制时自动提示 · 右键托盘图标可设置"); };
        }

        private Forms.MenuItem CreatePositionMenu()
        {
            Forms.MenuItem menu = new Forms.MenuItem("弹窗位置");
            automaticPositionItem = new Forms.MenuItem("任务栏自动定位") { RadioCheck = true };
            automaticPositionItem.Click += delegate { SelectPosition(ToastPosition.Automatic); };
            menu.MenuItems.Add(automaticPositionItem);
            foreach (ToastPosition preset in ToastPlacement.Presets)
            {
                ToastPosition selected = preset;
                Forms.MenuItem item = new Forms.MenuItem(ToastPlacement.Label(selected)) { RadioCheck = true };
                item.Click += delegate { SelectPosition(selected); };
                positionItems.Add(selected, item); menu.MenuItems.Add(item);
            }
            menu.MenuItems.Add(new Forms.MenuItem("-"));
            customPositionItem = new Forms.MenuItem("自定义位置（拖动调整）…") { RadioCheck = true };
            customPositionItem.Click += delegate { BeginPositionEdit(); };
            cancelPositionItem = new Forms.MenuItem("取消位置调整");
            cancelPositionItem.Click += delegate { toast.EndPositionEdit(false); };
            menu.MenuItems.Add(customPositionItem); menu.MenuItems.Add(cancelPositionItem);
            return menu;
        }

        private void RefreshPositionItems()
        {
            if (automaticPositionItem == null) return;
            automaticPositionItem.Visible = ToastProfile.Compact;
            automaticPositionItem.Checked = preferences.Position == ToastPosition.Automatic;
            ToastPosition selected = preferences.Position == ToastPosition.Automatic && !ToastProfile.Compact ?
                ToastPosition.BottomLeft : preferences.Position;
            foreach (KeyValuePair<ToastPosition, Forms.MenuItem> item in positionItems)
                item.Value.Checked = item.Key == selected;
            customPositionItem.Checked = selected == ToastPosition.Custom;
            customPositionItem.Text = toast.IsPositionEditing ? "正在调整：拖动后松开保存" : "自定义位置（拖动调整）…";
            cancelPositionItem.Visible = toast.IsPositionEditing;
        }

        private void SelectPosition(ToastPosition position)
        {
            toast.EndPositionEdit(false);
            preferences.Position = position; SavePreferences();
            toast.HideAndClear(); RefreshLayoutItems();
            Status("弹窗位置：" + ToastPlacement.Label(position), "复制提示将显示在这里");
        }

        private void ResetFeedback()
        {
            generation++; keys.Reset();
            engine.Reset(Native.GetClipboardSequenceNumber());
            deadlineTimer.Stop();
        }

        private void BeginPositionEdit()
        {
            if (disposed || toast.IsPositionEditing) return;
            ResetFeedback();
            toast.BeginPositionEdit(); RefreshPositionItems();
        }

        private void FinishPositionEdit(bool save)
        {
            if (disposed) return;
            ResetFeedback();
            if (save)
            {
                Native.Rect bounds;
                Native.MonitorInfo info = new Native.MonitorInfo { Size = (uint)Marshal.SizeOf(typeof(Native.MonitorInfo)) };
                if (!Native.GetWindowRect(toast.Handle, out bounds) ||
                    !Native.GetMonitorInfo(Native.MonitorFromWindow(toast.Handle, 2), ref info))
                {
                    RefreshPositionItems();
                    Status("未能设置弹窗位置", "请从托盘菜单重新调整"); return;
                }
                int dpi = Native.WindowDpi(toast.Handle);
                double scale = dpi > 0 ? dpi / 96.0 : VisualTreeHelper.GetDpi(toast).DpiScaleX;
                Point point = ToastPlacement.Normalize(ToastPlacement.WorkArea(info),
                    new Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top), scale);
                preferences.Position = ToastPosition.Custom;
                preferences.CustomX = point.X; preferences.CustomY = point.Y;
                preferences.CustomMonitor = Forms.Screen.FromHandle(toast.Handle).DeviceName;
                preferences.HasCustomPosition = true; SavePreferences();
            }
            RefreshLayoutItems();
            Status(save ? "弹窗位置已记住" : "已取消位置调整", save ? "下次启动仍在这里显示" : "继续使用原来的位置");
        }

        private static bool StartupSettingError(Exception error)
        { return error is IOException || error is UnauthorizedAccessException || error is SecurityException || error is ArgumentException; }

        private void SavePreferences()
        {
            if (!preferences.Save() && tray != null)
                tray.ShowBalloonTip(5000, "设置未能保存", "本次选择仍然生效，重启后可能恢复原设置。", Forms.ToolTipIcon.Warning);
        }

        private void RefreshLayoutItems()
        {
            previewItem.Enabled = !ToastProfile.Compact;
            previewItem.Checked = !ToastProfile.Compact && preferences.ShowPreview;
            previewItem.Text = ToastProfile.Compact ? "内容预览（单行样式不显示）" : "显示内容预览";
            materialItem.Text = "高级材质";
            materialItem.Checked = preferences.UseMaterial;
            RefreshPositionItems();
#if TASKBAR_EXPERIMENT
            cardStyleItem.Checked = !ToastProfile.Compact;
            taskbarStyleItem.Checked = ToastProfile.Compact;
            positionStatusItem.Visible = ToastProfile.Compact && preferences.Position == ToastPosition.Automatic;
            positionStatusItem.Text = "定位：" + TaskbarPlacement.LastDecision;
#endif
        }

#if TASKBAR_EXPERIMENT
        private void SwitchLayout(bool compact)
        {
            if (ToastProfile.Compact == compact || disposed) return;
            // Close the old layout and its material before changing the shared
            // profile. Pending clipboard completions belong to the old epoch.
            toast.EndPositionEdit(false);
            ResetFeedback();
            toast = ReplaceLayout(toast, compact, preferences.UseMaterial, preferences);
            toast.PositionEditFinished += FinishPositionEdit;
            preferences.UseTaskbar = compact; SavePreferences();
            if (compact) TaskbarPlacement.RequestRefresh();
            RefreshLayoutItems(); ApplyMenuTheme();
            Status(compact ? "已切换任务栏单行" : "已切换普通卡片", "复制反馈与动画保持不变");
        }

        internal static ToastWindow ReplaceLayout(ToastWindow previous, bool compact, bool material, Preferences placement = null)
        {
            previous.HideAndClear(); previous.Close();
            ToastProfile.SetCompact(compact);
            Themes.MaterialAvailable = false;
            Themes.Refresh();
            return new ToastWindow(material, placement);
        }
#endif

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
                    if (detected && !paused && !toast.IsPositionEditing)
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
                            if (!disposed && !paused && !toast.IsPositionEditing && expectedGeneration == generation)
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
            if (message == Native.WM_CLIPBOARDUPDATE && !paused && !disposed && !toast.IsPositionEditing && worker != null)
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
                    if (disposed || paused || toast.IsPositionEditing || request.Generation != generation ||
                        snapshot.Sequence != Native.GetClipboardSequenceNumber() ||
                        Native.Now - request.EventTime >= FeedbackEngine.ConfirmMilliseconds) return;
                    Show(engine.Observe(snapshot, request.EventTime, Native.Now, request.Foreground));
                }));
            }
            catch (InvalidOperationException) { }
        }

        private void Show(Feedback feedback)
        { if (feedback != null && !disposed && !toast.IsPositionEditing) toast.Present(feedback, preferences.ShowPreview); }

        private void Status(string title, string detail)
        {
            Show(new Feedback { Kind = FeedbackKind.Status, Title = title, Preview = detail,
                Foreground = Native.GetForegroundWindow() });
        }

        private void SetPaused(bool value)
        {
            toast.EndPositionEdit(false);
            paused = value;
            keys.Reset();
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
