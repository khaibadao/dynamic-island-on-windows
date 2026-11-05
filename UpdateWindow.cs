namespace DynamicIslandNotifier;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIslandNotifier.Services;

/// <summary>"Có bản mới": what changed, then Cập nhật ngay / Để sau.</summary>
public sealed class UpdateWindow : Window
{
    private static UpdateWindow? _open;
    private readonly TextBlock _changes;
    private readonly Button _install;
    private readonly TextBlock _status;

    /// <summary>Raised when you choose to install.</summary>
    public event Action? InstallRequested;

    public static UpdateWindow ShowFor(Version version, Action install)
    {
        if (_open != null) { _open.Activate(); return _open; }
        var w = new UpdateWindow(version);
        w.InstallRequested += install;
        _open = w;
        w.Closed += (_, _) => _open = null;
        w.Show();
        w.Activate();
        return w;
    }

    private UpdateWindow(Version version)
    {
        Title = "Dynamic Island · Bản mới";
        Width = 500; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brush(0x16, 0x16, 0x19);
        FontFamily = new FontFamily("Segoe UI");
        Topmost = true;

        var stack = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        stack.Children.Add(new TextBlock { Text = $"Có bản mới v{version.ToString(3)}", Foreground = Brush(0xF2, 0xF2, 0xF2), FontSize = 18, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock
        {
            Text = $"Bạn đang dùng v{UpdateChecker.Current.ToString(3)}. Bản mới được kiểm tra mã SHA-256 trước khi cài, rồi app tự mở lại.",
            Foreground = Brush(0xA8, 0xF2, 0xF2, 0xF2), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12)
        });

        _changes = new TextBlock { Text = "Đang tải danh sách thay đổi…", Foreground = Brush(0xE0, 0xE0, 0xE4), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, LineHeight = 19 };
        stack.Children.Add(new Border
        {
            Background = Brush(0x24, 0x24, 0x28), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 12),
            Child = new ScrollViewer { Content = _changes, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        });

        _status = new TextBlock { Foreground = Brush(0xFF, 0xC0, 0x5A), FontSize = 12, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        stack.Children.Add(_status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var later = new Button { Content = "Để sau", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        later.Click += (_, _) => Close();
        _install = new Button { Content = "Cập nhật ngay", Padding = new Thickness(14, 6, 14, 6), IsDefault = true, FontWeight = FontWeights.SemiBold };
        _install.Click += (_, _) =>
        {
            _install.IsEnabled = false;
            later.IsEnabled = false;
            Say("Đang tải và kiểm tra…");
            InstallRequested?.Invoke();
        };
        buttons.Children.Add(later);
        buttons.Children.Add(_install);
        stack.Children.Add(buttons);
        Content = stack;

        Loaded += async (_, _) => _changes.Text = await UpdateChecker.ChangesAsync(version);
    }

    public void Say(string text)
    {
        _status.Text = text;
        _status.Visibility = Visibility.Visible;
    }

    /// <summary>Installing failed: say why and let you try again.</summary>
    public void Failed(string why)
    {
        Say(why + ". Thử lại sau, hoặc dùng lệnh cài.");
        _install.IsEnabled = true;
    }

    private static Brush Brush(byte r, byte g, byte b) => Brush(0xFF, r, g, b);
    private static Brush Brush(byte a, byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromArgb(a, r, g, b)); x.Freeze(); return x; }
}
