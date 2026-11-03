using System.Globalization;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using MyBlazorProgressApp.Models;

namespace MyBlazorProgressApp.Services;

public class MyshareService(HttpClient httpClient, IOptions<MyshareOptions> options)
{
    private readonly MyshareOptions _options = options.Value;
    
    public bool IsTargetsLoaded { get; private set; }
    public DateTime LastStatusLoad { get; set; }
    public int CurrentProgress { get; private set; }
    public int ExtraProgress { get; private set; }
    private Dictionary<int,PersonContribution> _targets = new();
    private Dictionary<int,ScoreResponse> _statuses = new();

    private string _newBearer => _options.Bearer;
    private string _bearer => _options.ContributionsBearer;
    private string BaseUrl => _options.ContributionsBaseUrl.TrimEnd('/');
    private string NewBaseUrl => _options.BaseUrl.TrimEnd('/');
    

    /// <summary>
    /// Fetches a list of integers from the remote endpoint
    /// and resets any existing progress calculations.
    /// </summary>
    public async Task LoadTargets()
    {
        httpClient.DefaultRequestHeaders.Authorization 
            = new AuthenticationHeaderValue("Bearer", _newBearer);
        try
        {
            if (LastStatusLoad.AddMinutes(3)>DateTime.Now)
                return;
            IsTargetsLoaded = false;
            var url = NewBaseUrl + $"/ClubProposals/?clubSeasonId={_options.SeasonId}&clubId={_options.ClubId}&teamId={_options.TeamId}";
            var targets = await httpClient.GetFromJsonAsync<NewApiResponse<PersonContribution>>(url);
            var validTargets = targets?.Data?
                .Where(x => x.Status == 2)
                .Where(x => x.PersonId is not null && !_options.ExcludedPersonIds.Contains(x.PersonId.Value))
                .Where(x => x.TotalAcceptedAmount > 0)
                .Where(x => x.ClubTargetPriority > 0)
                .OrderByDescending(x => x.AcceptedDate)
                .GroupBy(x => x.PersonId) ?? [];

            _targets = validTargets.ToDictionary(x => x.Key ?? 0, x => x.First());
            IsTargetsLoaded = _targets.Count > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching data: {ex.Message}");
            IsTargetsLoaded = false;
        }
    }

    private async Task LoadStatus()
    {
        if (LastStatusLoad.AddSeconds(10)>DateTime.Now)
            return;
        httpClient.DefaultRequestHeaders.Authorization 
            = new AuthenticationHeaderValue("Bearer", _bearer);
        await Task.WhenAll(_targets.Keys.Select(LoadStatus));
        LastStatusLoad = DateTime.Now;
    }
    private async Task LoadStatus(int id)
    {
        try
        {
            var response = await httpClient.GetFromJsonAsync<ScoreResponse>(BaseUrl + $"/memberStatus?personId={id}");
            if (response is null)
                Console.WriteLine("Error fetching data");
            if(response?.PersonID is not null)
                _statuses[response.PersonID.Value] = response;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching data: {ex.Message}");
        }
    }

    public async Task<decimal> GetMissingFromStatuses(int overrideFakeValue)
    {
        await LoadStatus();
        var remainders = _statuses.Where(x => x.Value.TotalContribution < x.Value.AjourTarget).ToArray();
        var remainingSums = remainders.Select(x => x.Value.AjourTarget - x.Value.TotalContribution ?? 0).ToList();
        remainingSums = remainingSums.Order().ToList();
        var allStatusCount = _statuses.Count != 0 ? (decimal)_statuses.Count : 1;
        var notAjourCount = (decimal)remainingSums.Count;
        while(!IsTargetsLoaded || _targets.Count <= 0)
            await Task.Delay(100);
        
        var ajourCount = allStatusCount-notAjourCount;
        var percentage = ajourCount / allStatusCount * 100;
        
        var extra = await GetContributionsOutsideTargets();
        if (extra > remainingSums.Sum())
            overrideFakeValue = 0;
        else if (extra + overrideFakeValue > remainingSums.Sum())
            overrideFakeValue = (int) (remainingSums.Sum() - extra);

        extra += overrideFakeValue + 5;
        while (extra > 10 && extra > remainingSums.FirstOrDefault())
        {
            extra -= remainingSums.FirstOrDefault();
            remainingSums.RemoveAt(0);
        }
        var percentageBuk = ((_statuses.Count-remainingSums.Count) / (decimal)_statuses.Count) * 100m;
        CurrentProgress = (int)percentage;
        ExtraProgress = (int)(percentageBuk - percentage);
        return remainingSums.Sum();
    }
    public async Task<decimal> GetContributionsOutsideTargets()
    {
        try
        {
            httpClient.DefaultRequestHeaders.Authorization 
                = new AuthenticationHeaderValue("Bearer", _newBearer);
            var response = await httpClient.GetFromJsonAsync<NewApiResponse<Transaction>>(NewBaseUrl + $"/ClubTransactions/?clubId={_options.ClubId}&fromDate={QueryDate(_options.TransactionsFrom)}&toDate={QueryDate(_options.TransactionsTo)}");
            if (response?.Data?.Count is not > 0)
            {
                Console.WriteLine("Error fetching data");
                return 0;
            }
            var nonExisting = response.Data
                .Where(x => x.Description?.Contains(_options.DonationDescription) ?? false)
                .Where(x => x.UserId is not null)
                .Where(x => !_targets.ContainsKey(x.UserId!.Value));
            
            var sum = nonExisting.Sum(x => x.Amount);
            BonusContributions = sum;
            return sum ?? 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching data: {ex.Message}");
        }
        return 0;
    }

    public decimal? BonusContributions { get; set; }

    public void Clear()
    {
        _targets.Clear();
        IsTargetsLoaded = false;
    }

    private static string QueryDate(DateTime date) =>
        Uri.EscapeDataString(date.ToString("yyyy-MM-dd H:mm:ss", CultureInfo.InvariantCulture));
}
