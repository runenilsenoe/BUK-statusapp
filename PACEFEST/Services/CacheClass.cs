namespace PACEFEST.Services;

public class CacheClass
{
    
    protected DateTime LastTargetLoad { get; set; }
    protected DateTime LastStatusLoad { get; set; }
    protected DateTime LastContributionLoad { get; set; }
    
    public bool ContributionsLoading { get; protected set; }
    public bool StatusesLoading { get; protected set; }
    public bool TargetsLoading { get; protected set; }
    
    public static TimeSpan StatusInterval = TimeSpan.FromMinutes(1);
    public static TimeSpan ContributionsInterval = TimeSpan.FromSeconds(30);
    public static TimeSpan TargetInterval = TimeSpan.FromMinutes(5);
    
    public bool ShouldLoadStatus => DateTime.Now - LastStatusLoad > StatusInterval;
    public bool ShouldLoadContributions => DateTime.Now - LastContributionLoad > ContributionsInterval;
    public bool ShouldLoadTargets => DateTime.Now - LastTargetLoad > TargetInterval;
}