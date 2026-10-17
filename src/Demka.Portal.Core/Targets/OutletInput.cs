namespace Demka.Portal.Core.Targets;

public enum OutletStatus
{
    Active,
    New,
    Ceased,
    NotTrading
}

/// <summary>One outlet as the engine sees it.</summary>
public sealed class OutletInput
{
    public int OutletId { get; init; }
    public string Region { get; init; } = "";
    public OutletStatus Status { get; init; }

    /// <summary>False for HQ credit accounts and other outlets that never carry a target.</summary>
    public bool InScope { get; init; } = true;

    /// <summary>True when a merchandiser or team leader holds the outlet; false for "Unassigned - Region".</summary>
    public bool HasHolder { get; init; }

    /// <summary>
    /// Nine monthly sales values (KES incl. VAT) for the three baseline quarters, oldest first.
    /// The last month may be provisional; the completion factor is applied by the engine.
    /// </summary>
    public decimal[] BaselineMonths { get; init; } = new decimal[9];

    /// <summary>Monthly baseline set by the national team, replacing the calculated one.</summary>
    public decimal? BaselineOverride { get; init; }
}
