using System.Globalization;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using PACEFEST.Models;
using PACEFEST.Pages;

namespace PACEFEST.Services;

public class MyshareService(HttpClient httpClient, IOptions<MyshareOptions> options) : CacheClass
{
    private Dictionary<int,PersonContribution> _targets = new();
    private Dictionary<int,PersonStatus> _statuses = new();
    private List<ProgressItem> _personProgressItems = [];

    public decimal BonusContributions => (MockBonusContributed ?? 0) + (ActualBonusContributions ?? 0);
    public decimal? MockBonusContributed { get; set; }
    public decimal? ActualBonusContributions { get; set; }
    public int? TargetAmount => _targets.Count;
    public int? StatusAmount => _statuses.Count;
    public int? AjourAmount => _statuses.Count(x => x.Value.IsOnTrack);

    private readonly MyshareOptions _options = options.Value;
    private string _bearer => _options.Bearer;
    private string BaseUrl => _options.BaseUrl.TrimEnd('/');
    public bool MaximizeBoost { get; set; } = false;


    public async Task<List<ProgressItem>> Refresh(bool force = false)
    {
        Console.WriteLine(ShouldLoadTargets || force ? "Loading targets..." : "Skip targets reload");
        if (ShouldLoadTargets || force)
        {
            TargetsLoading = true;
            await LoadTargets();
            TargetsLoading = false;
            Console.WriteLine($"Targets reloaded -  {_targets.Count} Targets in state");
        }
        
        Console.WriteLine(ShouldLoadStatus || force ? "Loading statuses..." : "Skip status reload");
        if (ShouldLoadStatus || force)
        {
            StatusesLoading = true;
            await LoadStatus();
            StatusesLoading = false;
            Console.WriteLine($"Statuses reloaded - {_statuses.Count} Statuses in state");
        }
        
        await SetProgressItems();
        
        Console.WriteLine(ShouldLoadStatus || force ? "Loading contributions..." : "Skip contributions reload");
        if (ShouldLoadContributions || force)
        {
            ContributionsLoading = true;
            await LoadContributions();
            ContributionsLoading = false;
            Console.WriteLine("Contributions reloaded - ");
        }

        await SpreadExtraContributions();

        return _personProgressItems;
    }

    private Task SetProgressItems()
    {
        _personProgressItems = _personProgressItems.Where(x => _statuses.ContainsKey(x.PersonId)).ToList();
        if (_statuses.Count == 0)
            return Task.CompletedTask;
        foreach (var status in _statuses)
        {
            var entry = _personProgressItems.FirstOrDefault(x => x.PersonId == status.Value.PersonId) ?? new();
            _personProgressItems.Remove(entry);
            
            var transactions = status.Value.TransactionAmount;
            var target = status.Value.TargetAmount;
            
            entry.TotalTarget = target;
            entry.ActualContributed = transactions;
            
            entry.ActualStatusPercentage = (transactions / target) * 100;
            if(entry.ActualStatusPercentage < (decimal)0.1)
                entry.ActualStatusPercentage = (decimal)0.1;
            
            entry.RemainingNOKForAjour = target*Globals.OnTrackProsentDesimal - transactions;
            if (entry.RemainingNOKForAjour < 0)
                entry.RemainingNOKForAjour = 0;
            entry.PersonId = status.Value.PersonId;
            _personProgressItems.Add(entry);
        }
        _personProgressItems.Sort((x, y) => x.RemainingNOKForAjour.CompareTo(y.RemainingNOKForAjour));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Fetches a list of integers from the remote endpoint
    /// and resets any existing progress calculations.
    /// </summary>
    public async Task LoadTargets()
    {
        httpClient.DefaultRequestHeaders.Authorization 
            = new AuthenticationHeaderValue("Bearer", _bearer);
        try
        {
            var url = BaseUrl + $"/ClubProposals/?clubSeasonId={_options.SeasonId}&clubId={_options.ClubId}&teamId={_options.TeamId}";
            var targets = await httpClient.GetFromJsonAsync<NewApiResponse<PersonContribution>>(url);
            if (targets?.StatusCode != 200)
                return;
            var validTargets = targets?.Data?
                .Where(x => x.Status == 2)
                .Where(x => x.PersonId is not null && !_options.ExcludedPersonIds.Contains(x.PersonId.Value))
                .Where(x => x.TotalAcceptedAmount > 0)
                .Where(x => x.ClubTargetPriority > 0)
                .OrderByDescending(x => x.AcceptedDate)
                .GroupBy(x => x.PersonId) ?? [];
            

            _targets = validTargets.ToDictionary(x => x.Key ?? throw new Exception("FEIL I KEY"), x => x.First());
            var invalidTargets = targets?.Data?.Where(x => _targets.TryGetValue(x.PersonId ?? -1, out _) is false).ToArray(); 
            LastTargetLoad = DateTime.Now;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching data: {ex.Message}");
        }
    }
    private async Task LoadStatus()
    {
        try
        {
            var url = BaseUrl + $"/ClubMembersStatus/?clubSeasonId={_options.SeasonId}&clubId={_options.ClubId}";
            var statuses = await httpClient.GetFromJsonAsync<NewApiResponse<PersonStatus>>(url);
            if(statuses?.Data?.Count is not > 0)
                return;

            var notCounted = statuses.Data.Where(x => !x.IsCountedInAksjon).Select(x => x.PersonId);
            foreach (var key in notCounted)
            {
                //_targets.Remove(key);
                //_statuses.Remove(key);
            }
            
            _statuses = statuses.Data
                .Where(x => _targets.ContainsKey(x.PersonId))
                .Where(x => x.IsCountedInAksjon)
                .GroupBy(x => x.PersonId).ToDictionary(x => x.Key, x => x.First());
            
            LastStatusLoad = DateTime.Now;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching data: {ex.Message}");
        }
    }

    private Task SpreadExtraContributions()
    {
        foreach (var item in _personProgressItems)
            item.BoostedNOK = 0;
        
        if(BonusContributions <= 0)
            return Task.CompletedTask;

        var remainingBoost = BonusContributions;
        if (!MaximizeBoost)
        {
            var amountOfNonAjour = _personProgressItems.Count(x => !x.Ajour);
            foreach (var item in _personProgressItems.Where(item => !item.Ajour))
            {
                var amountForEach = remainingBoost / amountOfNonAjour;
                var amountForItem = amountForEach > item.RemainingNOKForAjour ? item.RemainingNOKForAjour : amountForEach;
                item.BoostedNOK = amountForItem;

                remainingBoost -= amountForItem;
                amountOfNonAjour--;
            }
            
            return Task.CompletedTask;
        }
        
        _personProgressItems = _personProgressItems.OrderBy(x => x.RemainingNOKForAjour).ToList();
        foreach (var item in _personProgressItems.Where(item => !item.Ajour))
        {
            if (remainingBoost < 1)
                return Task.CompletedTask;
            if (remainingBoost > item.RemainingNOKForAjour)
            {
                item.BoostedNOK = item.RemainingNOKForAjour;
                remainingBoost -= item.RemainingNOKForAjour;
            }
            else
            {
                item.BoostedNOK = remainingBoost;
                remainingBoost = 0;
            }
        }

        return Task.CompletedTask;
    }
    private async Task LoadContributions()
    {
        try
        {
            httpClient.DefaultRequestHeaders.Authorization 
                = new AuthenticationHeaderValue("Bearer", _bearer);
            var response = await httpClient.GetFromJsonAsync<NewApiResponse<Transaction>>(BaseUrl + $"/ClubTransactions/?clubId={_options.ClubId}&fromDate={QueryDate(_options.TransactionsFrom)}&toDate={QueryDate(_options.TransactionsTo)}");
            if (response?.Data?.Count is not > 0)
            {
                Console.WriteLine("Error fetching data");
                return;
            }

            var nonExisting = response.Data
                .Where(x => x.Description?.Contains(_options.DonationDescription) ?? false)
                .Where(x => x.UserId is not null)
                .Where(x => !_options.PreviouslyActiveUserIds.Contains(x.UserId ?? 0))
                .Where(x => !_targets.ContainsKey(x.UserId!.Value));
            
            var sum = nonExisting.Sum(x => x.Amount);
            ActualBonusContributions = sum;
            
            LastContributionLoad = DateTime.Now;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching data: {ex.Message}");
        }
    }


    private static string QueryDate(DateTime date) =>
        Uri.EscapeDataString(date.ToString("yyyy-MM-dd H:mm:ss", CultureInfo.InvariantCulture));
}
