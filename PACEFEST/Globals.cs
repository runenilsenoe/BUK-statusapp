namespace PACEFEST;

/// <summary>Aksjon thresholds, set from the "Aksjon" configuration section at startup (see Program.cs).</summary>
public static class Globals
{
    public static DateTime StartDato { get; set; }
    public static int OnTrackProsent { get; set; }
    public static decimal OnTrackProsentDesimal => OnTrackProsent / 100m;
    public static int SamvirkProsent { get; set; }

    public static void Load(IConfiguration configuration)
    {
        var section = configuration.GetSection("Aksjon");
        StartDato = section.GetValue<DateTime>("StartDato");
        OnTrackProsent = section.GetValue<int>("OnTrackProsent");
        SamvirkProsent = section.GetValue<int>("SamvirkProsent");
        if (OnTrackProsent is <= 0 or > 100)
            throw new InvalidOperationException("Aksjon:OnTrackProsent must be set (1-100).");
    }
}
