using System.Globalization;
namespace BukStatus.Core.Samvirk;

public record SamvirkMonthlyPayment(DateOnly Month, decimal? Coins, decimal? MinimumConfirmedAmount);
public record SamvirkStatusRow(int PersonId, string Name, int? Age, decimal? MonthlyAmount, decimal? BehindAmount, SamvirkMonthlyPayment[] Payments);
public record SamvirkStatusReport(DateOnly SnapshotDate, DateOnly[] Months, SamvirkStatusRow[] Active, SamvirkStatusRow[] Inactive, int UnknownStatus);

public static class SamvirkStatus
{
    public static SamvirkStatusReport Build(SamvirkSnapshot snapshot, decimal minimumMonthlyAmount = 2700m)
    {
        if (minimumMonthlyAmount <= 0) throw new ArgumentOutOfRangeException(nameof(minimumMonthlyAmount));
        var end = new DateOnly(snapshot.SnapshotDate.Year, snapshot.SnapshotDate.Month, 1);
        var months = Enumerable.Range(-4, 5).Select(end.AddMonths).ToArray();
        bool? Active(SamvirkPerson person) => bool.TryParse(person.Fields.GetValueOrDefault("Active BUK Drive"), out var drive) ? drive : null;
        SamvirkStatusRow Row(SamvirkPerson person)
        {
            int? age = null;
            if (person.BirthDate is { } birth && birth <= snapshot.SnapshotDate)
            {
                var years = snapshot.SnapshotDate.Year - birth.Year;
                age = snapshot.SnapshotDate < birth.AddYears(years) ? years - 1 : years;
            }
            else if (person.BirthDate is null && int.TryParse(person.Fields.GetValueOrDefault("Age"), NumberStyles.None, CultureInfo.InvariantCulture, out var reported) && reported is >= 0 and <= 130) age = reported;
            var monthly = person.Amounts.GetValueOrDefault("On Track Amount NOK");
            var payments = months.Select(month =>
            {
                var coins = person.Months.FirstOrDefault(m => m.Month == month)?.Coins;
                // Coins establish a threshold, not the exact transferred amount. Zero coins
                // cannot distinguish no payment from a payment below the minimum.
                decimal? confirmed = coins switch { 100 => monthly, 50 => minimumMonthlyAmount, _ => null };
                return new SamvirkMonthlyPayment(month, coins, confirmed);
            }).ToArray();
            return new(person.PersonId, person.Name, age, monthly,
                person.Amounts.GetValueOrDefault("Get On Track Amount NOK"), payments);
        }
        SamvirkStatusRow[] Rows(bool active) => snapshot.People.Where(p => Active(p) == active).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).Select(Row).ToArray();
        var active = Rows(true); var inactive = Rows(false);
        return new(snapshot.SnapshotDate, months, active, inactive, snapshot.People.Length - active.Length - inactive.Length);
    }
}
