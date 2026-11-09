using System;
using System.Linq;
using DynamicIslandNotifier.Services;
using Xunit;

public class ScheduleImportTests
{
    [Theory]
    [InlineData("Thứ 2", 1)]
    [InlineData("T3", 2)]
    [InlineData("thứ tư", 3)]
    [InlineData("5", 4)]
    [InlineData("FRI", 5)]
    [InlineData("Saturday", 6)]
    [InlineData("CN", 7)]
    [InlineData("Chủ nhật", 7)]
    [InlineData("Môn", 0)]
    public void Weekdays(string text, int expected) => Assert.Equal(expected, ScheduleImport.Weekday(text));

    [Fact]
    public void SheetWithHeader()
    {
        var csv = "Thứ,Bắt đầu,Kết thúc,Môn,Phòng\n2,7:30,9:50,OSG202,DE-C201\nThứ 4,12:50,15:10,MAD101,BE-305\n";
        var slots = ScheduleImport.FromTable(ScheduleImport.ParseCsv(csv), "sheet");
        Assert.Equal(2, slots.Count);
        Assert.Equal(1, slots[0].Weekday);
        Assert.Equal(new TimeSpan(7, 30, 0), slots[0].Start);
        Assert.Equal(new TimeSpan(9, 50, 0), slots[0].End);
        Assert.Equal("OSG202", slots[0].Subject);
        Assert.Equal("DE-C201", slots[0].Room);
        Assert.Equal(3, slots[1].Weekday);
    }

    [Fact]
    public void SheetWithTimeRangeAndSemicolons()
    {
        var csv = "Môn;Thứ;Giờ\nPRO192;T6;10h00 - 12h20\n";
        var slot = Assert.Single(ScheduleImport.FromTable(ScheduleImport.ParseCsv(csv), "file"));
        Assert.Equal(5, slot.Weekday);
        Assert.Equal(new TimeSpan(10, 0, 0), slot.Start);
        Assert.Equal(new TimeSpan(12, 20, 0), slot.End);
    }

    [Fact]
    public void FapWeeklyText()
    {
        // As Chrome copies FAP's Weekly timetable: tabs between cells, a cell over several lines
        var text = string.Join("\n",
            "YEAR 2026\tMON\tTUE\tWED\tTHU\tFRI\tSAT\tSUN",
            "WEEK 09/11 To 15/11\t09/11\t10/11\t11/11\t12/11\t13/11\t14/11\t15/11",
            "Slot 0\t-\t-\t-\t-\t-\t-\t-",
            "Slot 1\tOSG202-",
            "at DE-C201",
            "(Not yet)",
            "(7:30-9:50)\t-\tMAD101-",
            "at BE-305",
            "(attended)",
            "(7:30-9:50)\t-\t-\t-\t-",
            "Slot 2\t-\tPRO192-",
            "at DE-210",
            "(10:00-12:20)\t-\t-\t-\t-\t-");
        var slots = ScheduleImport.FromText(text, "fap");
        Assert.Equal(3, slots.Count);
        var osg = slots.Single(s => s.Subject == "OSG202");
        Assert.Equal(1, osg.Weekday);
        Assert.Equal("DE-C201", osg.Room);
        var mad = slots.Single(s => s.Subject == "MAD101");
        Assert.Equal(3, mad.Weekday);
        var pro = slots.Single(s => s.Subject == "PRO192");
        Assert.Equal(2, pro.Weekday);
        Assert.Equal(new TimeSpan(10, 0, 0), pro.Start);
    }

    [Fact]
    public void PlainLinesWithDays()
    {
        var text = "Thứ 2\nOSG202 7:30-9:50 phòng 304\nThứ 5\nNWC203c 15:20 - 17:40";
        var slots = ScheduleImport.FromText(text, "fap");
        Assert.Equal(2, slots.Count);
        Assert.Equal("304", slots[0].Room);
        Assert.Equal(4, slots[1].Weekday);
        Assert.Equal("NWC203c", slots[1].Subject);
    }

    [Fact]
    public void PhotoWordsByColumn()
    {
        ScheduleImport.Word W(string t, double x, double y) => new(t, x, y, t.Length * 7, 12);
        var words = new[]
        {
            W("MON", 100, 10), W("TUE", 200, 10), W("WED", 300, 10), W("THU", 400, 10),
            W("OSG202", 95, 50), W("(7:30-9:50)", 92, 66),
            W("MAD101", 295, 50), W("(12:50-15:10)", 290, 66),
        };
        var slots = ScheduleImport.FromWords(words, "photo");
        Assert.Equal(2, slots.Count);
        Assert.Equal(1, slots.Single(s => s.Subject == "OSG202").Weekday);
        Assert.Equal(3, slots.Single(s => s.Subject == "MAD101").Weekday);
    }

    [Fact]
    public void CalendarFile()
    {
        var ics = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART:20261109T073000\r\nDTEND:20261109T095000\r\nRRULE:FREQ=WEEKLY;COUNT=10\r\nSUMMARY:OSG202\r\nLOCATION:DE-C201\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var slot = Assert.Single(ScheduleImport.FromIcs(ics, "file"));
        Assert.Null(slot.Date);
        Assert.Equal(1, slot.Weekday);
        Assert.Equal("DE-C201", slot.Room);
    }

    [Fact]
    public void SheetLink()
    {
        Assert.Equal("https://docs.google.com/spreadsheets/d/abc_123/export?format=csv&gid=42",
            ScheduleImport.SheetCsvUrl("https://docs.google.com/spreadsheets/d/abc_123/edit#gid=42"));
        Assert.Null(ScheduleImport.SheetCsvUrl("https://example.com"));
    }

    [Theory]
    [InlineData("Thi PE PRO192", true)]
    [InlineData("Kiểm tra giữa kỳ MAD", true)]
    [InlineData("PE PRO192", true)]
    [InlineData("Nộp báo cáo", false)]
    [InlineData("Thiết kế web", false)]
    public void Exams(string title, bool exam) => Assert.Equal(exam, DeadlineService.IsExamTitle(title));
}
