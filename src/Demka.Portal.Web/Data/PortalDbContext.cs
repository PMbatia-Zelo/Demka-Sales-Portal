using Microsoft.EntityFrameworkCore;

namespace Demka.Portal.Web.Data;

public sealed class PortalDbContext(DbContextOptions<PortalDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Outlet> Outlets => Set<Outlet>();
    public DbSet<CustomerMapping> CustomerMappings => Set<CustomerMapping>();
    public DbSet<UnmappedLedgerName> UnmappedLedgerNames => Set<UnmappedLedgerName>();
    public DbSet<MonthStatus> MonthStatuses => Set<MonthStatus>();
    public DbSet<Allocation> Allocations => Set<Allocation>();
    public DbSet<TargetCycle> TargetCycles => Set<TargetCycle>();
    public DbSet<OutletOverride> OutletOverrides => Set<OutletOverride>();
    public DbSet<TargetVersion> TargetVersions => Set<TargetVersion>();
    public DbSet<OutletTarget> OutletTargets => Set<OutletTarget>();
    public DbSet<RegionTarget> RegionTargets => Set<RegionTarget>();
    public DbSet<UploadBatch> UploadBatches => Set<UploadBatch>();
    public DbSet<SalesActual> SalesActuals => Set<SalesActual>();
    public DbSet<Visit> Visits => Set<Visit>();
    public DbSet<ShopMapping> ShopMappings => Set<ShopMapping>();
    public DbSet<ActionPlan> ActionPlans => Set<ActionPlan>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        // Money and rates: 4 decimal places is enough for KES amounts and growth factors.
        b.Properties<decimal>().HavePrecision(19, 4);
        b.Properties<decimal?>().HavePrecision(19, 4);
        b.Properties<string>().HaveMaxLength(200);
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<AppUser>(e =>
        {
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => x.Phone).IsUnique().HasFilter("[Phone] IS NOT NULL");
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.PinHash).HasMaxLength(300);
            e.HasOne(x => x.TeamLeader).WithMany().HasForeignKey(x => x.TeamLeaderId).OnDelete(DeleteBehavior.NoAction);
        });

        m.Entity<Outlet>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Status).HasMaxLength(20);
            e.HasIndex(x => x.Region);
        });

        m.Entity<CustomerMapping>(e =>
        {
            e.HasIndex(x => new { x.CustomerNo, x.NameKey }).IsUnique();
            e.Property(x => x.CustomerNo).HasMaxLength(30);
            e.Property(x => x.Source).HasMaxLength(20);
        });

        m.Entity<Allocation>(e =>
        {
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.OutletId, x.ValidTo });
            e.HasOne(x => x.Holder).WithMany().HasForeignKey(x => x.HolderUserId).OnDelete(DeleteBehavior.NoAction);
        });

        m.Entity<TargetCycle>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.AssumptionsJson).HasColumnType("nvarchar(max)");
            e.HasIndex(x => x.Name).IsUnique();
        });

        m.Entity<OutletOverride>(e => e.HasIndex(x => new { x.CycleId, x.OutletId }).IsUnique());

        m.Entity<TargetVersion>(e =>
        {
            e.Property(x => x.AssumptionsJson).HasColumnType("nvarchar(max)");
            e.HasIndex(x => new { x.CycleId, x.Version }).IsUnique();
        });

        m.Entity<OutletTarget>(e =>
        {
            e.HasIndex(x => new { x.TargetVersionId, x.OutletId, x.Month }).IsUnique();
            e.HasIndex(x => new { x.TargetVersionId, x.HolderUserId });
        });

        m.Entity<RegionTarget>(e => e.HasIndex(x => new { x.TargetVersionId, x.Region, x.Month }).IsUnique());

        m.Entity<UploadBatch>(e =>
        {
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.SummaryJson).HasColumnType("nvarchar(max)");
        });

        m.Entity<UnmappedLedgerName>(e =>
        {
            e.Property(x => x.CustomerNo).HasMaxLength(30);
            e.Property(x => x.PostingGroup).HasMaxLength(30);
            e.HasIndex(x => new { x.UploadBatchId, x.CustomerNo, x.NameKey });
        });

        m.Entity<MonthStatus>(e => e.HasKey(x => x.Month));

        m.Entity<SalesActual>(e => e.HasIndex(x => new { x.OutletId, x.Month }).IsUnique());

        m.Entity<Visit>(e =>
        {
            e.HasIndex(x => new { x.OutletId, x.VisitedAt });
            e.HasIndex(x => x.VisitedAt);
            e.HasIndex(x => x.ShopId);
            e.Property(x => x.ErpCode).HasMaxLength(30);
        });
        m.Entity<ShopMapping>(e =>
        {
            e.HasKey(x => x.ShopId);
            e.Property(x => x.ShopId).ValueGeneratedNever();
            e.Property(x => x.ErpCode).HasMaxLength(30);
            e.Property(x => x.Source).HasMaxLength(20);
        });

        m.Entity<ActionPlan>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.ReviewComment).HasMaxLength(1000);
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.CycleId, x.Status });
        });

        m.Entity<AuditLog>(e =>
        {
            e.Property(x => x.OldValue).HasColumnType("nvarchar(max)");
            e.Property(x => x.NewValue).HasColumnType("nvarchar(max)");
            e.HasIndex(x => x.At);
        });
    }
}
