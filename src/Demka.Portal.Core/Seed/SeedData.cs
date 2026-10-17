using System.Text.Json;
using System.Text.Json.Serialization;
using Demka.Portal.Core.Targets;

namespace Demka.Portal.Core.Seed;

/// <summary>
/// Starting data exported from the Q4 2026 targets workbook (Base Data and Merchandiser Targets sheets).
/// Used once to fill an empty database, and by the regression tests.
/// </summary>
public sealed class SeedFile
{
    public List<SeedOutlet> Outlets { get; set; } = [];
    public List<SeedUser> Users { get; set; } = [];
}

public sealed class SeedOutlet
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Chain { get; set; } = "";
    public string Region { get; set; } = "";
    public string? RegionBasis { get; set; }
    /// <summary>Jan–Sep 2026, KES incl. VAT; September provisional.</summary>
    public decimal[] Monthly { get; set; } = [];
    public bool HqCreditAccount { get; set; }
    /// <summary>Target holder's name, or null when unassigned.</summary>
    public string? Holder { get; set; }
    public string? HolderType { get; set; }
    public string? OtherVisitors { get; set; }
    public int Visits { get; set; }
    public string? Route { get; set; }
    public string? RouteSource { get; set; }
    public bool Trading { get; set; }
    public bool InScope { get; set; }
    public string Status { get; set; } = "";
    public decimal? BaselineOverride { get; set; }

    public OutletStatus ParsedStatus => Status switch
    {
        "Active" => OutletStatus.Active,
        "New" => OutletStatus.New,
        "Ceased" => OutletStatus.Ceased,
        "Not trading" => OutletStatus.NotTrading,
        _ => throw new InvalidDataException($"Outlet {Id}: unknown status '{Status}'.")
    };
}

public sealed class SeedUser
{
    public string Name { get; set; } = "";
    public string? Region { get; set; }
    /// <summary>Merchandiser or TeamLeader.</summary>
    public string Role { get; set; } = "";
}

public sealed class SeedAssumptionsFile
{
    public string Cycle { get; set; } = "";
    public List<string> BaselineMonths { get; set; } = [];
    public string? Source { get; set; }
    public TargetAssumptions Assumptions { get; set; } = new();
}

public static class SeedLoader
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true
    };

    public static SeedFile LoadSeed(string path) =>
        JsonSerializer.Deserialize<SeedFile>(File.ReadAllText(path), Json)
        ?? throw new InvalidDataException($"Could not read seed file {path}.");

    public static SeedAssumptionsFile LoadAssumptions(string path) =>
        JsonSerializer.Deserialize<SeedAssumptionsFile>(File.ReadAllText(path), Json)
        ?? throw new InvalidDataException($"Could not read assumptions file {path}.");

    /// <summary>Turns seed outlets into engine inputs.</summary>
    public static List<OutletInput> ToEngineInputs(SeedFile seed) =>
        seed.Outlets.Select(o => new OutletInput
        {
            OutletId = o.Id,
            Region = o.Region,
            Status = o.ParsedStatus,
            InScope = o.InScope,
            HasHolder = o.Holder is not null,
            BaselineMonths = o.Monthly,
            BaselineOverride = o.BaselineOverride
        }).ToList();

    /// <summary>Merchandisers (not team leaders) per region, as the workbook counts them.</summary>
    public static Dictionary<string, int> MerchandisersByRegion(SeedFile seed) =>
        seed.Users.Where(u => u.Role == "Merchandiser" && u.Region is not null)
            .GroupBy(u => u.Region!)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
}
