namespace Demka.Portal.Core.Targets;

/// <summary>How the company total is formed.</summary>
public enum TargetMode
{
    /// <summary>Sum of outlet targets plus new-hire expansion.</summary>
    BottomUp,
    /// <summary>Company target split by regional allocation %, outlets scaled to fit.</summary>
    TopDown
}

/// <summary>What happens to outlets with no sales in the last two baseline months.</summary>
public enum CeasedOutletRule
{
    Exclude,
    Include
}

/// <summary>
/// Every input of the target engine. Mirrors the yellow cells on the Assumptions sheet of
/// Demka_MT_Merchandiser_Q4_2026_Value_Targets.xlsx. Values are fractions (0.15 = 15%) and KES incl. VAT.
/// </summary>
public sealed class TargetAssumptions
{
    public TargetMode Mode { get; set; } = TargetMode.BottomUp;

    /// <summary>Company target for the period (Top-down only). Null = use the bottom-up total.</summary>
    public decimal? CompanyTarget { get; set; }

    /// <summary>Weights on the monthly averages of the three baseline quarters (oldest first). Must total 1.</summary>
    public decimal[] BaselineWeights { get; set; } = [0m, 0.25m, 0.75m];

    /// <summary>Multiplier on the last baseline month when it is provisional (e.g. 0.95 for September).</summary>
    public decimal LastMonthCompletionFactor { get; set; } = 1m;

    /// <summary>Growth for outlets that have no merchandiser (replaces the regional factor).</summary>
    public decimal NoMerchandiserGrowth { get; set; }

    public decimal RecoveryMinPeakPerMonth { get; set; } = 250_000m;
    public decimal RecoveryDropBelowPeak { get; set; } = 0.15m;
    public decimal RecoveryShareOfGap { get; set; } = 0.5m;

    public CeasedOutletRule CeasedOutlets { get; set; } = CeasedOutletRule.Exclude;

    public decimal VatRate { get; set; } = 0.16m;

    /// <summary>Per-region settings, keyed by region name (Western, Nairobi, Central, Coast).</summary>
    public Dictionary<string, RegionAssumptions> Regions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Target months in order, with the production plan that sets the phasing.</summary>
    public List<PhasingMonth> Phasing { get; set; } = [];

    /// <summary>Returns the problems that block a calculation; empty when the assumptions are usable.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (BaselineWeights.Length != 3) errors.Add("Baseline weights need exactly three values (one per quarter).");
        else if (Math.Abs(BaselineWeights.Sum() - 1m) > 0.0001m) errors.Add("Baseline weights must total 100%.");
        if (Regions.Count == 0) errors.Add("At least one region is required.");
        if (Phasing.Count == 0) errors.Add("At least one target month is required.");
        if (Phasing.Any(p => p.Days <= 0 || p.PlanPerDay < 0 || p.SeasonalAdjustment < 0))
            errors.Add("Phasing months need positive days and non-negative plan and seasonal values.");
        if (Mode == TargetMode.TopDown && CompanyTarget is not null &&
            Math.Abs(Regions.Values.Sum(r => r.TopDownAllocation) - 1m) > 0.0001m)
            errors.Add("Top-down allocation must total 100%.");
        return errors;
    }
}

public sealed class RegionAssumptions
{
    /// <summary>Growth on baseline for merchandiser-held outlets.</summary>
    public decimal Growth { get; set; }

    /// <summary>Share of the company target (Top-down only).</summary>
    public decimal TopDownAllocation { get; set; }

    public int PlannedNewMerchandisers { get; set; }
    public decimal NewHireMonthsActive { get; set; }
    public decimal NewHireRampUp { get; set; } = 0.5m;
}

public sealed class PhasingMonth
{
    public string Name { get; set; } = "";
    public int Days { get; set; }
    /// <summary>Yoghurt production plan, '000 litres per day.</summary>
    public decimal PlanPerDay { get; set; }
    public decimal SeasonalAdjustment { get; set; } = 1m;
}
