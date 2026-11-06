namespace DynamicIslandNotifier.Tests;

using DynamicIslandNotifier.Services;
using Xunit;

/// <summary>Finding the newest version and what changed since yours.</summary>
public class UpdateCheckerTests
{
    [Theory]
    [InlineData("https://github.com/khaibadao/dynamic-island-on-windows/releases/tag/v2.3.0", "2.3.0")]
    [InlineData("https://github.com/khaibadao/dynamic-island-on-windows/releases/tag/2.10.1", "2.10.1")]
    public void Reads_the_version_from_the_latest_link(string link, string expected)
    {
        Assert.Equal(Version.Parse(expected), UpdateChecker.VersionFromLink(link));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://github.com/khaibadao/dynamic-island-on-windows/releases")]
    [InlineData("https://github.com/khaibadao/dynamic-island-on-windows/releases/tag/v2.3.0-beta")]
    public void Anything_else_is_not_a_version(string? link)
    {
        Assert.Null(UpdateChecker.VersionFromLink(link));
    }

    [Fact]
    public void Versions_compare_by_number_not_text()
    {
        Assert.True(new Version(2, 10, 0) > new Version(2, 9, 9));
        Assert.True(new Version(2, 3, 0) > new Version(2, 2, 0));
    }

    private const string Notes = """
        Bản chỉnh sửa từ upstream (MIT).

        ### Mới trong v2.4.0
        - **Thú cưng**: chọn 6 con
        ### Sửa trong v2.4.0
        - Sửa `MessageBox`
        ### Mới trong v2.3.0
        - Cài đặt & quyền riêng tư
        ### Mới trong v2.2.0
        - Nốt nhạc trôi
        """;

    [Fact]
    public void Shows_only_versions_newer_than_yours()
    {
        var text = UpdateChecker.ExtractChanges(Notes, new Version(2, 2, 0));
        Assert.Contains("Mới trong v2.4.0", text);
        Assert.Contains("• Thú cưng: chọn 6 con", text);
        Assert.Contains("• Sửa MessageBox", text);
        Assert.Contains("Cài đặt & quyền riêng tư", text);
        Assert.DoesNotContain("Nốt nhạc trôi", text);
    }

    [Fact]
    public void Nothing_new_gives_nothing()
    {
        Assert.Equal("", UpdateChecker.ExtractChanges(Notes, new Version(2, 4, 0)));
    }
}
