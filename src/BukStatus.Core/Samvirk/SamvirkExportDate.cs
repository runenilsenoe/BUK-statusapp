using System.Globalization;
using System.Text.RegularExpressions;

namespace BukStatus.Core.Samvirk;

public static class SamvirkExportDate
{
    public static DateOnly Resolve(string filename, DateOnly? supplied = null)
    {
        var name = filename.Replace('\\', '/').Split('/').Last();
        var match = Regex.Match(name, @"^(\d{4}_\d{2}_\d{2})(?:_|\.|$)", RegexOptions.CultureInvariant);
        if (match.Success)
        {
            if (!DateOnly.TryParseExact(match.Groups[1].Value, "yyyy_MM_dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new InvalidDataException("Filnavnet inneholder en ugyldig eksportdato.");
            if (supplied is { } explicitDate && explicitDate != date)
                throw new InvalidDataException("Oppgitt eksportdato stemmer ikke med datoen i filnavnet.");
            return date;
        }
        if (Regex.IsMatch(name, @"^\d{4}_", RegexOptions.CultureInvariant))
            throw new InvalidDataException("Filnavnets dato må ha format yyyy_MM_dd, for eksempel 2026_11_10_ung.xlsx.");
        return supplied ?? throw new InvalidDataException("Eksportdato mangler. Bruk filnavn som 2026_11_10_ung.xlsx eller oppgi dato yyyy-MM-dd.");
    }
}
