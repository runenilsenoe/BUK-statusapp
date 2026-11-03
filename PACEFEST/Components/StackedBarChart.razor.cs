using Microsoft.AspNetCore.Components;
using PACEFEST.Models;

namespace PACEFEST.Components;

public partial class StackedBarChart : ComponentBase
{
    /// <summary>
    /// A list of items to draw (one stacked bar per item).
    /// </summary>
    [Parameter]
    public ProgressItem[] Data { get; set; } = [];

    /// <summary>
    /// Total height of each bar (in px).
    /// </summary>
    [Parameter]
    public double BarHeightPx { get; set; } = 500;

    /// <summary>
    /// Width of each bar (in px).
    /// </summary>
    [Parameter]
    public double BarWidthPx { get; set; } = 25;
}