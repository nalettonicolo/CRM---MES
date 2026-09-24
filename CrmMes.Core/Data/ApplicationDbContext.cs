using CrmMes.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Core.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Area> Areas => Set<Area>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<MaterialSupplier> MaterialSuppliers => Set<MaterialSupplier>();
    public DbSet<WithdrawalSlip> WithdrawalSlips => Set<WithdrawalSlip>();
    public DbSet<WithdrawalItem> WithdrawalItems => Set<WithdrawalItem>();
    public DbSet<MissingMaterial> MissingMaterials => Set<MissingMaterial>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<BillOfMaterialItem> BillOfMaterialItems => Set<BillOfMaterialItem>();
    public DbSet<RoutingStep> RoutingSteps => Set<RoutingStep>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderOperation> WorkOrderOperations => Set<WorkOrderOperation>();
    public DbSet<OperationDowntime> OperationDowntimes => Set<OperationDowntime>();
    public DbSet<NonConformity> NonConformities => Set<NonConformity>();
    public DbSet<MaterialLot> MaterialLots => Set<MaterialLot>();
    public DbSet<MaterialLotConsumption> MaterialLotConsumptions => Set<MaterialLotConsumption>();
    public DbSet<WorkCenter> WorkCenters => Set<WorkCenter>();
    public DbSet<WorkOrderUnit> WorkOrderUnits => Set<WorkOrderUnit>();
    public DbSet<Carrier> Carriers => Set<Carrier>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<Site> Sites => Set<Site>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.Email).IsUnique();
            entity.Property(u => u.Name).HasMaxLength(200);
            entity.Property(u => u.Email).HasMaxLength(200);
            entity.Property(u => u.Role).HasMaxLength(50);
            entity.Property(u => u.PasswordHash).HasMaxLength(500);
            entity.Property(u => u.PinHash).HasMaxLength(500);
        });

        modelBuilder.Entity<Area>(entity =>
        {
            entity.Property(a => a.Name).HasMaxLength(200);
            entity.Property(a => a.Code).HasMaxLength(50);
            entity.HasOne(a => a.Site)
                .WithMany()
                .HasForeignKey(a => a.SiteId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Site>(entity =>
        {
            entity.Property(s => s.Name).HasMaxLength(200);
            entity.Property(s => s.Code).HasMaxLength(50);
            entity.Property(s => s.Address).HasMaxLength(500);
            entity.HasIndex(s => s.Code).IsUnique();
        });

        modelBuilder.Entity<Material>(entity =>
        {
            entity.HasIndex(m => m.Code).IsUnique();
            entity.Property(m => m.Code).HasMaxLength(100);
            entity.Property(m => m.Name).HasMaxLength(250);
            entity.Property(m => m.Unit).HasMaxLength(50);
        });

        modelBuilder.Entity<Supplier>(entity =>
        {
            entity.Property(s => s.Name).HasMaxLength(200);
            entity.Property(s => s.Code).HasMaxLength(80);
            entity.Property(s => s.Website).HasMaxLength(500);
            entity.HasIndex(s => s.Code).IsUnique();
        });

        modelBuilder.Entity<MaterialSupplier>(entity =>
        {
            entity.HasKey(ms => new { ms.MaterialId, ms.SupplierId });
            entity.Property(ms => ms.PartNumber).HasMaxLength(200);
            entity.Property(ms => ms.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<WithdrawalSlip>(entity =>
        {
            entity.Property(w => w.Code).HasMaxLength(80);
            entity.Property(w => w.Status).HasMaxLength(50);
            entity.HasIndex(w => w.Code).IsUnique();
            entity.HasOne(w => w.WorkOrder)
                .WithMany()
                .HasForeignKey(w => w.WorkOrderId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<WithdrawalItem>(entity =>
        {
            entity.Property(wi => wi.MaterialCode).HasMaxLength(120);
            entity.Property(wi => wi.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<MissingMaterial>(entity =>
        {
            entity.Property(mm => mm.MaterialCode).HasMaxLength(120);
            entity.Property(mm => mm.Source).HasMaxLength(100);
            entity.Property(mm => mm.Status).HasMaxLength(50);
            entity.HasOne(mm => mm.WithdrawalSlip)
                .WithMany()
                .HasForeignKey(mm => mm.WithdrawalSlipId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PurchaseOrder>(entity =>
        {
            entity.Property(po => po.Code).HasMaxLength(80);
            entity.Property(po => po.Status).HasMaxLength(50);
            entity.HasIndex(po => po.Code).IsUnique();
        });

        modelBuilder.Entity<PurchaseOrderItem>(entity =>
        {
            entity.Property(poi => poi.MaterialCode).HasMaxLength(120);
            entity.Property(poi => poi.Description).HasMaxLength(500);
            entity.HasOne(poi => poi.MissingMaterial)
                .WithMany()
                .HasForeignKey(poi => poi.MissingMaterialId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Carrier>(entity =>
        {
            entity.Property(c => c.Name).HasMaxLength(200);
            entity.Property(c => c.Code).HasMaxLength(80);
            entity.Property(c => c.Email).HasMaxLength(200);
            entity.Property(c => c.Phone).HasMaxLength(50);
            entity.HasIndex(c => c.Code).IsUnique();
        });

        modelBuilder.Entity<Shipment>(entity =>
        {
            entity.Property(s => s.Code).HasMaxLength(80);
            entity.Property(s => s.Direction).HasMaxLength(20);
            entity.Property(s => s.Status).HasMaxLength(50);
            entity.Property(s => s.TrackingNumber).HasMaxLength(120);
            entity.Property(s => s.CounterpartReference).HasMaxLength(250);
            entity.Property(s => s.Address).HasMaxLength(500);
            entity.Property(s => s.Notes).HasMaxLength(1000);
            entity.HasIndex(s => s.Code).IsUnique();
            entity.HasOne(s => s.Carrier)
                .WithMany()
                .HasForeignKey(s => s.CarrierId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(s => s.PurchaseOrder)
                .WithMany()
                .HasForeignKey(s => s.PurchaseOrderId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(s => s.WorkOrder)
                .WithMany()
                .HasForeignKey(s => s.WorkOrderId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.Property(a => a.Action).HasMaxLength(100);
            entity.Property(a => a.EntityType).HasMaxLength(100);
            entity.Property(a => a.UserName).HasMaxLength(200);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.Property(rt => rt.TokenHash).HasMaxLength(128);
            entity.HasIndex(rt => rt.TokenHash).IsUnique();
            entity.HasOne(rt => rt.User)
                .WithMany()
                .HasForeignKey(rt => rt.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.Property(p => p.Code).HasMaxLength(100);
            entity.Property(p => p.Name).HasMaxLength(250);
            entity.Property(p => p.Description).HasMaxLength(1000);
            entity.HasIndex(p => p.Code).IsUnique();
        });

        modelBuilder.Entity<BillOfMaterialItem>(entity =>
        {
            entity.Property(b => b.MaterialCode).HasMaxLength(120);
            entity.Property(b => b.Notes).HasMaxLength(500);
            entity.HasOne(b => b.Product)
                .WithMany(p => p.BillOfMaterial)
                .HasForeignKey(b => b.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RoutingStep>(entity =>
        {
            entity.Property(r => r.Name).HasMaxLength(200);
            entity.Property(r => r.Description).HasMaxLength(1000);
            entity.Property(r => r.WorkCenter).HasMaxLength(200);
            entity.HasOne(r => r.Product)
                .WithMany(p => p.RoutingSteps)
                .HasForeignKey(r => r.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkOrder>(entity =>
        {
            entity.Property(w => w.Code).HasMaxLength(80);
            entity.Property(w => w.CustomerReference).HasMaxLength(250);
            entity.Property(w => w.Status).HasMaxLength(50);
            entity.Property(w => w.Notes).HasMaxLength(1000);
            entity.Property(w => w.ProductLotNumber).HasMaxLength(120);
            entity.HasIndex(w => w.Code).IsUnique();
            entity.HasOne(w => w.Product)
                .WithMany()
                .HasForeignKey(w => w.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(w => w.Area)
                .WithMany()
                .HasForeignKey(w => w.AreaId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<WorkOrderOperation>(entity =>
        {
            entity.Property(o => o.Name).HasMaxLength(200);
            entity.Property(o => o.Description).HasMaxLength(1000);
            entity.Property(o => o.WorkCenter).HasMaxLength(200);
            entity.Property(o => o.Status).HasMaxLength(50);
            entity.Property(o => o.StartedBy).HasMaxLength(200);
            entity.Property(o => o.CompletedBy).HasMaxLength(200);
            entity.HasOne(o => o.WorkOrder)
                .WithMany(w => w.Operations)
                .HasForeignKey(o => o.WorkOrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(o => o.StartedByUser)
                .WithMany()
                .HasForeignKey(o => o.StartedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(o => o.CompletedByUser)
                .WithMany()
                .HasForeignKey(o => o.CompletedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<OperationDowntime>(entity =>
        {
            entity.Property(d => d.Reason).HasMaxLength(200);
            entity.Property(d => d.Notes).HasMaxLength(1000);
            entity.Property(d => d.ReportedBy).HasMaxLength(200);
            entity.Property(d => d.ClosedBy).HasMaxLength(200);
            entity.HasOne(d => d.Operation)
                .WithMany()
                .HasForeignKey(d => d.WorkOrderOperationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(d => d.ReportedByUser)
                .WithMany()
                .HasForeignKey(d => d.ReportedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(d => d.ClosedByUser)
                .WithMany()
                .HasForeignKey(d => d.ClosedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<NonConformity>(entity =>
        {
            entity.Property(n => n.Description).HasMaxLength(200);
            entity.Property(n => n.Notes).HasMaxLength(1000);
            entity.Property(n => n.ReportedBy).HasMaxLength(200);
            entity.HasOne(n => n.Operation)
                .WithMany()
                .HasForeignKey(n => n.WorkOrderOperationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(n => n.Unit)
                .WithMany()
                .HasForeignKey(n => n.WorkOrderUnitId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(n => n.ReportedByUser)
                .WithMany()
                .HasForeignKey(n => n.ReportedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<WorkOrderUnit>(entity =>
        {
            entity.Property(u => u.SerialNumber).HasMaxLength(120);
            entity.Property(u => u.Status).HasMaxLength(50);
            entity.HasIndex(u => u.SerialNumber).IsUnique();
            entity.HasOne(u => u.WorkOrder)
                .WithMany(w => w.Units)
                .HasForeignKey(u => u.WorkOrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MaterialLot>(entity =>
        {
            entity.Property(l => l.MaterialCode).HasMaxLength(120);
            entity.Property(l => l.LotNumber).HasMaxLength(120);
            entity.Property(l => l.Notes).HasMaxLength(500);
            entity.HasIndex(l => new { l.MaterialCode, l.LotNumber }).IsUnique();
            entity.HasOne(l => l.Supplier)
                .WithMany()
                .HasForeignKey(l => l.SupplierId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(l => l.PurchaseOrder)
                .WithMany()
                .HasForeignKey(l => l.PurchaseOrderId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MaterialLotConsumption>(entity =>
        {
            entity.HasOne(c => c.MaterialLot)
                .WithMany(l => l.Consumptions)
                .HasForeignKey(c => c.MaterialLotId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(c => c.WithdrawalItem)
                .WithMany()
                .HasForeignKey(c => c.WithdrawalItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkCenter>(entity =>
        {
            entity.Property(w => w.Code).HasMaxLength(50);
            entity.Property(w => w.Name).HasMaxLength(200);
            entity.Property(w => w.Description).HasMaxLength(500);
            entity.HasIndex(w => w.Code).IsUnique();
            entity.HasOne(w => w.Site)
                .WithMany()
                .HasForeignKey(w => w.SiteId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Sqlite has no native decimal type and can't ORDER BY / compare the TEXT it stores decimals as.
        // Postgres (production) handles decimal natively, so this only kicks in for the Sqlite test provider.
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            foreach (var property in modelBuilder.Model.GetEntityTypes()
                .SelectMany(entityType => entityType.GetProperties())
                .Where(property => property.ClrType == typeof(decimal)))
            {
                property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<decimal, double>(
                    value => (double)value,
                    value => (decimal)value));
            }
        }
    }
}
