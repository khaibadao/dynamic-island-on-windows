namespace DynamicIslandNotifier.Tests;

using DynamicIslandNotifier.Services;
using Xunit;

/// <summary>Quick countdowns typed into Hạn chót.</summary>
public class QuickTimerTests
{
    [Theory]
    [InlineData("15p nấu mì", 15, "Nấu mì")]
    [InlineData("nấu mì 15 phút", 15, "Nấu mì")]
    [InlineData("phơi đồ 40ph", 40, "Phơi đồ")]
    [InlineData("1h30p giặt đồ", 90, "Giặt đồ")]
    [InlineData("hẹn giờ 5p", 5, "Hẹn giờ")]
    [InlineData("25'", 25, "Hẹn giờ")]
    [InlineData("10 min tea", 10, "Tea")]
    [InlineData("ngủ trưa 20p nữa", 20, "Ngủ trưa")]
    public void Reads_timers(string text, int minutes, string label)
    {
        var t = QuickTimer.Parse(text);
        Assert.NotNull(t);
        Assert.Equal(minutes, t!.Value.Minutes);
        Assert.Equal(label, t.Value.Label);
    }

    [Theory]
    [InlineData("Nộp báo cáo PRO 15/11 17h")]   // a deadline
    [InlineData("Thi mai 9h30")]
    [InlineData("Lab OSG 18:45 hôm nay")]
    [InlineData("mua 2 hộp mì")]                // no length
    [InlineData("0p")]
    [InlineData("Đi học 7 giờ sáng mai")]
    public void Leaves_deadlines_and_notes_alone(string text) => Assert.Null(QuickTimer.Parse(text));
}
