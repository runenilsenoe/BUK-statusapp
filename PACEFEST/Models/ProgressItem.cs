namespace PACEFEST.Models;
public class ProgressItem
{
    /// <summary>
    /// A direct percentage (0–100) representing the current status.
    /// </summary>
    public decimal ActualStatusPercentage { get; set; }

    /// <summary>
    /// A direct percentage (0–100) representing the current status.
    /// </summary>
    public decimal PercentageWithBoost => ((BoostedNOK + ActualContributed) / TotalTarget) * 100;
    /// <summary>
    /// How many NOK is contributed
    /// </summary>
    public decimal ActualContributed { get; set; }
    
    /// <summary>
    /// How many NOK remain for AJOUR status.
    /// </summary>
    public decimal RemainingNOKForAjour { get; set; }
    
    /// <summary>
    /// How many NOK is total target
    /// </summary>
    public decimal TotalTarget { get; set; }

    /// <summary>
    /// An extra “boost” amount in NOK that extends the actual status.
    /// </summary>
    public decimal BoostedNOK { get; set; }
    public int PersonId { get; set; }
    public bool Ajour => ActualStatusPercentage > Globals.OnTrackProsent;
}