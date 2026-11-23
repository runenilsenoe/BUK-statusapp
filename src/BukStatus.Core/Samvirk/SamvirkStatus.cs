using System.Globalization;
namespace BukStatus.Core.Samvirk;

public record SamvirkMonthlyPayment(DateOnly Month, decimal? Coins, decimal? MinimumConfirmedAmount, decimal? CalculatedAmount = null);
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
                var period = person.Months.FirstOrDefault(m => m.Month == month);
                var coins = period?.Coins;
                // Coins establish a threshold, not the exact transferred amount. Zero coins
                // cannot distinguish no payment from a payment below the minimum.
                decimal? confirmed = coins switch { 100 => monthly, 50 => minimumMonthlyAmount, _ => null };
                decimal? calculated = null;
                // The undated remainder describes the export month only. Historical coins
                // do not reveal exact payments or the monthly rate used at the time.
                if (month == end && coins != 100 && monthly is > 0)
                {
                    var remaining = person.Amounts.GetValueOrDefault("Remaining Full Discount NOK") ?? period?.Remaining100;
                    if (remaining is >= 0)
                    {
                        var paid = Math.Clamp(monthly.Value - remaining.Value, 0, monthly.Value);
                        // Do not replace a confirmed coins threshold with a contradictory estimate.
                        if (confirmed is null || paid >= confirmed.Value) calculated = paid;
                    }
                }
                return new SamvirkMonthlyPayment(month, coins, confirmed, calculated);
            }).ToArray();
            return new(person.PersonId, person.Name, age, monthly,
                person.Amounts.GetValueOrDefault("Get On Track Amount NOK"), payments);
        }
        SamvirkStatusRow[] Rows(bool active) => snapshot.People.Where(p => Active(p) == active).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).Select(Row).ToArray();
        var active = Rows(true); var inactive = Rows(false);
        return new(snapshot.SnapshotDate, months, active, inactive, snapshot.People.Length - active.Length - inactive.Length);
    }
}
