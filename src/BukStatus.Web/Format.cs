namespace BukStatus.Web;

public static class Format
{
    public static string Kr(decimal amount) => $"{amount:N0} kr";

    public static string Pct(decimal percent) => $"{percent:0} %";

    public static string MonthName(int month) =>
        System.Globalization.CultureInfo.GetCultureInfo("nb-NO").DateTimeFormat.GetMonthName(month);
}
