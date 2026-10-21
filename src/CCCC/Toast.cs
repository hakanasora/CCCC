using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CCCC
{
    internal sealed class ToastWindow : Window
    {
        internal const double OuterWidth = CardBackgroundRenderer.LogicalWidth;
        internal const double OuterHeight = CardBackgroundRenderer.LogicalHeight;
        private readonly Grid surface;
        private readonly Grid counterContent;
        private readonly Image background;
        private readonly Border card, badge, counter;
        private readonly SampledText title, detail, symbol, times, overflow;
        private readonly SampledText[] glyphs;
        private readonly TextPixelWorkspace textPixels = new TextPixelWorkspace();
        private readonly Dictionary<string, Brush> palette = new Dictionary<string, Brush>();
        private readonly System.Windows.Shapes.Path checkMark;
        private static readonly Geometry CheckGeometry = MakeCheckGeometry();
        private readonly DispatcherTimer hideTimer;
        private readonly CardBackgroundCache backgrounds = new CardBackgroundCache();
        private readonly CopyMotion motion = new CopyMotion();
        private readonly TitleTransition titleTransition = new TitleTransition();
        private readonly WindowFade windowFade = new WindowFade();
        private readonly ScaleTransform cardScale = new ScaleTransform(1, 1), countScale = new ScaleTransform(1, 1);
        private AcrylicHost acrylic;
        private bool rendering, preparing, pendingEntrance, pendingHit, currentPreview = true;
        private bool deferredPulse;
        private long lastCopySignal;
        private double lastCopyQueueMilliseconds;
        private int preparedRevision;
        internal string MaterialFailure { get; private set; }
        internal bool HasNativeMaterial { get { return acrylic != null && Themes.UseAcrylic; } }
        private IntPtr hwnd, anchorWindow;
        private int revision;
        private bool positioning, refreshQueued, closed;
        internal Feedback Current { get; private set; }
        internal IntPtr Handle { get { return new WindowInteropHelper(this).EnsureHandle(); } }
        internal int BackgroundBuildCount { get { return backgrounds.BuildCount; } }
        internal int BackgroundCacheCount { get { return backgrounds.Count; } }
        internal BackgroundFrame CurrentBackground { get; private set; }
        internal double LastPreparationMilliseconds { get; private set; }
#if PRESENTATION_CHECKS
        internal bool TestFailMaterialCreation;
        internal int TestMaterialAttempts;
        internal Action<long, bool> TestFrameObserved;
        internal bool TestPendingEntrance { get { return pendingEntrance; } }
        internal bool TestPendingHit { get { return pendingHit; } }
        internal bool TestMotionRunning { get { return motion.Running; } }
        internal bool TestFramesRunning { get { return rendering; } }
        internal double TestScale { get { return cardScale.ScaleX; } }
        internal IntPtr TestMaterialHandle { get { return acrylic == null ? IntPtr.Zero : acrylic.Handle; } }
#endif

        private Brush Brush(string value)
        {
            Brush existing;
            if (palette.TryGetValue(value, out existing)) return existing;
            SolidColorBrush color = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            color.Freeze(); palette.Add(value, color); return color;
        }

        private static Geometry MakeCheckGeometry()
        {
            Geometry geometry = Geometry.Parse("M3,9 L7,13 L15,5");
            geometry.Freeze();
            return geometry;
        }

        internal ToastWindow() : this(Themes.MaterialEnabled) { }
        internal ToastWindow(bool materialEnabled)
        {
            Themes.MaterialEnabled = materialEnabled;
            Title = "CCCC 2.4.6 · 复制提示";
            Width = OuterWidth; Height = OuterHeight;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; ShowActivated = false; Topmost = true;
            Focusable = false; IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            FontFamily = EmbeddedFonts.Family;
            FontWeight = EmbeddedFonts.BodyWeight;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
            TextOptions.SetTextHintingMode(this, TextHintingMode.Fixed);
            surface = new Grid { Width = OuterWidth, Height = OuterHeight };
            surface.RenderTransform = cardScale; surface.RenderTransformOrigin = new Point(.5, .5);
            background = new Image { HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top, Stretch = Stretch.Fill, SnapsToDevicePixels = true };
            RenderOptions.SetBitmapScalingMode(background, BitmapScalingMode.NearestNeighbor);
            surface.Children.Add(background);
            // Transparent content host. All card pixels are in the background layer;
            // no parent Effect; each text element owns its four-times glyph cache.
            card = new Border { Margin = new Thickness(32), Padding = new Thickness(1) };
            Grid body = new Grid { Margin = new Thickness(17, 0, 15, 0) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(13) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(211) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
            badge = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16),
                Background = Brush("#15976B"), VerticalAlignment = VerticalAlignment.Center };
            symbol = new SampledText(textPixels) { Text = "!", FontFamily = EmbeddedFonts.Family,
                FontSize = 19, FontWeight = EmbeddedFonts.TitleWeight, Foreground = Brush("#DDF6EB"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -1, 0, 0), Visibility = Visibility.Hidden };
            checkMark = new System.Windows.Shapes.Path { Data = CheckGeometry, Width = 18, Height = 18,
                Stroke = Brush("#DDF6EB"), StrokeThickness = 2, Stretch = Stretch.None,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = false };
            Grid badgeContent = new Grid();
            badgeContent.Children.Add(checkMark); badgeContent.Children.Add(symbol);
            badge.Child = badgeContent; body.Children.Add(badge);
            StackPanel copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            title = new SampledText(textPixels) { FontSize = 14, FontWeight = EmbeddedFonts.TitleWeight,
                Foreground = Brush("#1D1D1F"), TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap };
            detail = new SampledText(textPixels) { FontSize = 12, FontWeight = EmbeddedFonts.BodyWeight, Foreground = Brush("#6E6E73"),
                Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap };
            copy.Children.Add(title); copy.Children.Add(detail);
            Grid.SetColumn(copy, 2); body.Children.Add(copy);
            counter = new Border { CornerRadius = new CornerRadius(9), Background = Brush("#E9E9ED"),
                Padding = new Thickness(4, 5, 4, 5), Margin = new Thickness(8, 0, 0, 0),
                Width = 48, Height = 28, HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Hidden };
            FontFamily counterFont = new FontFamily("Bahnschrift, Segoe UI");
            times = new SampledText(textPixels) { FontFamily = counterFont, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1D1D1F") };
            times.HorizontalAlignment = HorizontalAlignment.Left;
            times.VerticalAlignment = VerticalAlignment.Center;
            overflow = new SampledText(textPixels) { Text = "+", FontFamily = counterFont, FontSize = 8, FontWeight = FontWeights.SemiBold,
                Foreground = times.Foreground, HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Hidden };
            glyphs = new SampledText[] { title, detail, times, overflow, symbol };
            // Hinting is specified on the text itself, including detached exports;
            // do not rely on a window-level setting reaching every text visual.
            foreach (SampledText text in glyphs)
            {
                TextOptions.SetTextFormattingMode(text, TextFormattingMode.Display);
                TextOptions.SetTextRenderingMode(text, TextRenderingMode.Grayscale);
                TextOptions.SetTextHintingMode(text, TextHintingMode.Fixed);
            }
            // Natural counter widths keep the whole sampled glyph group centered.
            TextOptions.SetTextFormattingMode(times, TextFormattingMode.Ideal);
            TextOptions.SetTextFormattingMode(overflow, TextFormattingMode.Ideal);
            counterContent = new Grid { Height = 18, HorizontalAlignment = HorizontalAlignment.Center };
            counterContent.Children.Add(times); counterContent.Children.Add(overflow);
            counter.RenderTransform = countScale; counter.RenderTransformOrigin = new Point(.5, .5);
            counter.Child = counterContent; Grid.SetColumn(counter, 3); body.Children.Add(counter);
            card.Child = body; surface.Children.Add(card); Content = surface;
            SourceInitialized += delegate
            {
                hwnd = new WindowInteropHelper(this).Handle;
                Native.MakePassive(hwnd);
                TryMaterial();
                HwndSource.FromHwnd(hwnd).AddHook(delegate(IntPtr window, int message, IntPtr w, IntPtr l, ref bool handled)
                {
                    if (message == 0x21) { handled = true; return new IntPtr(3); }
                    if (message == 0x84) { handled = true; return new IntPtr(-1); }
                    if (message == 0x02E0 || message == 0x007E || message == 0x001A) QueueDpiRefresh();
                    // WPF must process WM_DPICHANGED itself; do not mark it handled.
                    return IntPtr.Zero;
                });
            };
            Closed += delegate
            {
                closed = true; pendingEntrance = pendingHit = deferredPulse = false;
                revision++; hideTimer.Stop(); motion.Stop(); windowFade.Reset(0); StopFrames();
                foreach (SampledText text in glyphs) text.ClearCache();
                textPixels.Release();
                if (acrylic != null) acrylic.Dispose();
            };
            hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            hideTimer.Tick += delegate { FadeOut(); };
        }

        private void SetContent(Feedback value, bool preview)
        {
            Current = value;
            currentPreview = preview;
            detail.Text = preview || value.Kind == FeedbackKind.Status ? value.Preview : value.TypeLabel;
            counter.Visibility = value.Count >= 2 ? Visibility.Visible : Visibility.Hidden;
            times.Text = "×" + Math.Min(Math.Max(value.Count, 0), 99);
            overflow.Visibility = value.Count > 99 ? Visibility.Visible : Visibility.Hidden;
            LayoutCounter();
            bool warning = value.Kind == FeedbackKind.Unconfirmed;
            bool neutral = value.Kind == FeedbackKind.Updated || value.Kind == FeedbackKind.Retained;
            Appearance theme = Themes.Current;
            title.Foreground = Brush(theme.Text); detail.Foreground = Brush(theme.Detail);
            counter.Background = Brush(theme.Counter); times.Foreground = overflow.Foreground = Brush(theme.CountText);
            checkMark.Visibility = warning || neutral ? Visibility.Hidden : Visibility.Visible;
            symbol.Visibility = warning || neutral ? Visibility.Visible : Visibility.Hidden;
            symbol.Text = warning ? "!" : "·";
            checkMark.Stroke = Brush(theme.Green);
            symbol.Foreground = Brush(warning ? (theme.Dark ? "#FFD383" : "#AE7E25") : neutral ? (theme.Dark ? "#AECBBC" : "#548274") : theme.Green);
            badge.Background = Brush(warning ? (theme.Dark ? "#554025" : "#FFF0D1") : neutral ? (theme.Dark ? "#2B4237" : "#E7F1EE") : theme.Badge);
        }

        private void UpdateTitle(string text, bool animate)
        {
            titleTransition.Request(text, Native.Now, animate);
            ApplyTitleFrame();
            if (titleTransition.Running) StartFrames();
        }

        private void ApplyTitleFrame()
        {
            if (title.Text != titleTransition.Text) title.Text = titleTransition.Text;
            title.Opacity = titleTransition.Opacity;
        }

        private void TryMaterial()
        {
            if (hwnd == IntPtr.Zero || acrylic != null || !Themes.MaterialEnabled || !Themes.Current.Transparent) return;
            try
            {
#if PRESENTATION_CHECKS
                TestMaterialAttempts++;
                if (TestFailMaterialCreation) throw new InvalidOperationException("Injected material creation failure.");
#endif
                acrylic = new AcrylicHost(hwnd); Themes.MaterialAvailable = true; MaterialFailure = null;
            }
            catch (Exception error) { Themes.MaterialAvailable = false; MaterialFailure = error.GetType().Name + " (0x" + error.HResult.ToString("X8") + ")"; }
        }

        private void ReleaseMaterial()
        {
            AcrylicHost old = acrylic; acrylic = null; Themes.MaterialAvailable = false;
            if (old == null) return;
            try { old.Dispose(); }
            catch (Exception error) { MaterialFailure = error.GetType().Name + " (0x" + error.HResult.ToString("X8") + ")"; }
        }

        internal void SetMaterialEnabled(bool enabled)
        {
            if (closed) return;
            HideAndClear();
            Themes.MaterialEnabled = enabled;
            Themes.Refresh();
            MaterialFailure = null;
            if (!enabled || !Themes.Current.Transparent) ReleaseMaterial();
            else if (hwnd != IntPtr.Zero) TryMaterial();
            if (hwnd != IntPtr.Zero) Position(anchorWindow);
        }

        private void MaterialFault(Exception error)
        {
#if TESTS
            Console.WriteLine("MATERIAL_FAULT=" + error);
#endif
            Themes.MaterialAvailable = false;
            MaterialFailure = error.GetType().Name + " (0x" + error.HResult.ToString("X8") + ")";
            if (acrylic != null) { try { acrylic.Dispose(); } catch (Exception) { } acrylic = null; }
        }

        internal void RefreshAppearance()
        {
            if (closed) return;
            if (!Themes.MaterialEnabled || !Themes.Current.Transparent) ReleaseMaterial();
            else if (hwnd != IntPtr.Zero) TryMaterial();
            if (Current != null) SetContent(Current, currentPreview);
            if (hwnd != IntPtr.Zero) Position(anchorWindow);
            if (IsVisible && Current != null) PrepareVisibleText();
        }

        internal void CopyPressed(int count, IntPtr foreground, bool preview)
        { CopyPressed(count, foreground, preview, Native.Now); }

        internal void CopyPressed(int count, IntPtr foreground, bool preview, long pressedAt)
        {
            if (closed) return;
            lastCopySignal = Native.Now;
            lastCopyQueueMilliseconds = Math.Max(0, lastCopySignal - pressedAt);
            // Keep the last actual result steady while awaiting the new one.
            // A hidden card first appears with the result, without a blank or
            // provisional message. First appearance is fade-only; later key
            // presses may pulse the already present copy feedback.
            bool repeat = count >= 2 && IsVisible && Current != null && Current.Kind != FeedbackKind.Status;
            deferredPulse = repeat;
            if (!repeat) pendingHit = false;
            if (!IsVisible || pendingEntrance || Current == null) return;
            deferredPulse = false;
            revision++;
            // Signal title motion before content layout, positioning, or any
            // asynchronous clipboard read. Do not choose an unverified title.
            if (titleTransition.Target != FeedbackEngine.SuccessTitle(count))
            {
                titleTransition.Prepare(lastCopySignal);
                ApplyTitleFrame(); StartFrames();
            }
            Current.Count = count;
            SetContent(Current, preview);
            Position(foreground);
            PrepareVisibleText();
            hideTimer.Stop();
            if (repeat) PlayHit(); else RestoreFade();
            hideTimer.Start();
        }

        private void PlayHit()
        {
            // The first subsequent rendering callback owns the pulse epoch.
            // Preparing glyphs must not eat into the 55 ms grow stage.
            pendingHit = true; StartFrames();
        }

        private void PrepareVisibleText()
        {
            Stopwatch watch = Stopwatch.StartNew();
            preparing = true;
            try
            {
                surface.Measure(new Size(OuterWidth, OuterHeight));
                surface.Arrange(new Rect(0, 0, OuterWidth, OuterHeight));
                surface.UpdateLayout();
                foreach (SampledText text in glyphs)
                {
                    if (text.Visibility != Visibility.Visible) continue;
                    if ((text == times || text == overflow) && counter.Visibility != Visibility.Visible) continue;
                    text.Moving = pendingHit || motion.Running;
                    text.PrepareForRender();
                }
                preparedRevision = revision;
            }
            finally { preparing = false; watch.Stop(); LastPreparationMilliseconds = watch.Elapsed.TotalMilliseconds; }
        }

        private void RestoreFade()
        {
            if (windowFade.FadingOut || (!windowFade.Running && Opacity < 1))
                windowFade.BeginIn(Opacity, Native.Now);
        }

        private void LayoutCounter()
        {
            // Center the entire visible group, including the raised plus, rather
            // than centering a reserved box with left-aligned characters inside it.
            Size available = new Size(Double.PositiveInfinity, Double.PositiveInfinity);
            times.Measure(available);
            double width = times.DesiredSize.Width;
            if (overflow.Visibility == Visibility.Visible)
            {
                overflow.Margin = new Thickness(0);
                overflow.Measure(available);
                double plusWidth = overflow.DesiredSize.Width;
                overflow.Margin = new Thickness(width + .5, -2, 0, 0);
                width += .5 + plusWidth;
            }
            else overflow.Margin = new Thickness(0, -2, 0, 0);
            counterContent.Width = width;
        }

        internal void Present(Feedback value, bool preview)
        {
            if (value == null || closed) return;
            bool playHit = value.Kind != FeedbackKind.Status && value.Count >= 2 && (pendingHit || deferredPulse);
            if (!playHit) pendingHit = false;
            deferredPulse = false;
            revision++;
            hideTimer.Stop();
            bool alreadyVisible = IsVisible && !pendingEntrance;
            if (!alreadyVisible) { Opacity = 0; windowFade.Reset(0); }
            SetContent(value, preview);
            UpdateTitle(value.Title, alreadyVisible);
            // Position and build for the real target DPI while the new card is hidden.
            Position(value.Foreground);
            if (!alreadyVisible)
            {
                pendingEntrance = true;
                if (HasNativeMaterial) acrylic.SetMotion(cardScale.ScaleX, 0);
                if (!IsVisible) Show();
                // Ensure WPF's source/visual DPI is fully committed after first Show.
                Position(value.Foreground);
            }
            if (playHit) PlayHit();
            PrepareVisibleText();
            if (alreadyVisible) { RestoreFade(); hideTimer.Start(); }
            StartFrames();
        }

        private int EffectiveDpi()
        {
            int dpi = hwnd == IntPtr.Zero ? 0 : Native.WindowDpi(hwnd);
            if (dpi <= 0) dpi = (int)Math.Round(VisualTreeHelper.GetDpi(this).DpiScaleX * 96);
            return dpi > 0 ? dpi : 96;
        }

        private void ApplyBackground(int dpi, double visualScale)
        {
            LayoutCounter();
            BackgroundFrame frame = backgrounds.Get(dpi);
            background.Source = frame.Bitmap;
            // WPF device scaling * this logical extent equals exact bitmap pixels.
            // This avoids an extra image resample even at fractional display scales.
            background.Width = frame.Width / visualScale;
            background.Height = frame.Height / visualScale;
            CurrentBackground = frame;
        }

        internal void Position(IntPtr foreground)
        {
            if (closed || positioning) return;
            positioning = true;
            anchorWindow = foreground;
            try
            {
                IntPtr handle = Handle;
                IntPtr monitor = Native.MonitorFromWindow(foreground, 2);
                Native.MonitorInfo info = new Native.MonitorInfo { Size = (uint)Marshal.SizeOf(typeof(Native.MonitorInfo)) };
                if (!Native.GetMonitorInfo(monitor, ref info)) return;
                if (Native.MonitorFromWindow(handle, 2) != monitor)
                    Native.SetWindowPos(handle, IntPtr.Zero, info.Work.Left + 4, info.Work.Top + 4, 0, 0,
                        0x0001 | 0x0004 | 0x0010 | 0x0200);
                int dpi = EffectiveDpi();
                double scale = dpi / 96.0;
                int width = (int)Math.Round(OuterWidth * scale), height = (int)Math.Round(OuterHeight * scale);
                int x = info.Work.Left + (int)Math.Round(-12 * scale);
                int y = info.Work.Bottom - height + (int)Math.Round(12 * scale);
                if (acrylic != null)
                {
                    try
                    {
                        acrylic.Position(x, y, width, height, dpi);
                        if (HasNativeMaterial && IsVisible) acrylic.Show(); else acrylic.Hide();
                    }
                    catch (Exception error) { MaterialFault(error); }
                }
                Native.Rect currentBounds;
                if (!Native.GetWindowRect(handle, out currentBounds) || currentBounds.Left != x || currentBounds.Top != y ||
                    currentBounds.Right - currentBounds.Left != width || currentBounds.Bottom - currentBounds.Top != height)
                    Native.SetWindowPos(handle, IntPtr.Zero, x, y, width, height, 0x0010 | 0x0200 | 0x0004);
                dpi = EffectiveDpi();
                ApplyBackground(dpi, VisualTreeHelper.GetDpi(this).DpiScaleX);
            }
            finally { positioning = false; }
        }

        private void QueueDpiRefresh()
        {
            if (closed || refreshQueued) return;
            refreshQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
            {
                refreshQueued = false;
                if (!closed && hwnd != IntPtr.Zero) Position(anchorWindow);
            }));
        }

        private void FadeOut()
        {
            hideTimer.Stop();
            windowFade.BeginOut(Opacity, Native.Now); StartFrames();
        }

        private void StartFrames()
        {
            if (!rendering) { CompositionTarget.Rendering += DrawFrame; rendering = true; }
            RenderOptions.SetBitmapScalingMode(background, BitmapScalingMode.HighQuality);
        }
        private void StopFrames()
        {
            if (rendering) { CompositionTarget.Rendering -= DrawFrame; rendering = false; }
            RenderOptions.SetBitmapScalingMode(background, BitmapScalingMode.NearestNeighbor);
        }
        private void DrawFrame(object sender, EventArgs args)
        {
            if (closed || preparing) return;
            if (!IsVisible || Current == null) { StopFrames(); return; }
            bool started = pendingEntrance || pendingHit;
            if (started || preparedRevision != revision)
            {
                // Recheck layout in case a queued DPI/layout notification ran
                // after Present. Cache hits perform no glyph rasterization.
                PrepareVisibleText();
            }
            long now = Native.Now;
            if (pendingEntrance)
            {
                pendingEntrance = false;
                windowFade.BeginIn(Opacity, now); hideTimer.Start();
            }
            else if (pendingHit && windowFade.FadingOut) windowFade.BeginIn(Opacity, now);
            if (pendingHit) { pendingHit = false; motion.Pulse(now); }
#if PRESENTATION_CHECKS
            if (TestFrameObserved != null) TestFrameObserved(now, started && preparedRevision == revision);
#endif
            double value = motion.Value(now), whole = 1 + CopyMotion.CardAmount * value;
            titleTransition.Advance(now); ApplyTitleFrame();
            cardScale.ScaleX = cardScale.ScaleY = whole;
            countScale.ScaleX = countScale.ScaleY = (1 + CopyMotion.CounterAmount * value) / whole;
            if (windowFade.Running)
            {
                windowFade.Advance(now); Opacity = windowFade.Opacity;
                if (!windowFade.Running && windowFade.FadingOut) { HideAndClear(); return; }
            }
            if (acrylic != null)
            {
                try { acrylic.SetMotion(whole, Opacity); }
                catch (Exception error) { MaterialFault(error); ApplyBackground(EffectiveDpi(), VisualTreeHelper.GetDpi(this).DpiScaleX); }
            }
            foreach (SampledText text in glyphs)
                text.Moving = motion.Running;
            if (!motion.Running && !windowFade.Running && !titleTransition.Running && !pendingHit && !pendingEntrance) StopFrames();
        }

        internal void HideAndClear()
        {
            deferredPulse = pendingEntrance = pendingHit = false;
            revision++;
            hideTimer.Stop();
            windowFade.Reset(0); motion.Stop(); StopFrames();
            titleTransition.Reset(""); ApplyTitleFrame();
            cardScale.ScaleX = cardScale.ScaleY = countScale.ScaleX = countScale.ScaleY = 1;
            if (acrylic != null) acrylic.Hide();
            Hide(); Opacity = 0;
            Current = null;
            title.Text = detail.Text = times.Text = "";
            textPixels.Idle();
            long retainedPixels = textPixels.RetainedBytes;
            foreach (SampledText text in glyphs) retainedPixels += text.SurfacePixelBytes;
            bool reuseEmptySurfaces = retainedPixels <= 4 * 1024 * 1024;
            foreach (SampledText text in glyphs)
            {
                text.Moving = false;
                if (reuseEmptySurfaces) text.ParkForIdle(textPixels.FinalPixels);
                else text.ClearCache();
            }
            if (!reuseEmptySurfaces) textPixels.Release();
        }

        internal void RenderSample(string path)
        {
            RenderSample(path, new Feedback { Kind = FeedbackKind.Confirmed, Title = "去粘贴吧",
                Preview = "这次真的复制好了，可以放心粘贴。", Count = 12 }, true);
        }

        internal void RenderSample(string path, Feedback sample, bool preview)
        { RenderSample(path, sample, preview, 96); }

        internal void RenderSample(string path, Feedback sample, bool preview, int dpi)
        {
            // Export a detached copy, never change DPI on the live HWND visual tree.
            ToastWindow exporter = new ToastWindow();
            try
            {
                double scale = dpi / 96.0;
                VisualTreeHelper.SetRootDpi(exporter.surface, new DpiScale(scale, scale));
                exporter.SetContent(sample, preview);
                exporter.UpdateTitle(sample.Title, false);
                exporter.ApplyBackground(dpi, scale);
                exporter.surface.Measure(new Size(OuterWidth, OuterHeight));
                exporter.surface.Arrange(new Rect(0, 0, OuterWidth, OuterHeight));
                exporter.surface.UpdateLayout();
                BackgroundFrame frame = exporter.CurrentBackground;
                RenderTargetBitmap bitmap = new RenderTargetBitmap(frame.Width, frame.Height, dpi, dpi, PixelFormats.Pbgra32);
                bitmap.Render(exporter.surface);
                PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (FileStream stream = File.Create(path)) encoder.Save(stream);
            }
            finally { exporter.Close(); }
        }

#if TESTS
        internal bool TestRendering { get { return rendering; } }
        internal double TestScale { get { return cardScale.ScaleX; } }
        internal IntPtr TestMaterialHandle { get { return acrylic == null ? IntPtr.Zero : acrylic.Handle; } }
        internal bool TestMaterialVisible { get { return acrylic != null && acrylic.Visible; } }
        internal bool TestTitleAwaitingResult { get { return titleTransition.AwaitingResult; } }
        internal long TestCopySignal { get { return lastCopySignal; } }
        internal double TestCopyQueueMilliseconds { get { return lastCopyQueueMilliseconds; } }
        internal sealed class LayoutSnapshot
        {
            internal Rect Title, Detail, Counter, Group, Number, Plus;
            internal string NumberText;
            internal bool HasOverflow;
            internal double NumberFont, PlusFont;
            internal string CounterFont, PlusFamily;
            internal double TitleFont, DetailFont;
            internal TextRenderingMode RenderingMode;
            internal TextHintingMode HintingMode;
        }
        internal bool TestFontBinding()
        {
            return title.FontFamily == EmbeddedFonts.Family && detail.FontFamily == EmbeddedFonts.Family &&
                symbol.FontFamily == EmbeddedFonts.Family && title.FontWeight.ToOpenTypeWeight() == 650 &&
                symbol.FontWeight.ToOpenTypeWeight() == 650 && detail.FontWeight.ToOpenTypeWeight() == 500 &&
                title.FontSize == 14 && detail.FontSize == 12 && times.FontSize == 12 && overflow.FontSize == 8;
        }
        internal LayoutSnapshot TestLayout(int count, int dpi)
        {
            double scale = dpi / 96.0;
            VisualTreeHelper.SetRootDpi(surface, new DpiScale(scale, scale));
            SetContent(new Feedback { Kind = FeedbackKind.Confirmed, Title = FeedbackEngine.SuccessTitle(count),
                Preview = "保持固定的预览文字", Count = count }, true);
            UpdateTitle(Current.Title, false);
            surface.Measure(new Size(OuterWidth, OuterHeight));
            surface.Arrange(new Rect(0, 0, OuterWidth, OuterHeight)); surface.UpdateLayout();
            return new LayoutSnapshot { Title = TestBounds(title), Detail = TestBounds(detail),
                Counter = TestBounds(counter), Group = TestBounds(counterContent), Number = TestBounds(times), Plus = TestBounds(overflow),
                NumberText = times.Text, HasOverflow = overflow.Visibility == Visibility.Visible,
                NumberFont = times.FontSize, PlusFont = overflow.FontSize,
                CounterFont = times.FontFamily.Source, PlusFamily = overflow.FontFamily.Source,
                TitleFont = title.FontSize, DetailFont = detail.FontSize,
                RenderingMode = TextOptions.GetTextRenderingMode(title), HintingMode = TextOptions.GetTextHintingMode(title) };
        }
        internal bool TestCheck(FeedbackKind kind)
        {
            SetContent(new Feedback { Kind = kind, Title = "已复制", Preview = "test", Count = 6 }, true);
            bool success = kind != FeedbackKind.Unconfirmed && kind != FeedbackKind.Updated && kind != FeedbackKind.Retained;
            return checkMark.Data.IsFrozen && checkMark.Width == 18 && checkMark.Height == 18 &&
                checkMark.StrokeThickness == 2 && checkMark.StrokeStartLineCap == PenLineCap.Round &&
                checkMark.StrokeEndLineCap == PenLineCap.Round && checkMark.StrokeLineJoin == PenLineJoin.Round &&
                checkMark.Visibility == (success ? Visibility.Visible : Visibility.Hidden) &&
                symbol.Visibility == (success ? Visibility.Hidden : Visibility.Visible) &&
                ((SolidColorBrush)checkMark.Stroke).Color == (Color)ColorConverter.ConvertFromString(Themes.Current.Green);
        }
        internal bool TestTitleReset()
        {
            UpdateTitle("old", false); UpdateTitle("new", true);
            bool started = rendering && titleTransition.Running;
            HideAndClear();
            return started && !titleTransition.Running && title.Text == "" && title.Opacity == 1 && !rendering;
        }
        private Rect TestBounds(FrameworkElement element)
        { return element.TransformToAncestor(surface).TransformBounds(new Rect(new Point(), element.RenderSize)); }
#endif

    }
}

