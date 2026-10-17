using System.Text.Json;
using Demka.Portal.Core.Seed;
using Demka.Portal.Core.Targets;

namespace Demka.Portal.Tests;

/// <summary>
/// Regression tests: the engine must reproduce Demka_MT_Merchandiser_Q4_2026_Value_Targets.xlsx exactly.
/// Expected values in Fixtures/q4-2026-expected.json were exported from the workbook's own results.
/// </summary>
public class TargetEngineWorkbookTests
{
    private static readonly string Dir = AppContext.BaseDirectory;
    private static readonly SeedFile Seed = SeedLoader.LoadSeed(Path.Combine(Dir, "seed", "q4-2026", "seed.json"));
    private static readonly SeedAssumptionsFile Assumptions = SeedLoader.LoadAssumptions(Path.Combine(Dir, "seed", "q4-2026", "assumptions.json"));
    private static readonly JsonElement Expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir, "Fixtures", "q4-2026-expected.json"))).RootElement;

    private static TargetCalculation Run(TargetAssumptions? a = null) =>
        TargetEngine.Calculate(SeedLoader.ToEngineInputs(Seed), a ?? Assumptions.Assumptions, SeedLoader.MerchandisersByRegion(Seed));

    [Fact]
    public void Company_total_matches_workbook_437_3M()
    {
        var result = Run();
        Assert.Equal(437_253_549.66m, Math.Round(result.Total, 2));
    }

    [Theory]
    [InlineData("Western")]
    [InlineData("Nairobi")]
    [InlineData("Central")]
    [InlineData("Coast")]
    public void Region_targets_and_expansion_match_workbook(string region)
    {
        var r = Run().Regions.Single(x => x.Region == region);
        var e = Expected.GetProperty("regions").GetProperty(region);
        Assert.Equal(e.GetProperty("q4Target").GetDecimal(), r.Target, 2);
        Assert.Equal(e.GetProperty("expansion").GetDecimal(), r.Expansion, 2);
        Assert.Equal(e.GetProperty("bottomUpOutlets").GetDecimal(), r.BottomUpOutlets, 2);
        Assert.Equal(e.GetProperty("oct").GetDecimal(), r.MonthlyTargets[0], 2);
        Assert.Equal(e.GetProperty("nov").GetDecimal(), r.MonthlyTargets[1], 2);
        Assert.Equal(e.GetProperty("dec").GetDecimal(), r.MonthlyTargets[2], 2);
    }

    [Fact]
    public void Every_outlet_matches_workbook_to_the_cent()
    {
        var byId = Run().Outlets.ToDictionary(o => o.OutletId);
        var mismatches = new List<string>();
        foreach (var e in Expected.GetProperty("outlets").EnumerateArray())
        {
            var o = byId[e.GetProperty("id").GetInt32()];
            void Check(string field, decimal actual, decimal expected)
            {
                if (Math.Abs(actual - expected) > 0.01m) mismatches.Add($"Outlet {o.OutletId} {field}: {actual:N2} vs {expected:N2}");
            }
            Check("baseline", o.BaselinePerMonth, e.GetProperty("baseline").GetDecimal());
            Check("uplift", o.RecoveryUpliftPerMonth, e.GetProperty("uplift").GetDecimal());
            Check("Q4 target", o.Target, e.GetProperty("q4").GetDecimal());
            Check("Oct", o.MonthlyTargets[0], e.GetProperty("oct").GetDecimal());
            Check("Nov", o.MonthlyTargets[1], e.GetProperty("nov").GetDecimal());
            Check("Dec", o.MonthlyTargets[2], e.GetProperty("dec").GetDecimal());
        }
        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches.Take(20)));
    }

    [Fact]
    public void Merchandiser_counts_match_workbook()
    {
        var counts = SeedLoader.MerchandisersByRegion(Seed);
        Assert.Equal(21, counts["Western"]);
        Assert.Equal(20, counts["Nairobi"]);
        Assert.Equal(9, counts["Central"]);
        Assert.Equal(4, counts["Coast"]);
    }
}

/// <summary>Behaviour of individual rules, independent of the workbook data.</summary>
public class TargetEngineRuleTests
{
    private static TargetAssumptions Basic() => new()
    {
        BaselineWeights = [0m, 0.25m, 0.75m],
        NoMerchandiserGrowth = 0m,
        Regions = { ["Coast"] = new RegionAssumptions { Growth = 0.10m } },
        Phasing = [new() { Name = "M1", Days = 30, PlanPerDay = 1 }, new() { Name = "M2", Days = 30, PlanPerDay = 1 }, new() { Name = "M3", Days = 30, PlanPerDay = 2 }]
    };

    private static OutletInput Outlet(decimal[] months, OutletStatus status = OutletStatus.Active, bool held = true, decimal? ovr = null, bool inScope = true) =>
        new() { OutletId = 1, Region = "Coast", Status = status, HasHolder = held, BaselineMonths = months, BaselineOverride = ovr, InScope = inScope };

    private static OutletTargetResult One(OutletInput o, TargetAssumptions? a = null) =>
        TargetEngine.Calculate([o], a ?? Basic(), new Dictionary<string, int> { ["Coast"] = 1 }).Outlets[0];

    [Fact]
    public void Active_outlet_uses_weighted_quarters_and_regional_growth()
    {
        var r = One(Outlet([100, 100, 100, 200, 200, 200, 400, 400, 400]));
        Assert.Equal(350m, r.BaselinePerMonth);              // 25% × 200 + 75% × 400
        Assert.Equal(350m * 1.10m * 3, r.Target);
    }

    [Fact]
    public void Unassigned_outlet_uses_no_merchandiser_growth()
    {
        var r = One(Outlet([0, 0, 0, 0, 0, 0, 100, 100, 100], held: false));
        Assert.Equal(0m, r.Growth);
    }

    [Fact]
    public void Ceased_outlet_gets_no_target_by_default()
    {
        Assert.Equal(0m, One(Outlet([100, 100, 100, 100, 100, 100, 100, 0, 0], OutletStatus.Ceased)).Target);
    }

    [Fact]
    public void Out_of_scope_outlet_gets_no_target()
    {
        Assert.Equal(0m, One(Outlet([100, 100, 100, 100, 100, 100, 100, 100, 100], inScope: false)).Target);
    }

    [Fact]
    public void New_outlet_averages_only_the_months_it_traded()
    {
        Assert.Equal(300m, One(Outlet([0, 0, 0, 0, 0, 0, 0, 200, 400], OutletStatus.New)).BaselinePerMonth);
    }

    [Fact]
    public void Override_replaces_the_baseline_and_blocks_recovery()
    {
        var r = One(Outlet([900_000, 900_000, 900_000, 0, 0, 0, 100, 100, 100], ovr: 0m));
        Assert.Equal(0m, r.BaselinePerMonth);
        Assert.False(r.Recovery);
    }

    [Fact]
    public void Strong_outlet_that_fell_gets_half_the_gap_back()
    {
        // Peak Q1 = 400k/month, Q3 = 200k/month: drop > 15% and peak ≥ 250k.
        var r = One(Outlet([400_000, 400_000, 400_000, 200_000, 200_000, 200_000, 200_000, 200_000, 200_000]));
        Assert.True(r.Recovery);
        Assert.Equal(0.5m * (400_000m - 200_000m), r.RecoveryUpliftPerMonth);
    }

    [Fact]
    public void Phasing_follows_days_times_production_plan()
    {
        var w = TargetEngine.PhasingWeights(Basic().Phasing);
        Assert.Equal(0.25m, w[0]);
        Assert.Equal(0.50m, w[2]);
    }

    [Fact]
    public void Top_down_scales_outlets_to_the_company_target()
    {
        var a = Basic();
        a.Mode = TargetMode.TopDown;
        a.CompanyTarget = 1_000m;
        a.Regions["Coast"].TopDownAllocation = 1m;
        var calc = TargetEngine.Calculate([Outlet([100, 100, 100, 100, 100, 100, 100, 100, 100])], a, new Dictionary<string, int> { ["Coast"] = 1 });
        Assert.Equal(1_000m, calc.Total);
        Assert.Equal(1_000m, calc.Outlets[0].Target, 6);
    }

    [Fact]
    public void Weights_that_do_not_total_100_percent_are_rejected()
    {
        var a = Basic();
        a.BaselineWeights = [0.5m, 0.5m, 0.5m];
        Assert.Throws<InvalidOperationException>(() => One(Outlet(new decimal[9]), a));
    }
}
