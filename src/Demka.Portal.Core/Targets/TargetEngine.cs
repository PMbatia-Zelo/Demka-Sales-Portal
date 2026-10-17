namespace Demka.Portal.Core.Targets;

/// <summary>
/// Calculates value targets per outlet, region and month.
/// A direct port of the Q4 2026 targets workbook (Outlet Targets, Region Targets and Assumptions sheets);
/// the regression test in Demka.Portal.Tests checks it against the workbook's own results.
/// </summary>
public static class TargetEngine
{
    /// <param name="outlets">Every outlet, in or out of scope.</param>
    /// <param name="assumptions">The cycle's assumptions.</param>
    /// <param name="currentMerchandisersByRegion">
    /// Merchandisers holding at least one outlet, per region. Used to size the new-hire expansion.
    /// </param>
    public static TargetCalculation Calculate(
        IEnumerable<OutletInput> outlets,
        TargetAssumptions assumptions,
        IReadOnlyDictionary<string, int> currentMerchandisersByRegion)
    {
        var errors = assumptions.Validate();
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));

        var months = assumptions.Phasing.Count;
        var weights = PhasingWeights(assumptions.Phasing);

        var outletResults = outlets.Select(o => CalculateOutlet(o, assumptions, months)).ToList();

        var regionResults = new List<RegionTargetResult>();
        foreach (var (region, ra) in assumptions.Regions)
        {
            var inRegion = outletResults.Where(o => string.Equals(o.Region, region, StringComparison.OrdinalIgnoreCase)).ToList();
            var baselinePeriod = inRegion.Sum(o => o.BaselinePeriod);
            var bottomUp = inRegion.Sum(o => o.BottomUp);
            var merchandisers = currentMerchandisersByRegion.TryGetValue(region, out var n) ? n : 0;

            // Workbook: hires × months active × ramp-up × (regional baseline per month ÷ current merchandisers)
            var avgPerMerchandiser = merchandisers > 0 ? baselinePeriod / months / merchandisers : 0m;
            var expansion = ra.PlannedNewMerchandisers * ra.NewHireMonthsActive * ra.NewHireRampUp * avgPerMerchandiser;

            var target = assumptions.Mode == TargetMode.TopDown && assumptions.CompanyTarget is { } company
                ? company * ra.TopDownAllocation
                : bottomUp + expansion;
            var scale = bottomUp > 0 ? Math.Max(0m, (target - expansion) / bottomUp) : 1m;

            foreach (var o in inRegion)
            {
                o.RegionScale = scale;
                o.Target = o.BottomUp * scale;
                o.MonthlyTargets = weights.Select(w => o.Target * w).ToArray();
            }

            regionResults.Add(new RegionTargetResult
            {
                Region = region,
                CurrentMerchandisers = merchandisers,
                BaselinePeriod = baselinePeriod,
                BottomUpOutlets = bottomUp,
                Expansion = expansion,
                Target = target,
                Scale = scale,
                MonthlyTargets = weights.Select(w => target * w).ToArray()
            });
        }

        // Outlets in a region with no assumptions (e.g. "Unallocated") carry no target.
        foreach (var o in outletResults.Where(o => !assumptions.Regions.ContainsKey(o.Region)))
        {
            o.RegionScale = 0m;
            o.Target = 0m;
            o.MonthlyTargets = new decimal[months];
        }

        return new TargetCalculation { Outlets = outletResults, Regions = regionResults, PhasingWeights = weights };
    }

    /// <summary>Month weight = days × plan per day × seasonal adjustment, normalised to total 1.</summary>
    public static decimal[] PhasingWeights(IReadOnlyList<PhasingMonth> phasing)
    {
        var raw = phasing.Select(p => p.Days * p.PlanPerDay * p.SeasonalAdjustment).ToArray();
        var total = raw.Sum();
        return total == 0 ? raw.Select(_ => 1m / raw.Length).ToArray() : raw.Select(r => r / total).ToArray();
    }

    private static OutletTargetResult CalculateOutlet(OutletInput o, TargetAssumptions a, int months)
    {
        if (o.BaselineMonths.Length != 9)
            throw new ArgumentException($"Outlet {o.OutletId}: nine baseline months are required.");

        var m = (decimal[])o.BaselineMonths.Clone();
        m[8] *= a.LastMonthCompletionFactor;

        var q = new[] { (m[0] + m[1] + m[2]) / 3m, (m[3] + m[4] + m[5]) / 3m, (m[6] + m[7] + m[8]) / 3m };
        var peak = Math.Max(q[0], q[1]); // best of the two earlier quarters
        var weighted = a.BaselineWeights[0] * q[0] + a.BaselineWeights[1] * q[1] + a.BaselineWeights[2] * q[2];

        decimal baseline;
        if (!o.InScope) baseline = 0m;
        else if (o.BaselineOverride is { } ovr) baseline = ovr;
        else baseline = o.Status switch
        {
            OutletStatus.Active => weighted,
            OutletStatus.New => NewOutletBaseline(m),
            OutletStatus.Ceased => a.CeasedOutlets == CeasedOutletRule.Include ? weighted : 0m,
            _ => 0m
        };

        var growth = o.HasHolder
            ? (a.Regions.TryGetValue(o.Region, out var ra) ? ra.Growth : 0m)
            : a.NoMerchandiserGrowth;

        // Recovery: a held outlet whose best earlier quarter was strong but whose latest quarter fell well below it.
        var recovery = o.HasHolder
                       && o.BaselineOverride is null
                       && baseline > 0
                       && peak >= a.RecoveryMinPeakPerMonth
                       && q[2] < peak * (1 - a.RecoveryDropBelowPeak)
                       && peak > baseline;
        var uplift = recovery ? a.RecoveryShareOfGap * (peak - baseline) : 0m;

        return new OutletTargetResult
        {
            OutletId = o.OutletId,
            Region = o.Region,
            QuarterAverages = q,
            PeakQuarterAverage = peak,
            BaselinePerMonth = baseline,
            Growth = growth,
            Recovery = recovery,
            RecoveryUpliftPerMonth = uplift,
            BaselinePeriod = baseline * months,
            BottomUp = (baseline * (1 + growth) + uplift) * months
        };
    }

    /// <summary>New outlets: total of the latest quarter divided by the months in which they actually traded.</summary>
    private static decimal NewOutletBaseline(decimal[] m)
    {
        var latest = new[] { m[6], m[7], m[8] };
        var traded = latest.Count(v => v > 0);
        return traded == 0 ? 0m : latest.Sum() / traded;
    }
}
