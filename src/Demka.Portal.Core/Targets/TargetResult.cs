namespace Demka.Portal.Core.Targets;

public sealed class OutletTargetResult
{
    public int OutletId { get; init; }
    public string Region { get; init; } = "";
    public decimal[] QuarterAverages { get; init; } = new decimal[3];
    public decimal PeakQuarterAverage { get; init; }
    public decimal BaselinePerMonth { get; init; }
    public decimal Growth { get; init; }
    public bool Recovery { get; init; }
    public decimal RecoveryUpliftPerMonth { get; init; }
    /// <summary>Baseline × number of target months (the run-rate value of the period).</summary>
    public decimal BaselinePeriod { get; init; }
    public decimal BottomUp { get; init; }
    public decimal RegionScale { get; set; }
    public decimal Target { get; set; }
    public decimal[] MonthlyTargets { get; set; } = [];
}

public sealed class RegionTargetResult
{
    public string Region { get; init; } = "";
    public int CurrentMerchandisers { get; init; }
    public decimal BaselinePeriod { get; init; }
    public decimal BottomUpOutlets { get; init; }
    public decimal Expansion { get; init; }
    public decimal BottomUpTotal => BottomUpOutlets + Expansion;
    public decimal Target { get; init; }
    public decimal Scale { get; init; }
    public decimal[] MonthlyTargets { get; init; } = [];
}

public sealed class TargetCalculation
{
    public IReadOnlyList<OutletTargetResult> Outlets { get; init; } = [];
    public IReadOnlyList<RegionTargetResult> Regions { get; init; } = [];
    public decimal[] PhasingWeights { get; init; } = [];
    public decimal Total => Regions.Sum(r => r.Target);
    public decimal BottomUpTotal => Regions.Sum(r => r.BottomUpTotal);
}
