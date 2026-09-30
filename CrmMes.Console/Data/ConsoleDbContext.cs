using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Console.Data;

/// <summary>The vendor console's own database: never shared with any customer's installation.</summary>
public class ConsoleDbContext : DbContext, IDataProtectionKeyContext
{
    public ConsoleDbContext(DbContextOptions<ConsoleDbContext> options) : base(options)
    {
    }

    public DbSet<ConsoleUser> Users => Set<ConsoleUser>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Installation> Installations => Set<Installation>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PriceItem> Prices => Set<PriceItem>();
    public DbSet<ConsoleSetting> Settings => Set<ConsoleSetting>();
    public DbSet<ConsoleAudit> Audit => Set<ConsoleAudit>();
    public DbSet<SupportTicket> Tickets => Set<SupportTicket>();

    /// <summary>Login cookies survive a restart of the service (keys in the database, not in memory).</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConsoleUser>(entity =>
        {
            entity.HasIndex(u => u.Email).IsUnique();
            entity.Property(u => u.Email).HasMaxLength(200);
            entity.Property(u => u.Name).HasMaxLength(200);
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.Property(c => c.Name).HasMaxLength(250);
            entity.Property(c => c.BillingMethod).HasMaxLength(20);
            entity.Property(c => c.PlanKey).HasMaxLength(50);
            entity.Property(c => c.MonthlyTotal).HasPrecision(18, 2);
            entity.HasIndex(c => c.StripeCustomerId);
        });

        modelBuilder.Entity<Installation>(entity =>
        {
            entity.HasIndex(i => i.KeyHash).IsUnique();
            entity.Property(i => i.KeyHash).HasMaxLength(64);
            entity.Property(i => i.KeyPrefix).HasMaxLength(20);
            entity.HasOne(i => i.Customer).WithMany(c => c.Installations).HasForeignKey(i => i.CustomerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.Property(p => p.Amount).HasPrecision(18, 2);
            entity.HasIndex(p => p.StripeInvoiceId);
            entity.HasOne(p => p.Customer).WithMany(c => c.Payments).HasForeignKey(p => p.CustomerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PriceItem>(entity =>
        {
            entity.HasKey(p => p.Key);
            entity.Property(p => p.Key).HasMaxLength(50);
            entity.Property(p => p.MonthlyPrice).HasPrecision(18, 2);
        });

        modelBuilder.Entity<ConsoleSetting>(entity => entity.HasKey(s => s.Key));

        modelBuilder.Entity<SupportTicket>(entity =>
        {
            entity.HasIndex(t => t.Number).IsUnique();
            entity.Property(t => t.Subject).HasMaxLength(200);
            entity.Property(t => t.Status).HasMaxLength(20);
            entity.HasOne(t => t.Installation).WithMany().HasForeignKey(t => t.InstallationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(t => t.Customer).WithMany().HasForeignKey(t => t.CustomerId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
