using Microsoft.AspNetCore.Components;
using NetCoreAudio;
using PACEFEST.Models;

namespace PACEFEST.Pages;

public partial class PACEFESTIndex : ComponentBase
{
    private Timer? _autoLoadTimer; 
    private int _selectedAmount = 0; // Default value
    private decimal? _totalMissing = 0; // Default value
    private int? _previousAjourCount = null;

    private bool MaximizeBoost
    {
        get => ProgressService.MaximizeBoost;
        set
        {
            ProgressService.MaximizeBoost = value;
            StateHasChanged();
        }
    }

    private bool _enableSound;

    private bool EnableSound
    {
        get => _enableSound;
        set
        {
            _enableSound = value;
            _player.Stop();
        }
    }

    private int BUKStatus = 0;
    private int SØRLANDETStatus = 0;
    
    public ProgressItem[] ProgressItems = [];

    protected override async Task OnInitializedAsync()
    {
        _autoLoadTimer = new Timer(TimerCallback, null, TimeSpan.Zero, TimeSpan.FromSeconds(10));
        await base.OnInitializedAsync();
    }

    private void TimerCallback(object? state)
    {
        Task.Run(() => InvokeAsync(async () =>
        {
            await LoadData();
            StateHasChanged();
        }));
    }

    private async Task OnSliderReleased()
    {
        await LoadData();
    }

    private readonly Player _player = new();
    private DateTime? _lastLoad;

    private async Task LoadData(bool force = false)
    {
        if (_lastLoad.HasValue && _lastLoad > DateTime.Now.AddSeconds(-1))
            return;
        _lastLoad = DateTime.Now;
        Console.WriteLine($"Loading data...      {DateTime.Now.ToShortTimeString()}");
        
        var result = await ProgressService.Refresh(force);
        ProgressItems = result.OrderBy(x => x.PercentageWithBoost).ToArray();
        
        var ajourCount = ProgressItems.Count(x => x.PercentageWithBoost >= Globals.OnTrackProsent);
        if (_previousAjourCount > 0 && _previousAjourCount < ajourCount) 
            await PlaySound();
        
        _previousAjourCount = ajourCount;
        var percentage = (decimal)ajourCount/ProgressItems.Length;
        
        BUKStatus = (int)Math.Round(percentage*100,1, MidpointRounding.AwayFromZero);
        SØRLANDETStatus = (int)Math.Round((percentage*100+ Globals.SamvirkProsent)/2);
        _totalMissing = ProgressItems.Sum(x => x.RemainingNOKForAjour - x.BoostedNOK);
        StateHasChanged();
    }

    private async Task PlaySound()
    {
        if (_player.Playing || !EnableSound)
            return;
        await _player.SetVolume(50);
        await _player.Play("wwwroot/Sound/NyAjour.mp3");
    }
}