namespace Demka.Portal.Web.Data;

// Tables follow the data model in the design document (section "Data model").
// Every number is held at outlet × month grain; all other views are sums over outlets.

public enum UserRole { Merchandiser, TeamLeader, RegionalManager, NationalAdmin }

public sealed class AppUser
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    /// <summary>Sign-in phone number in +2547XXXXXXXX form. Null until loaded at go-live.</summary>
    public string? Phone { get; set; }
    public UserRole Role { get; set; }
    public string? Region { get; set; }
    public int? TeamLeaderId { get; set; }
    public AppUser? TeamLeader { get; set; }
    /// <summary>Hashed PIN (never plain text). Null until the admin issues one.</summary>
    public string? PinHash { get; set; }
    public int FailedPinAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public bool MustChangePin { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Outlet
{
    /// <summary>Outlet ID as used in the targets workbook and outlet mapping.</summary>
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Chain { get; set; } = "";
    public string Region { get; set; } = "";
    public string? RegionBasis { get; set; }
    public bool HqCreditAccount { get; set; }
    /// <summary>False for accounts that never carry a target (e.g. HQ credit accounts).</summary>
    public bool InScope { get; set; } = true;
    /// <summary>Active, New, Ceased or NotTrading.</summary>
    public string Status { get; set; } = "Active";
    public bool IsActive { get; set; } = true;
}

/// <summary>Business Central customer number → outlet (outlet mapping v3.0 plus merges).</summary>
public sealed class CustomerMapping
{
    public int Id { get; set; }
    public string CustomerNo { get; set; } = "";
    public string? CustomerName { get; set; }
    public int? OutletId { get; set; }
    public Outlet? Outlet { get; set; }
    /// <summary>True when the customer is not Modern Trade and should be ignored on upload.</summary>
    public bool Ignore { get; set; }
}

public enum AllocationSource { Seed, Solutech, Admin }

/// <summary>Who holds an outlet's target over a period. A null holder means "Unassigned - Region".</summary>
public sealed class Allocation
{
    public int Id { get; set; }
    public int OutletId { get; set; }
    public Outlet? Outlet { get; set; }
    public int? HolderUserId { get; set; }
    public AppUser? Holder { get; set; }
    public string? Route { get; set; }
    public int VisitsInWindow { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public AllocationSource Source { get; set; }
}

public enum CycleStatus { Draft, Published, Closed }

/// <summary>A target period such as Q4 2026, with its assumptions.</summary>
public sealed class TargetCycle
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>First of the nine baseline months (e.g. 2026-01-01).</summary>
    public DateOnly BaselineStart { get; set; }
    /// <summary>First target month (e.g. 2026-10-01).</summary>
    public DateOnly TargetStart { get; set; }
    public int TargetMonths { get; set; } = 3;
    /// <summary>Draft assumptions as JSON (TargetAssumptions); copied into each published version.</summary>
    public string AssumptionsJson { get; set; } = "{}";
    public CycleStatus Status { get; set; }
    public int PublishedVersion { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int? PublishedByUserId { get; set; }
}

/// <summary>Baseline overrides and other per-outlet exceptions for a cycle.</summary>
public sealed class OutletOverride
{
    public int Id { get; set; }
    public int CycleId { get; set; }
    public int OutletId { get; set; }
    public decimal? BaselinePerMonth { get; set; }
    public string? Note { get; set; }
}

/// <summary>A published version of the targets. Rows in OutletTarget are never changed after publish.</summary>
public sealed class TargetVersion
{
    public int Id { get; set; }
    public int CycleId { get; set; }
    public TargetCycle? Cycle { get; set; }
    public int Version { get; set; }
    public string AssumptionsJson { get; set; } = "{}";
    public decimal Total { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public int? PublishedByUserId { get; set; }
    public string? Note { get; set; }
}

/// <summary>Published target for one outlet and month, with how it was built.</summary>
public sealed class OutletTarget
{
    public long Id { get; set; }
    public int TargetVersionId { get; set; }
    public TargetVersion? TargetVersion { get; set; }
    public int OutletId { get; set; }
    public Outlet? Outlet { get; set; }
    /// <summary>Holder at the time of publishing; null = unassigned.</summary>
    public int? HolderUserId { get; set; }
    public DateOnly Month { get; set; }
    public decimal Amount { get; set; }
    public decimal BaselinePerMonth { get; set; }
    public decimal Growth { get; set; }
    public bool Recovery { get; set; }
    public decimal RecoveryUpliftPerMonth { get; set; }
}

/// <summary>Region-level targets that do not sit with an outlet (new-hire expansion).</summary>
public sealed class RegionTarget
{
    public int Id { get; set; }
    public int TargetVersionId { get; set; }
    public string Region { get; set; } = "";
    public DateOnly Month { get; set; }
    public decimal Target { get; set; }
    public decimal Expansion { get; set; }
}

public enum UploadType { LedgerEntries, SolutechCheckIns }

public sealed class UploadBatch
{
    public int Id { get; set; }
    public UploadType Type { get; set; }
    public string FileName { get; set; } = "";
    public DateOnly? PeriodFrom { get; set; }
    public DateOnly? PeriodTo { get; set; }
    public int RowsRead { get; set; }
    public int RowsMatched { get; set; }
    public int RowsRejected { get; set; }
    public int UnmappedCustomers { get; set; }
    public int? UploadedByUserId { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Net Modern Trade taxable sales for one outlet and month, KES incl. VAT.</summary>
public sealed class SalesActual
{
    public long Id { get; set; }
    public int OutletId { get; set; }
    public Outlet? Outlet { get; set; }
    public DateOnly Month { get; set; }
    public decimal Amount { get; set; }
    public bool IsProvisional { get; set; }
    public int? UploadBatchId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Visit
{
    public long Id { get; set; }
    public int OutletId { get; set; }
    public int? UserId { get; set; }
    public DateTimeOffset VisitedAt { get; set; }
    public string? Route { get; set; }
    public int? UploadBatchId { get; set; }
}

public enum PlanStatus { Draft, Submitted, Approved, Returned }

public sealed class ActionPlan
{
    public int Id { get; set; }
    public int CycleId { get; set; }
    public int OwnerUserId { get; set; }
    public AppUser? Owner { get; set; }
    public int OutletId { get; set; }
    public Outlet? Outlet { get; set; }
    public string ActionType { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal ExpectedUplift { get; set; }
    public DateOnly DueDate { get; set; }
    public PlanStatus Status { get; set; }
    public int? ReviewerUserId { get; set; }
    public string? ReviewComment { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}

public sealed class AuditLog
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public int? UserId { get; set; }
    public string Action { get; set; } = "";
    public string Entity { get; set; } = "";
    public string? EntityId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}
