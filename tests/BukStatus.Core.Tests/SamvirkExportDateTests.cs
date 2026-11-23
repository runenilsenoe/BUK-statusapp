using BukStatus.Core.Samvirk;
namespace BukStatus.Core.Tests;
public class SamvirkExportDateTests
{
    [Theory]
    [InlineData("2026_11_10_ung_export.xlsx", 2026, 11, 10)]
    [InlineData("2028_02_29_ung.csv", 2028, 2, 29)]
    [InlineData("C:\\exports\\2026_11_10_ung_export.xlsx", 2026, 11, 10)]
    public void Filename_determines_export_date(string file, int y, int m, int d) => Assert.Equal(new DateOnly(y,m,d), SamvirkExportDate.Resolve(file));
    [Theory]
    [InlineData("2026_02_30_ung.xlsx")]
    [InlineData("2026_13_01_ung.xlsx")]
    [InlineData("2026_1_01_ung.xlsx")]
    [InlineData("ung.xlsx")]
    public void Invalid_or_missing_date_is_not_silently_today(string file) => Assert.Throws<InvalidDataException>(() => SamvirkExportDate.Resolve(file));
    [Fact] public void Explicit_date_is_fallback_but_cannot_override_dated_filename()
    {
        var date = new DateOnly(2026,11,10);
        Assert.Equal(date, SamvirkExportDate.Resolve("ung.xlsx", date));
        Assert.Equal(date, SamvirkExportDate.Resolve("2026_11_10_ung.xlsx", date));
        Assert.Throws<InvalidDataException>(() => SamvirkExportDate.Resolve("2026_11_10_ung.xlsx",date.AddDays(1)));
    }
}
