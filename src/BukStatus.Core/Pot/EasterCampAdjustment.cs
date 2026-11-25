using BukStatus.Core.Status;

namespace BukStatus.Core.Pot;

public static class EasterCampAdjustment
{
    public static IReadOnlyDictionary<int, MemberStatusSnapshot> Apply(
        IReadOnlyDictionary<int, MemberStatusSnapshot> statuses, IEnumerable<ExtraContribution> transactions)
    {
        var credits = transactions.Where(t => t.PersonId > 0 && CollectionTotals.IsEasterAdjustment(t.Description ?? ""))
            .GroupBy(t => t.PersonId).ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));
        return statuses.ToDictionary(p => p.Key, p =>
        {
            var status = p.Value;
            var credit = credits.GetValueOrDefault(p.Key);
            if (credit == 0) return status;
            var target = Math.Max(0, status.TotalTarget - credit);
            return status with
            {
                TotalTarget = target,
                TotalContribution = status.TotalContribution - credit,
                // Preserve the API's due fraction; a manual ajour threshold is applied afterwards.
                AjourTarget = status.TotalTarget > 0 ? target * (status.AjourTarget / status.TotalTarget) : 0,
                AjourUntil = null
            };
        });
    }
}
