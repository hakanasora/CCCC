using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace CCCC
{
    internal enum PrankPaymentState { Main, Help, Confirmation, Closed }
    internal enum PrankCloseReason { None, Confirmed, Cleanup }

    internal static class PrankTheme
    {
        internal static readonly Brush Purple = Brush("#42305D"), Line = Brush("#D8CAE5"),
            Cream = Brush("#FFFBF4"), Lavender = Brush("#F3EFF8");
        internal static Brush Brush(string color)
        {
            SolidColorBrush brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze(); return brush;
        }
        internal static TextBlock Text(string text, double size, Brush color)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = color,
                VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        }
        internal static Style ButtonStyle(Type type, bool payment)
        {
            Style style = new Style(type);
            style.Setters.Add(new Setter(Control.BackgroundProperty, Cream));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Purple));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Line));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1.5)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(18, 12, 18, 12)));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 21.0));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            ControlTemplate template = new ControlTemplate(type);
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter); template.VisualTree = border;
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            Trigger hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, .86)); style.Triggers.Add(hover);
            Trigger pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(UIElement.OpacityProperty, .65)); style.Triggers.Add(pressed);
            Trigger focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(Control.BorderBrushProperty, Brush("#2463D5")));
            focus.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(3))); style.Triggers.Add(focus);
            if (payment)
            {
                Trigger selected = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
                selected.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(3)));
                selected.Setters.Add(new Setter(Control.BackgroundProperty, Brush("#E4F5E9"))); style.Triggers.Add(selected);
            }
            Trigger disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .6)); style.Triggers.Add(disabled);
            return style;
        }
        internal static Button Button(string name, string text, bool primary)
        {
            Button button = new Button { Name = name, Content = text, Style = ButtonStyle(typeof(Button), false),
                MinHeight = 52, Foreground = primary ? Brushes.White : Purple,
                Background = primary ? Brush("#7352A4") : Cream };
            AutomationProperties.SetName(button, text);
            return button;
        }
        internal static void Configure(Window window, string title)
        {
            window.Title = title;
            window.WindowStyle = WindowStyle.None; window.ResizeMode = ResizeMode.NoResize;
            window.AllowsTransparency = true; window.Background = Brushes.Transparent;
            window.ShowInTaskbar = false; window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.UseLayoutRounding = true; window.SnapsToDevicePixels = true;
            // New interactive controls use the installed Windows UI fonts. The
            // original toast's separately licensed embedded fonts are untouched.
            window.FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
            TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
        }
        internal static Border Frame(UIElement content)
        {
            return new Border { Background = Cream, BorderBrush = Line, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(15), Margin = new Thickness(12), Child = content,
                Effect = new DropShadowEffect { Color = Colors.Black, Opacity = .23, BlurRadius = 16, ShadowDepth = 3 } };
        }
    }

    internal sealed class PrankPaymentWindow : Window, IPrankPaymentView
    {
        private readonly Grid interactive;
        private readonly Border help, hint;
        private readonly TextBlock helpError;
        private readonly Button launchButton;
        private readonly RadioButton[] methods = new RadioButton[3];
        private readonly DispatcherTimer hintTimer;
        private readonly Func<string, bool> openUrl;
        private PrankPlacementSubscription placement;
        private IntPtr monitor;
        private FreeloadConfirmWindow confirmation;
        private PrankCloseReason closeReason;
        private bool launching, placing;
        internal PrankPaymentState State { get; private set; }
        internal int SelectedPayment { get; private set; }
        internal FreeloadConfirmWindow Confirmation { get { return confirmation; } }
        internal bool HintVisible { get { return hint.Visibility == Visibility.Visible; } }
        internal string HelpError { get { return helpError.Text; } }

        internal PrankPaymentWindow() : this(OpenRepository) { }
        internal PrankPaymentWindow(Func<string, bool> openUrl)
        {
            this.openUrl = openUrl;
            PrankTheme.Configure(this, "付款之后立即复制！");
            ShowActivated = false;
            State = PrankPaymentState.Main;
            Width = 960; Height = 620;
            Grid page = new Grid { Width = 936, Height = 596 };
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(76) });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(90) });
            interactive = page;

            Grid title = new Grid { Margin = new Thickness(28, 0, 18, 0) };
            title.ColumnDefinitions.Add(new ColumnDefinition());
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            TextBlock heading = PrankTheme.Text("付款之后立即复制！", 27, Brushes.White);
            heading.FontWeight = FontWeights.SemiBold; title.Children.Add(heading);
            Button decoration = PrankTheme.Button("DecorativeClose", "×", false);
            decoration.FontSize = 33; decoration.Padding = new Thickness(0); decoration.MinHeight = 42;
            decoration.Background = Brushes.Transparent; decoration.BorderThickness = new Thickness(0);
            decoration.Foreground = Brushes.White;
            AutomationProperties.SetName(decoration, "装饰关闭按钮");
            decoration.Click += delegate { ShowDecorativeHint(); };
            Grid.SetColumn(decoration, 1); title.Children.Add(decoration);
            Border titleBar = new Border { Background = PrankTheme.Purple, CornerRadius = new CornerRadius(14, 14, 0, 0), Child = title };
            page.Children.Add(titleBar);

            Grid middle = new Grid(); Grid.SetRow(middle, 1); page.Children.Add(middle);
            middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34, GridUnitType.Star) });
            middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66, GridUnitType.Star) });
            StackPanel choices = new StackPanel { Margin = new Thickness(28, 30, 28, 20) };
            TextBlock choose = PrankTheme.Text("选择付款方式", 24, PrankTheme.Purple);
            choose.FontWeight = FontWeights.SemiBold; choose.Margin = new Thickness(0, 0, 0, 24); choices.Children.Add(choose);
            string[] labels = { "WeChat", "PayPal", "支付宝" }, colors = { "#078640", "#003087", "#1677FF" };
            for (int i = 0; i < methods.Length; i++)
            {
                int index = i;
                Grid row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                row.Children.Add(PrankBrandMarks.Create(i));
                TextBlock label = PrankTheme.Text(labels[i], 23, PrankTheme.Brush(colors[i]));
                label.Margin = new Thickness(12, 0, 0, 0); label.FontWeight = FontWeights.SemiBold;
                Grid.SetColumn(label, 1); row.Children.Add(label);
                TextBlock selected = PrankTheme.Text("✓", 21, PrankTheme.Purple);
                selected.Visibility = Visibility.Hidden; Grid.SetColumn(selected, 2); row.Children.Add(selected);
                RadioButton method = new RadioButton { Name = "Payment" + i, GroupName = "PaymentMethods", Content = row,
                    Style = PrankTheme.ButtonStyle(typeof(RadioButton), true), Height = 76, Margin = new Thickness(0, 0, 0, 18),
                    Padding = new Thickness(14, 10, 14, 10), HorizontalContentAlignment = HorizontalAlignment.Stretch };
                // Keep each row stretched inside the centered ContentPresenter.
                row.Width = 218;
                AutomationProperties.SetName(method, labels[i]);
                method.Checked += delegate { SelectedPayment = index; selected.Visibility = Visibility.Visible; };
                method.Unchecked += delegate { selected.Visibility = Visibility.Hidden; };
                methods[i] = method; choices.Children.Add(method);
            }
            methods[0].IsChecked = true;
            Border left = new Border { Background = PrankTheme.Lavender, BorderBrush = PrankTheme.Line,
                BorderThickness = new Thickness(0, 0, 1, 0), Child = choices };
            middle.Children.Add(left);

            Grid right = new Grid(); Grid.SetColumn(right, 1); middle.Children.Add(right);
            StackPanel checkout = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            StackPanel prices = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12) };
            TextBlock currency = PrankTheme.Text("CNY", 25, PrankTheme.Purple); currency.Margin = new Thickness(0, 0, 17, 0);
            TextBlock original = PrankTheme.Text("9.9", 29, PrankTheme.Brush("#8C8593"));
            original.TextDecorations = TextDecorations.Strikethrough; original.Margin = new Thickness(0, 0, 19, 0);
            TextBlock discount = PrankTheme.Text("3.9", 53, PrankTheme.Brush("#E5684E")); discount.FontWeight = FontWeights.Bold;
            prices.Children.Add(currency); prices.Children.Add(original); prices.Children.Add(discount); checkout.Children.Add(prices);
            checkout.Children.Add(new PrankQrCode { Width = 270, Height = 270, HorizontalAlignment = HorizontalAlignment.Center });
            TextBlock scan = PrankTheme.Text("请扫码支付", 23, PrankTheme.Purple);
            scan.HorizontalAlignment = HorizontalAlignment.Center; scan.Margin = new Thickness(0, 8, 0, 0); checkout.Children.Add(scan);
            right.Children.Add(checkout);

            StackPanel helpContent = new StackPanel();
            helpContent.Children.Add(PrankTheme.Text("扫码不便？也可以前往收银台直接结算。", 21, PrankTheme.Purple));
            helpError = PrankTheme.Text("", 16, PrankTheme.Brush("#A23528")); helpError.Margin = new Thickness(0, 8, 0, 0);
            helpError.Visibility = Visibility.Collapsed; helpContent.Children.Add(helpError);
            StackPanel helpActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            launchButton = PrankTheme.Button("OpenRepository", "前往收银台", true);
            launchButton.Click += delegate { LaunchRepository(); };
            Button back = PrankTheme.Button("CloseHelp", "返回", false); back.Margin = new Thickness(12, 0, 0, 0);
            back.Click += delegate { ToggleHelp(); };
            helpActions.Children.Add(launchButton); helpActions.Children.Add(back); helpContent.Children.Add(helpActions);
            help = new Border { Background = PrankTheme.Cream, BorderBrush = PrankTheme.Line, BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(12), Padding = new Thickness(22), Margin = new Thickness(26),
                VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed, Child = helpContent };
            right.Children.Add(help);

            Grid footer = new Grid { Margin = new Thickness(28, 14, 28, 14) };
            footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition());
            Button helpButton = PrankTheme.Button("ToggleHelp", "无法扫码？点击这里", false);
            helpButton.HorizontalAlignment = HorizontalAlignment.Left; helpButton.Click += delegate { ToggleHelp(); }; footer.Children.Add(helpButton);
            Button freeload = PrankTheme.Button("Freeload", "我要白嫖", true);
            freeload.MinWidth = 190; freeload.HorizontalAlignment = HorizontalAlignment.Right;
            freeload.Click += delegate { RequestFreeload(); }; Grid.SetColumn(freeload, 1); footer.Children.Add(freeload);
            Border footerBorder = new Border { BorderBrush = PrankTheme.Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = footer };
            Grid.SetRow(footerBorder, 2); page.Children.Add(footerBorder);

            TextBlock hintText = PrankTheme.Text("哈哈,这只是个装饰哦", 21, Brushes.White);
            hint = new Border { Background = PrankTheme.Purple, CornerRadius = new CornerRadius(10), Padding = new Thickness(22, 12, 22, 12),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 90, 0, 0), Visibility = Visibility.Collapsed, IsHitTestVisible = false, Child = hintText };
            Grid.SetRowSpan(hint, 3); Panel.SetZIndex(hint, 10); page.Children.Add(hint);
            Content = new Viewbox { Stretch = Stretch.Uniform, Child = PrankTheme.Frame(page) };
            hintTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(2) };
            hintTimer.Tick += HideHint;
            Closed += MainClosed;
            LocationChanged += MainMoved; SizeChanged += MainMoved;
        }

        public void Present(IntPtr foreground)
        {
            if (State == PrankPaymentState.Closed) return;
            monitor = PrankPlacement.SelectMonitor(foreground);
            new WindowInteropHelper(this).EnsureHandle();
            placement = new PrankPlacementSubscription(this, Reposition);
            Reposition(); Show(); Reposition();
        }
        private void Reposition()
        {
            if (placing || State == PrankPaymentState.Closed) return;
            placing = true;
            try { PrankPlacement.Apply(this, PrankPlacement.MainBounds(PrankPlacement.WorkArea(monitor))); }
            finally { placing = false; }
            if (confirmation != null) confirmation.Recenter();
        }
        private void MainMoved(object sender, EventArgs args)
        { if (!placing && confirmation != null) confirmation.Recenter(); }
        internal void SelectPayment(int index)
        {
            if (State != PrankPaymentState.Confirmation && State != PrankPaymentState.Closed && index >= 0 && index < methods.Length)
                methods[index].IsChecked = true;
        }
        internal void ToggleHelp()
        {
            if (State == PrankPaymentState.Confirmation || State == PrankPaymentState.Closed || launching) return;
            State = State == PrankPaymentState.Help ? PrankPaymentState.Main : PrankPaymentState.Help;
            help.Visibility = State == PrankPaymentState.Help ? Visibility.Visible : Visibility.Collapsed;
        }
        private static bool OpenRepository(string url)
        {
            // Shell/DDE can reuse an existing browser and return no new process.
            // A null Process is not a launch failure; shell exceptions are.
            using (Process process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })) { }
            return true;
        }
        internal void LaunchRepository()
        {
            if (State != PrankPaymentState.Help || launching) return;
            launching = true; launchButton.IsEnabled = false;
            try
            {
                if (!openUrl(PrankPaymentController.RepositoryUrl)) throw new Win32Exception();
                if (State == PrankPaymentState.Help) { State = PrankPaymentState.Main; help.Visibility = Visibility.Collapsed; }
                helpError.Text = ""; helpError.Visibility = Visibility.Collapsed;
            }
            catch (Exception error)
            {
                if (!(error is Win32Exception || error is InvalidOperationException || error is SecurityException || error is System.IO.FileNotFoundException)) throw;
                helpError.Text = "无法打开浏览器，请稍后重试。\n" + PrankPaymentController.RepositoryUrl;
                helpError.Visibility = Visibility.Visible;
                Trace.TraceWarning("CCCC repository launch: {0}", error.GetType().Name);
            }
            finally { launching = false; launchButton.IsEnabled = true; }
        }
        internal void ShowDecorativeHint()
        {
            if (State == PrankPaymentState.Confirmation || State == PrankPaymentState.Closed) return;
            hintTimer.Stop(); hint.Visibility = Visibility.Visible; hintTimer.Start();
        }
        private void HideHint(object sender, EventArgs args) { hintTimer.Stop(); hint.Visibility = Visibility.Collapsed; }
        internal void RequestFreeload()
        {
            if (State == PrankPaymentState.Closed || State == PrankPaymentState.Confirmation) return;
            State = PrankPaymentState.Confirmation;
            help.Visibility = Visibility.Collapsed; HideHint(null, EventArgs.Empty);
            confirmation = new FreeloadConfirmWindow(this);
            confirmation.Closed += ConfirmClosed;
            interactive.IsEnabled = false;
            // A modeless owned HWND leaves clipboard/tray dispatch running.
            // Disable only parent controls, so global shortcuts remain untouched.
            confirmation.Show(); confirmation.Recenter();
        }
        private void ConfirmClosed(object sender, EventArgs args)
        {
            FreeloadConfirmWindow child = (FreeloadConfirmWindow)sender;
            child.Closed -= ConfirmClosed; confirmation = null;
            if (State == PrankPaymentState.Closed) return;
            if (child.Accepted) CloseInternally(PrankCloseReason.Confirmed);
            else { State = PrankPaymentState.Main; interactive.IsEnabled = true; Activate(); }
        }
        protected override void OnClosing(CancelEventArgs args)
        {
            if (closeReason == PrankCloseReason.None && !Dispatcher.HasShutdownStarted)
            {
                args.Cancel = true;
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(RequestFreeload));
            }
            base.OnClosing(args);
        }
        private void CloseInternally(PrankCloseReason reason)
        {
            if (State == PrankPaymentState.Closed) return;
            closeReason = reason; State = PrankPaymentState.Closed;
            hintTimer.Stop(); hint.Visibility = Visibility.Collapsed;
            if (confirmation != null)
            {
                FreeloadConfirmWindow child = confirmation; confirmation = null;
                child.Closed -= ConfirmClosed; child.Close();
            }
            Close();
        }
        public void CloseForCleanup() { CloseInternally(PrankCloseReason.Cleanup); }
        private void MainClosed(object sender, EventArgs args)
        {
            State = PrankPaymentState.Closed;
            hintTimer.Stop(); hintTimer.Tick -= HideHint;
            if (placement != null) { placement.Dispose(); placement = null; }
            if (confirmation != null) { confirmation.Closed -= ConfirmClosed; confirmation = null; }
            LocationChanged -= MainMoved; SizeChanged -= MainMoved;
        }
    }

    internal sealed class FreeloadConfirmWindow : Window
    {
        private readonly PrankPaymentWindow parent;
        private readonly Button cancel;
        private PrankPlacementSubscription placement;
        internal bool Accepted { get; private set; }
        internal FreeloadConfirmWindow(PrankPaymentWindow parent)
        {
            this.parent = parent;
            PrankTheme.Configure(this, "真的要白嫖嘛QAQ");
            Owner = parent; Width = 430; Height = 230;
            Grid panel = new Grid { Width = 406, Height = 206, Margin = new Thickness(0) };
            panel.RowDefinitions.Add(new RowDefinition());
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(78) });
            TextBlock message = PrankTheme.Text("真的要白嫖嘛QAQ", 28, PrankTheme.Purple);
            message.HorizontalAlignment = HorizontalAlignment.Center; panel.Children.Add(message);
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top };
            Button accept = PrankTheme.Button("ConfirmFreeload", "确定", false); accept.MinWidth = 120;
            accept.Click += delegate { Complete(true); };
            cancel = PrankTheme.Button("CancelFreeload", "不白嫖了", true); cancel.MinWidth = 150; cancel.Margin = new Thickness(14, 0, 0, 0);
            cancel.Click += delegate { Complete(false); };
            // Enter on the initial focus cancels. The accept button is still
            // reachable by Tab/Shift+Tab and normal keyboard button activation.
            cancel.IsDefault = true;
            buttons.Children.Add(accept); buttons.Children.Add(cancel);
            Grid.SetRow(buttons, 1); panel.Children.Add(buttons);
            Content = new Viewbox { Stretch = Stretch.Uniform, Child = PrankTheme.Frame(panel) };
            SourceInitialized += delegate { placement = new PrankPlacementSubscription(this, Recenter); Recenter(); };
            Loaded += delegate { cancel.Focus(); Keyboard.Focus(cancel); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs args)
            { if (args.Key == Key.Escape) { args.Handled = true; Complete(false); } };
            Closed += delegate { if (placement != null) { placement.Dispose(); placement = null; } };
        }
        internal void Complete(bool accepted) { Accepted = accepted; Close(); }
        internal void Recenter()
        {
            Rect bounds = PrankPlacement.Bounds(parent);
            if (bounds.IsEmpty) return;
            IntPtr monitor = PrankPlacement.SelectMonitor(new WindowInteropHelper(parent).Handle);
            PrankPlacement.Apply(this, PrankPlacement.ConfirmBounds(bounds, PrankPlacement.WorkArea(monitor), PrankPlacement.Scale(parent)));
        }
    }
}
