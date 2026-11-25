using System.Text.RegularExpressions;

namespace BukStatus.Core.Pot;

public record CollectionTotals(decimal Total, decimal Revenue, decimal Operations, decimal Subsidies,
    DateOnly From, DateOnly To)
{
    public static CollectionTotals Build(IEnumerable<ExtraContribution> transactions, DateOnly from, DateOnly to)
    {
        decimal total = 0, operations = 0, subsidies = 0;
        foreach (var row in transactions.Where(t => t.Date >= from && t.Date <= to))
        {
            var description = row.Description ?? "";
            if (IsEasterAdjustment(description)) continue;
            total += row.Amount;
            if (IsSubsidy(description, from.Year))
                subsidies += row.Amount;
            else if (Regex.IsMatch(description, @"\b(?:drift\p{L}*|pappb\p{L}*)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                operations += row.Amount;
        }
        return new(total, total - operations - subsidies, operations, subsidies, from, to);
    }

    public static bool IsEasterAdjustment(string description)
    {
        const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        return Regex.IsMatch(description, @"\bp[åa]ske\s*camp\b", options)
            && Regex.IsMatch(description, @"\b(?:null\s*f[øo]r\p{L}*|nullstill\p{L}*|fratrekk)\b", options);
    }

    private static bool IsSubsidy(string description, int year)
    {
        const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        if (Regex.IsMatch(description, @"\btilskudd\p{L}*\b", options)) return true;
        if (Regex.IsMatch(description, @"\bBCC\b", options)
            && Regex.IsMatch(description, @"\b(?:spons\p{L}*|st[øo]tt\p{L}*)\b", options)) return true;
        if (!Regex.IsMatch(description, @"\boverf[øo]r\p{L}*\b", options)) return false;
        return Regex.IsMatch(description, @"\b(?:forrige\s+[åa]r|fjor\p{L}*)\b", options)
            || Regex.Matches(description, @"\b20\d{2}\b").Any(m => int.Parse(m.Value) < year);
    }
}
