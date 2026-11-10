using System.Collections.Generic;
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
    [Fact]
    public void GridSheetWithTimeColumn()
    {
        // A school timetable as Google Sheets exports it: times in column B, days across, no subject codes
        var text = "THỜI KHÓA BIỂU NĂM HỌC 2026-2027 (Áp dụng sau GKI - Đã sửa đổi)\n\tTHỜI GIAN\tTHỨ 2\tTHỨ 3\tTHỨ 4\tTHỨ 5\tTHỨ 6\tTHỨ 7\tCHỦ NHẬT\nSÁNG\t4h - 6h\tChuyên đề Anh ( Thầy Cường )\tChuyên đề Anh ( Cô Thương )\tChuyên đề Anh ( Thầy Cường )\tChuyên đề Anh ( Cô Thương )\tChuyên đề Anh ( Thầy Cường )\tChuyên đề Anh ( Cô Thương )\t\n\t6h45\tVào lớp\tVào lớp\tVào lớp\tVào lớp\tVào lớp\tVào lớp\tDậy\n\t7h-7h45\tChào cờ\tVăn\tVăn\tToán\tToán\tChuyên đề ( Cô Thương )\t7h15-8h45: IELTS Writing cô Mến\n\t7h50-8h35\tTin học\tVăn\tVăn\tToán\tToán\tChuyên đề ( Cô Thương )\t\n\t8h35-8h55\tGiải lao\tGiải lao\tGiải lao\tGiải lao\tGiải lao\tGiải lao\tBTVN cô Mến\n\t8h55-9h40\tTin học\tGiáo dục địa phương\tTiếng pháp\tSử\tChuyên đề ( Thầy Cường )\tToán\t\n\t9h45-10h30\tKinh tế pháp luật\tLý\tTiếng pháp\tSử\tChuyên đề ( Thầy Cường )\t\tDạy Tom\n\t10h35-11h20\tKinh tế pháp luật\tLý\tChuyên đề ( Cô Thương )\tTiếng Pháp\tLý\t\t\nCHIỀU\t13h-14h\tGiải đề HSGTP 12\tToán\tToán\tToán\tToán\tHọc Mỹ thuật\tIELTS Writing Task 1\n\t14h-15h\t\tHọc Thể dục-GDQP\tHọc chuyên đề cô Thương\tLuyện IELTS Writing task 1\tHọc chuyên đề thầy Cường\t\tIELTS Writing Task 2\n\t15h-16h\t\t\t\tLuyện IELTS Writing task 2\t\tGiải lao\t15h30-17h: Toán thầy Lư\n\t16h-17h\tLý\t\tVăn\tVăn\tLuyện Listening\tVăn\t\n\t17h-18h\t\t\t\t17h30-19h: Toán thầy Lư\t17h-19h: Văn thầy Dũng\t17h-19h: Văn thầy Dũng\t\nTỐI\t19h-20h30 (1h30)\tLuyện IELTS Reading\t19h15-20h45: IELTS Speaking thầy Chương\t19h-20h30: Toán thầy Lư\t19h15-20h45: IELTS Reading x Listening cô Trân\t19h-21h: Văn thầy Dũng\tLuyện IELTS Reading\tBù môn\n\t20h30-21h30 (1h)\tBTVN Toán thầy Lư\t\tBTVN Toán thầy Lư\t\t\tVăn\t\n\t21h30-22h30 (1h)\tVăn\tLuyện Listening\tLý\tBTVN cô Trân\tBTVN Toán thầy Lư\tLý\t\n\t22h30-23h (30p)\tLuyện Speaking\tLuyện Speaking\tLuyện Speaking\t22h30-0h: Dạy IELTS Reading\t22h30-0h: Dạy IELTS Writing\tLuyện Listening\t\n\t23h-23h30\tLuyện Listening\tLuyện Listening\tLuyện Listening\tLuyện Speaking\tLuyện Speaking\tLuyện Speaking\t";
        var slots = ScheduleImport.FromTable(ScheduleImport.ParseCsv(text), "sheet");
        // Văn 7h-7h45 + 7h50-8h35 on Tuesday is one block
        var van = Assert.Single(slots, s => s.Weekday == 2 && s.Subject == "Văn" && s.Start == new TimeSpan(7, 0, 0));
        Assert.Equal(new TimeSpan(8, 35, 0), van.End);
        // A time inside the cell wins over the row's
        var toan = Assert.Single(slots, s => s.Weekday == 4 && s.Subject == "Toán thầy Lư" && s.Start == new TimeSpan(17, 30, 0));
        Assert.Equal(new TimeSpan(19, 0, 0), toan.End);
        // Until midnight
        Assert.Contains(slots, s => s.Weekday == 5 && s.Subject == "Dạy IELTS Writing" && s.End == TimeSpan.FromHours(24));
        // Breaks and "Vào lớp" / "Dậy" aren't classes
        Assert.DoesNotContain(slots, s => s.Subject is "Giải lao" or "Vào lớp" or "Dậy");
        // Sunday column read too
        Assert.Contains(slots, s => s.Weekday == 7 && s.Subject == "IELTS Writing cô Mến" && s.Start == new TimeSpan(7, 15, 0));
        Assert.Contains(slots, s => s.Weekday == 1 && s.Subject == "Chuyên đề Anh ( Thầy Cường )" && s.Start == new TimeSpan(4, 0, 0) && s.End == new TimeSpan(6, 0, 0));
        Assert.True(slots.Count > 60, $"only {slots.Count}");
    }

    [Theory]
    [InlineData("Văn", "class")]
    [InlineData("Chuyên đề ( Thầy Cường )", "extra")]
    [InlineData("Toán thầy Lư", "extra")]
    [InlineData("BTVN Toán thầy Lư", "self")]
    [InlineData("Luyện IELTS Reading", "self")]
    [InlineData("Dạy IELTS Writing", "teach")]
    [InlineData("OSG202", "class")]
    public void Kinds(string subject, string kind) => Assert.Equal(kind, ScheduleService.Kind(subject));

    [Fact]
    public void LineTypedByHand()
    {
        var a = ScheduleImport.FromLine("Họp nhóm SWP 15h-16h30 phòng 304")!;
        Assert.Equal("Họp nhóm SWP", a.Subject);
        Assert.Equal(new TimeSpan(15, 0, 0), a.Start);
        Assert.Equal(new TimeSpan(16, 30, 0), a.End);
        Assert.Equal("304", a.Room);
        var b = ScheduleImport.FromLine("Gym 18h")!;
        Assert.Equal("Gym", b.Subject);
        Assert.Equal(new TimeSpan(19, 0, 0), b.End);
        Assert.Null(ScheduleImport.FromLine("không có giờ"));
    }

    [Fact]
    public void ExamListWithHeader()
    {
        var csv = "LỊCH THI HỌC KỲ FALL 2026\nSTT,Ngày thi,Môn thi,Giờ thi,Phòng thi\n1,18/11/2026,MAD101,7h30-9h00,DE-201\n2,Thứ 6 20/11/2026,PRO192,13:00 - 14:30,BE-305\n";
        var rows = ScheduleImport.ParseCsv(csv);
        var slots = ScheduleImport.FromTable(rows, "sheet");
        Assert.Equal(2, slots.Count);
        Assert.Equal(new DateTime(2026, 11, 18), slots[0].Date);
        Assert.Equal(new TimeSpan(7, 30, 0), slots[0].Start);
        Assert.Equal("DE-201", slots[0].Room);
        Assert.True(ScheduleImport.IsExamSheet(rows));
        var preview = ScheduleService.Sort("u", "t", slots, "auto", examSheet: true);
        Assert.Equal(2, preview.Exams.Count);
        Assert.Empty(preview.Weekly);
    }

    [Fact]
    public void DatedRowsWithoutHeader()
    {
        var csv = "18/11 | Thi Toán | 7h30\n20/11 | Thi Văn | 9h-11h\n25/11 | Nộp đồ án SWP\n".Replace(" | ", ",");
        var slots = ScheduleImport.FromTable(ScheduleImport.ParseCsv(csv), "sheet");
        Assert.Equal(3, slots.Count);
        var toan = slots.Single(s => s.Subject == "Thi Toán");
        Assert.Equal(11, toan.Date!.Value.Month);
        Assert.Equal(new TimeSpan(7, 30, 0), toan.Start);
        // Sorted: names with "thi" are exams; one with no time is a deadline
        var preview = ScheduleService.Sort("u", "t", slots, "auto", examSheet: false);
        Assert.Equal(2, preview.Exams.Count);
        Assert.Equal("Nộp đồ án SWP", Assert.Single(preview.Due).Subject);
        Assert.Empty(preview.Dated);
        // Read as classes on request: no exams
        Assert.Empty(ScheduleService.Sort("u", "t", slots, "classes", examSheet: false).Exams);
    }

    [Fact]
    public void GridByDate()
    {
        var csv = "Giờ,18/11,19/11,20/11\n7h-9h,Toán,,Văn\n13h-15h,,Lý,\n";
        var slots = ScheduleImport.FromTable(ScheduleImport.ParseCsv(csv), "sheet");
        Assert.Equal(3, slots.Count);
        Assert.Contains(slots, s => s.Subject == "Lý" && s.Date!.Value.Day == 19 && s.Start == new TimeSpan(13, 0, 0));
    }

    [Theory]
    [InlineData("18/11", 11, 18)]
    [InlineData("Thứ 3, 18-11", 11, 18)]
    [InlineData("2026-11-18", 11, 18)]
    public void Dates(string text, int month, int day)
    {
        var d = ScheduleImport.DateIn(text)!.Value;
        Assert.Equal(month, d.Month);
        Assert.Equal(day, d.Day);
    }

    [Fact]
    public void DeadlineSheetWithoutTimes()
    {
        // "Khai's Deadline": a title, a header with STT · Date · CÔNG VIỆC · … · TIẾN ĐỘ, no times
        var rows = new List<string[]>
        {
            new[] { "DEALDINE", "", "", "", "", "", "" },
            new[] { "STT", "Date", "CÔNG VIỆC", "", "TIẾN ĐỘ", "CHECK", "NOTE" },
            new[] { "", "", "NỘI DUNG", "", "", "", "CHI TIẾT" },
            new[] { "1", "18/11", "Kiểm tra cuối kì NWC", "- Kiểm tra PE + FE", "Chưa hoàn thành", "", "" },
            new[] { "2", "20/11", "Kiểm tra cuối kì MAD", "- Kiêm tra PE", "Chưa hoàn thành", "", "" },
            new[] { "3", "21/11", "Kiểm tra cuối kì OSG", "- Kiêm tra PE", "Hoàn thành", "", "" },
            new[] { "4", "25/11", "Nộp báo cáo SWP", "", "Chưa hoàn thành", "", "" },
            new[] { "5", "", "", "", "\u200B", "", "" },
        };
        var slots = ScheduleImport.FromTable(rows, "sheet");
        Assert.Equal(3, slots.Count);                       // the done one and the empty rows are left out
        Assert.All(slots, s => Assert.True(s.AllDay));
        Assert.True(ScheduleImport.IsDeadlineSheet(rows));
        var p = ScheduleService.Sort("u", "t", slots, "auto", examSheet: false, deadlineSheet: true);
        Assert.Equal(2, p.Exams.Count);                     // "Kiểm tra cuối kì …"
        Assert.Equal("Nộp báo cáo SWP", Assert.Single(p.Due).Subject);
        Assert.Empty(p.Weekly);
        Assert.Empty(p.Dated);
    }

    [Fact]
    public void LinkTypedAsSessionIsNotATime()
    {
        Assert.Null(ScheduleImport.FromLine("https://docs.google.com/spreadsheets/d/1k36O0wXSKv559aZRg2H2hl5TJ3uyVEzXocQyv0pS54I/edit?gid=0#gid=0"));
        Assert.NotNull(ScheduleImport.SheetCsvUrl("https://docs.google.com/spreadsheets/d/1k36O0wXSKv559aZRg2H2hl5TJ3uyVEzXocQyv0pS54I/edit?gid=0#gid=0"));
    }
}
