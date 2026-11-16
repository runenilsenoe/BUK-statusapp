using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ExcelDataReader;
using Microsoft.VisualBasic.FileIO;

namespace BukStatus.Core.Samvirk;

public record SamvirkMonth(DateOnly Month, string? Discount, decimal? Coins, decimal? Remaining50, decimal? Remaining100);
public record SamvirkPerson(int PersonId, string Name, int ChurchId, DateOnly? BirthDate,
    Dictionary<string, string?> Fields, Dictionary<string, decimal?> Amounts,
    Dictionary<string, DateOnly?> Dates, SamvirkMonth[] Months);
public record SamvirkData(SamvirkPerson[] People, string Hash);

public static class SamvirkParser
{
    static SamvirkParser() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    public const int MaxBytes = 5 * 1024 * 1024;
    private static readonly string[] DateFields = ["Birth Date", "On Track Until Date", "Setup Date", "UNG Period Until Date"];
    private static readonly Regex Monthly = new(@"^(Discount|Coins|Remaining 50 coins|Remaining 100 coins) (\d{4}-\d{2})$", RegexOptions.CultureInvariant);
    public static SamvirkData Parse(byte[] bytes, string filename)
    {
        if (bytes.Length == 0 || bytes.Length > MaxBytes) throw new InvalidDataException("Filen må være mellom 1 byte og 5 MB.");
        using var stream = new MemoryStream(bytes);
        var rows = new List<string[]>();
        if (Path.GetExtension(filename).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            using var csv = new TextFieldParser(stream, Encoding.UTF8, true) { HasFieldsEnclosedInQuotes = true };
            csv.SetDelimiters(";");
            while (!csv.EndOfData) { rows.Add(csv.ReadFields()!); CheckSize(rows.Count); }
        }
        else if (Path.GetExtension(filename).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = ExcelReaderFactory.CreateReader(stream);
            // Exactly one nonempty sheet, to avoid silently ignoring another export.
            do
            {
                var sheet = new List<string[]>();
                while (reader.Read())
                {
                    if (reader.FieldCount > 500) throw new InvalidDataException("For mange kolonner.");
                    var row = Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetValue(i) switch
                    {
                        null or DBNull => "", DateTime date => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                        IFormattable value => value.ToString(null, CultureInfo.InvariantCulture), var value => value.ToString() ?? ""
                    }).ToArray();
                    if (row.Any(v => !string.IsNullOrWhiteSpace(v))) sheet.Add(row);
                    CheckSize(sheet.Count);
                }
                if (sheet.Count > 0)
                {
                    if (rows.Count > 0) throw new InvalidDataException("Filen må ha ett ark med data.");
                    rows = sheet;
                }
            } while (reader.NextResult());
        }
        else throw new InvalidDataException("Bruk .xlsx eller semikolonseparert .csv.");
        if (rows.Count < 2) throw new InvalidDataException("Filen mangler personer.");
        var headers = rows[0].Select(h => h.Trim().TrimStart('\uFEFF')).ToArray();
        if (headers.Length > 500 || headers.Any(string.IsNullOrWhiteSpace) || headers.Distinct().Count() != headers.Length)
            throw new InvalidDataException("Tomme eller dupliserte kolonnenavn.");
        foreach (var field in new[] { "BCC Person ID", "Display Name", "Church Id" })
            if (!headers.Contains(field)) throw new InvalidDataException($"Mangler kolonnen {field}.");
        var periods = new Dictionary<string, (string Kind, DateOnly Month)>();
        foreach (var header in headers)
        {
            var match = Monthly.Match(header);
            if (match.Success && DateOnly.TryParseExact(match.Groups[2].Value + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
                periods[header] = (match.Groups[1].Value, month);
            else if (new[] { "Discount ", "Coins ", "Remaining 50 coins ", "Remaining 100 coins " }.Any(header.StartsWith))
                throw new InvalidDataException($"Ugyldig måned i kolonnen {header}.");
        }
        var people = new List<SamvirkPerson>();
        for (var index = 1; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            if (row.Length != headers.Length) throw new InvalidDataException($"Rad {index + 1}: antall kolonner stemmer ikke.");
            var fields = headers.Select((h, i) => (h, value: string.IsNullOrWhiteSpace(row[i]) ? null : row[i].Trim())).ToDictionary(p => p.h, p => p.value);
            int PositiveId(string key) => int.TryParse(fields[key], out var id) && id > 0 ? id : throw new InvalidDataException($"Rad {index + 1}: ugyldig {key}.");
            var id = PositiveId("BCC Person ID"); var church = PositiveId("Church Id");
            var name = fields["Display Name"];
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException($"Rad {index + 1}: navn mangler.");
            var dates = new Dictionary<string, DateOnly?>();
            foreach (var key in DateFields.Where(fields.ContainsKey))
            {
                var value = fields[key];
                dates[key] = value is null ? null : DateOnly.TryParseExact(value, new[] { "dd.MM.yyyy", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? date : throw new InvalidDataException($"Rad {index + 1}: ugyldig dato i {key}.");
            }
            decimal? Number(string key)
            {
                var value = fields.GetValueOrDefault(key);
                if (value is null) return null;
                value = value.Replace("\u00a0", "").Replace(" ", "");
                // Decimal comma or decimal point; never interpret a separator as thousands.
                if (value.Contains(',') && value.Contains('.')) throw new InvalidDataException($"Rad {index + 1}: tvetydig tall i {key}.");
                return decimal.TryParse(value.Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
                    ? amount : throw new InvalidDataException($"Rad {index + 1}: ugyldig tall i {key}.");
            }
            var amounts = headers.Where(h => h.EndsWith(" NOK", StringComparison.Ordinal)).ToDictionary(h => h, Number);
            var months = periods.Values.Select(p => p.Month).Distinct().Order().Select(month =>
            {
                string? Key(string kind) => periods.FirstOrDefault(p => p.Value == (kind, month)).Key;
                decimal? Num(string kind) => Key(kind) is { } key ? Number(key) : null;
                return new SamvirkMonth(month, Key("Discount") is { } discount ? fields[discount] : null,
                    Num("Coins"), Num("Remaining 50 coins"), Num("Remaining 100 coins"));
            }).ToArray();
            people.Add(new(id, name, church, dates.GetValueOrDefault("Birth Date"), fields, amounts, dates, months));
        }
        if (people.Count == 0 || people.Select(p => p.PersonId).Distinct().Count() != people.Count)
            throw new InvalidDataException("Tom fil eller dupliserte BCC person-ID-er.");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        return new(people.ToArray(), hash);
    }
    private static void CheckSize(int rows) { if (rows > 10001) throw new InvalidDataException("Maksimalt 10 000 personer."); }
}
