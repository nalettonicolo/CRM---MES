namespace CrmMes.Web.Services;

public sealed record WarehouseLocation(
    Guid Id, string Code, string Name, Guid? SiteId, bool IsActive, DateTime CreatedAt);

public sealed record LocationStockRow(
    Guid Id, Guid MaterialId, string MaterialCode, string MaterialName, string Unit, decimal Quantity);
