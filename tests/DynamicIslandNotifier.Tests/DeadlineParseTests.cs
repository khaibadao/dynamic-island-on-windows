namespace DynamicIslandNotifier.Tests;

using DynamicIslandNotifier.Services;
using Xunit;

/// <summary>Reading what you type into Hạn chót.</summary>
public class DeadlineParseTests
{
    // Thursday 5 Nov 2026, 14:00
    private static readonly DateTime Now = new(2026, 11, 5, 14, 0, 0);

    [Theory]
    [InlineData("Nộp báo cáo PRO 15/11 17h", "Nộp báo cáo PRO", 2026, 11, 15, 17, 0)]
    [InlineData("Thi mai 9h30", "Thi", 2026, 11, 6, 9, 30)]
    [InlineData("Họp nhóm thứ 6 20h", "Họp nhóm", 2026, 11, 6, 20, 0)]
    [InlineData("Bài tập 20/11", "Bài tập", 2026, 11, 20, 23, 59)]
    [InlineData("Lab OSG 18:45 hôm nay", "Lab OSG", 2026, 11, 5, 18, 45)]
    [InlineData("Đi học 7 giờ sáng mai", "Đi học", 2026, 11, 6, 7, 0)]
    [InlineData("Gặp thầy 3h chiều mốt", "Gặp thầy", 2026, 11, 7, 15, 0)]
    [InlineData("Thi cuối kỳ 3/1/2027", "Thi cuối kỳ", 2027, 1, 3, 23, 59)]
    [InlineData("Ôn MAD 2/1", "Ôn MAD", 2027, 1, 2, 23, 59)]       // a date already gone this year → next year
    public void Reads_title_and_due(string text, string title, int y, int mo, int d, int h, int mi)
    {
        var parsed = DeadlineService.Parse(text, Now);
        Assert.NotNull(parsed);
        Assert.Equal(title, parsed!.Value.Title);
        Assert.Equal(new DateTime(y, mo, d, h, mi, 0), parsed.Value.Due);
    }

    [Fact]
    public void A_time_already_past_today_means_tomorrow()
    {
        var parsed = DeadlineService.Parse("Gọi mẹ 9h", Now);
        Assert.Equal(new DateTime(2026, 11, 6, 9, 0, 0), parsed!.Value.Due);
    }

    [Fact]
    public void No_date_or_time_is_rejected()
    {
        Assert.Null(DeadlineService.Parse("Làm bài tập", Now));
    }

    [Theory]
    [InlineData(2, 0, "còn 2 ngày")]
    [InlineData(0, 3, "còn 3 giờ")]
    [InlineData(0, -1, "quá hạn 1 giờ")]
    public void Says_how_long_is_left(int days, int hours, string expected)
    {
        var due = Now.AddDays(days).AddHours(hours);
        Assert.Equal(expected, DeadlineService.Left(due, Now));
    }
}
