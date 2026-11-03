namespace DynamicIslandNotifier;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIslandNotifier.Core;
using DynamicIslandNotifier.Services;
using DynamicIslandNotifier.UI;

/// <summary>
/// A small floating card while a tree grows: the tree, the time left and a
/// progress line. Drag it anywhere you like (it remembers the spot); it
/// fades while the pointer is over it so you can still see what's behind.
/// Right-click for a small menu.
/// </summary>
public sealed class FocusWidget : Window
{
    private readonly FocusService _focus;
    private readonly TreeShape _tree = new() { LayoutTransform = new ScaleTransform(0.34, 0.34), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _time = new() { Foreground = Brushes.White, FontSize = 22, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI") };
    private readonly TextBlock _sub  = new() { Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)), FontSize = 11, FontFamily = new FontFamily("Segoe UI") };
    private readonly ScaleTransform _fill = new(0, 1);
    private readonly Border _card;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _hiddenUntilDone;
    private bool _placed;

    public FocusWidget(FocusService focus)
    {
        _focus = focus;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "DynamicIslandFocus";

        var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(_time);
        text.Children.Add(_sub);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 8, 16, 10) };
        var treeBox = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(20),
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x30, 0x22)), Child = _tree
        };
        row.Children.Add(treeBox);
        row.Children.Add(text);

        var green = new SolidColorBrush(Color.FromRgb(0x34, 0xC7, 0x59));
        var bar = new Grid { Height = 3, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(14, 0, 14, 5) };
        bar.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), CornerRadius = new CornerRadius(1.5) });
        bar.Children.Add(new Border { Background = green, CornerRadius = new CornerRadius(1.5), RenderTransform = _fill, RenderTransformOrigin = new Point(0, 0.5) });

        var grid = new Grid();
        grid.Children.Add(row);
        grid.Children.Add(bar);

        _card = new Border
        {
            Child = grid, CornerRadius = new CornerRadius(16), Margin = new Thickness(14),
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x1C, 0x1C, 0x1F)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x77, 0x34, 0xC7, 0x59)), BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.SizeAll, ToolTip = "Kéo để di chuyển · chuột phải để xem thêm",
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0x34, 0xC7, 0x59), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.25 }
        };
        Content = _card;

        // Fade (not vanish) under the pointer
        _card.MouseEnter += (_, _) => _card.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, TimeSpan.FromMilliseconds(150)));
        _card.MouseLeave += (_, _) => _card.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)));

        // Drag anywhere; the spot is remembered
        _card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != System.Windows.Input.MouseButtonState.Pressed) return;
            try { DragMove(); } catch (InvalidOperationException) { return; }
            KeepOnScreen();
            AppSettings.Current.FocusWidgetLeft = Left;
            AppSettings.Current.FocusWidgetTop  = Top;
            AppSettings.Current.Save();
        };

        _card.ContextMenu = BuildMenu();

        _tick.Tick += (_, _) => Refresh();
        _focus.Changed += () => Dispatcher.BeginInvoke(new Action(Sync));
        SourceInitialized += (_, _) => NativeInterop.MakeOverlay(this);   // never takes focus from your work
        SystemParameters.StaticPropertyChanged += (_, e) => { if (e.PropertyName == nameof(SystemParameters.WorkArea)) KeepOnScreen(); };
        SizeChanged += (_, _) => { if (!_placed) Place(); };

        Sync();
    }

    /// <summary>Shows the card while a tree grows, hides it otherwise.</summary>
    private void Sync()
    {
        if (!_focus.IsRunning) _hiddenUntilDone = false;
        bool show = _focus.IsRunning && !_hiddenUntilDone;
        if (show && !IsVisible)
        {
            Refresh();
            Opacity = 0;
            Show();
            Place();
            NativeInterop.ForceTopMost(this);
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
            _tick.Start();
        }
        else if (!show && IsVisible)
        {
            _tick.Stop();
            Hide();
        }
    }

    private void Refresh()
    {
        if (!_focus.IsRunning) return;
        var left = _focus.Remaining;
        _time.Text = left.TotalHours >= 1 ? left.ToString(@"h\:mm\:ss") : left.ToString(@"m\:ss");
        var done = TimeSpan.FromMinutes(_focus.Minutes) - left;
        _sub.Text  = $"{TreeCatalog.Get(_focus.CurrentKind).Name} · đã học {(int)done.TotalMinutes} / {_focus.Minutes} phút";
        _tree.Kind = _focus.CurrentKind;
        _tree.Set(_focus.Progress, dead: false);
        _fill.ScaleX = _focus.Progress;
    }

    /// <summary>Where you last dragged it; the first time, the bottom-right corner above the taskbar.</summary>
    private void Place()
    {
        if (ActualWidth <= 0) return;
        var s = AppSettings.Current;
        if (s.FocusWidgetLeft is { } x && s.FocusWidgetTop is { } y)
        {
            Left = x;
            Top  = y;
        }
        else
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 4;
            Top  = area.Bottom - ActualHeight - 4;
        }
        KeepOnScreen();
        _placed = true;
    }

    /// <summary>Never lost off-screen (e.g. after unplugging a monitor).</summary>
    private void KeepOnScreen()
    {
        double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop;
        double vw = SystemParameters.VirtualScreenWidth, vh = SystemParameters.VirtualScreenHeight;
        Left = Math.Clamp(Left, vl - 14, vl + vw - ActualWidth + 14);
        Top  = Math.Clamp(Top,  vt - 14, vt + vh - ActualHeight + 14);
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();

        var reset = new MenuItem { Header = "Về góc dưới bên phải" };
        reset.Click += (_, _) =>
        {
            AppSettings.Current.FocusWidgetLeft = null;
            AppSettings.Current.FocusWidgetTop  = null;
            AppSettings.Current.Save();
            Place();
        };
        menu.Items.Add(reset);

        var hide = new MenuItem { Header = "Ẩn đến khi trồng xong" };
        hide.Click += (_, _) => { _hiddenUntilDone = true; Sync(); };
        menu.Items.Add(hide);

        menu.Items.Add(new Separator());
        var giveUp = new MenuItem { Header = "Bỏ cuộc (cây sẽ chết)" };
        var sure = new MenuItem { Header = "Chắc chắn, bỏ cuộc" };
        sure.Click += (_, _) => _focus.GiveUp();
        giveUp.Items.Add(sure);
        menu.Items.Add(giveUp);
        return menu;
    }

    public void Shutdown() => Close();
}
