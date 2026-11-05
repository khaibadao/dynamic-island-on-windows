namespace DynamicIslandNotifier;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIslandNotifier.Services;

/// <summary>
/// "Cài đặt & quyền riêng tư": what the app looks at, what it sends where,
/// and a switch for each of those.
/// </summary>
public sealed class SettingsWindow : Window
{
    private static SettingsWindow? _open;
    private static readonly Brush Ink = Freeze(Color.FromRgb(0xF2, 0xF2, 0xF2));
    private static readonly Brush Dim = Freeze(Color.FromArgb(0xA8, 0xF2, 0xF2, 0xF2));
    private static readonly Brush Card = Freeze(Color.FromRgb(0x24, 0x24, 0x28));

    /// <summary>Called after a switch changes, so the app can apply it right away.</summary>
    public static event Action? Changed;

    public static void ShowOnce()
    {
        if (_open != null) { _open.Activate(); return; }
        _open = new SettingsWindow();
        _open.Closed += (_, _) => _open = null;
        _open.Show();
        _open.Activate();
    }

    private SettingsWindow()
    {
        Title = "Dynamic Island · Cài đặt & quyền riêng tư";
        Width = 520; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Freeze(Color.FromRgb(0x16, 0x16, 0x19));
        FontFamily = new FontFamily("Segoe UI");
        Topmost = true;

        var s = AppSettings.Current;
        var stack = new StackPanel { Margin = new Thickness(22, 18, 22, 20) };
        stack.Children.Add(new TextBlock { Text = "Cài đặt & quyền riêng tư", Foreground = Ink, FontSize = 18, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(Note("Mọi thứ dưới đây chỉ xử lý trên máy bạn, trừ những gì ghi rõ là gửi ra mạng. Lịch sử copy và thông báo được mã hoá, chỉ tài khoản Windows của bạn mở được.", top: 6));

        stack.Children.Add(Header("Trên máy bạn"));
        stack.Children.Add(Switch("Đọc tên cửa sổ đang mở",
            "Để Deep focus nhận ra YouTube, Facebook… khi đang trồng cây, và để biết khi bạn trình chiếu / chia sẻ màn hình. Chỉ xem, không lưu, không gửi đi.",
            s.ReadWindowTitles, v => s.ReadWindowTitles = v));
        stack.Children.Add(Switch("Nghe âm thanh loa cho sóng nhạc",
            "Sóng nhạc trên viên nhảy theo bài đang phát. Âm thanh chỉ được đo độ to theo dải, không ghi âm, không lưu. Tắt thì sóng chỉ lên xuống đều.",
            s.ListenToAudio, v => s.ListenToAudio = v));
        stack.Children.Add(Switch("Ẩn banner Windows trùng với tai thỏ",
            "Khi tai thỏ hiện thông báo, banner của Windows được ẩn để không hiện hai lần. Thoát app (hoặc tắt mục này) là trả lại như cũ.",
            s.HideWindowsBanners, v => s.HideWindowsBanners = v));
        stack.Children.Add(Switch("Nhắc hạn chót cả khi Không làm phiền",
            "Nhắc trước 1 ngày, 1 giờ và lúc đến hạn vẫn hiện dù đang bật Không làm phiền.",
            s.DeadlinesBypassDnd, v => s.DeadlinesBypassDnd = v));

        stack.Children.Add(Header("Gửi ra mạng"));
        stack.Children.Add(Switch("Biểu tượng trang web (Google)",
            "Thông báo từ trình duyệt hiện logo của trang. Tên miền (vd. messenger.com) được gửi tới dịch vụ biểu tượng của Google, rồi lưu lại trên máy.",
            s.WebIcons, v => s.WebIcons = v));
        stack.Children.Add(Switch("Tìm lời bài hát trên NetEase",
            "Ngoài LRCLIB, tìm thêm trên NetEase (Trung Quốc) để có nhiều bài hơn. Chỉ gửi tên bài và ca sĩ.",
            s.NetEaseLyrics, v => s.NetEaseLyrics = v));
        stack.Children.Add(Note("Luôn dùng: LRCLIB (tên bài, ca sĩ, để tìm lời) và GitHub (kiểm tra bản mới khi mở app và mỗi 6 giờ). Không có quảng cáo, không thống kê, không gửi nội dung thông báo hay copy.", top: 10));

        Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 720 };
    }

    private static TextBlock Header(string text) => new()
    {
        Text = text.ToUpperInvariant(), Foreground = Dim, FontSize = 11, FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(2, 18, 0, 6)
    };

    private static TextBlock Note(string text, double top) => new()
    {
        Text = text, Foreground = Dim, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0), LineHeight = 18
    };

    private static Border Switch(string title, string help, bool value, Action<bool> set)
    {
        var box = new CheckBox { IsChecked = value, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 10, 0) };
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = title, Foreground = Ink, FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = help, Foreground = Dim, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        var row = new DockPanel();
        DockPanel.SetDock(box, Dock.Left);
        row.Children.Add(box);
        row.Children.Add(text);
        void Apply(bool on) { set(on); AppSettings.Current.Save(); Changed?.Invoke(); }
        box.Checked   += (_, _) => Apply(true);
        box.Unchecked += (_, _) => Apply(false);
        var card = new Border { Background = Card, CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 6), Child = row, Cursor = System.Windows.Input.Cursors.Hand };
        card.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is not CheckBox) box.IsChecked = !box.IsChecked; };
        return card;
    }

    private static Brush Freeze(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
}
