using System.ComponentModel.DataAnnotations.Schema;

namespace CrmMes.Core.Models;

public class WarehouseLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid? SiteId { get; set; }
    public Site? Site { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<LocationStock> StockItems { get; set; } = new List<LocationStock>();
}

public class LocationStock
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LocationId { get; set; }
    public WarehouseLocation? Location { get; set; }
    public Guid MaterialId { get; set; }
    public Material? Material { get; set; }
    public decimal Quantity { get; set; }
}

public static class InventorySessionStatuses
{
    public const string Open = "Open";
    public const string Closed = "Closed";
}

public class InventorySession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Status { get; set; } = InventorySessionStatuses.Open;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
    public string? Notes { get; set; }
    public Guid? CreatedBy { get; set; }
    public User? CreatedByUser { get; set; }

    public ICollection<InventoryLine> Lines { get; set; } = new List<InventoryLine>();
}

public class InventoryLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public InventorySession? Session { get; set; }
    public Guid LocationId { get; set; }
    public WarehouseLocation? Location { get; set; }
    public Guid MaterialId { get; set; }
    public Material? Material { get; set; }
    public decimal SystemQuantity { get; set; }
    public decimal? CountedQuantity { get; set; }

    [NotMapped]
    public decimal? Difference => CountedQuantity.HasValue ? CountedQuantity.Value - SystemQuantity : null;
}
