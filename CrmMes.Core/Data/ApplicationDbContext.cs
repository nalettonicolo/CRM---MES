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
        });

        modelBuilder.Entity<Area>(entity =>
        {
            entity.Property(a => a.Name).HasMaxLength(200);
            entity.Property(a => a.Code).HasMaxLength(50);
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
            entity.HasOne(o => o.WorkOrder)
                .WithMany(w => w.Operations)
                .HasForeignKey(o => o.WorkOrderId)
                .OnDelete(DeleteBehavior.Cascade);
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
