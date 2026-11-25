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
            total += row.Amount;
            var description = row.Description ?? "";
            if (Regex.IsMatch(description, @"\b(?:drift\p{L}*|pappb\p{L}*)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                operations += row.Amount;
            else if (Regex.IsMatch(description, @"\btilskudd\p{L}*\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                subsidies += row.Amount;
        }
        return new(total, total - operations - subsidies, operations, subsidies, from, to);
    }
}
