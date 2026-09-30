namespace CrmMes.Web.Services;

// Purchasing: suppliers, purchase orders, what to reorder (mirrors of the records in CrmMes.Api).

public sealed record Supplier(Guid Id, string Name, string Code, string? Email, string? Phone, string? Website, bool IsActive);

public sealed record SupplierPurchaseOrder(Guid Id, string Code, string Status, DateTime CreatedAt);

public sealed record SupplierCatalogEntry(string MaterialCode, string MaterialName, string PartNumber, string? Description, decimal UnitPrice, decimal LeadTimeDays);

public sealed record SupplierDetail(
    Guid Id, string Name, string Code, string? Email, string? Phone, string? Website, bool IsActive,
    List<SupplierPurchaseOrder> PurchaseOrders, List<SupplierCatalogEntry> CatalogEntries);

public sealed record PurchaseOrderSummary(
    Guid Id, string Code, Guid SupplierId, string Status, DateTime CreatedAt, DateTime? ConfirmedAt, DateTime? ReceivedAt,
    int ItemCount, DateTime? ExpectedDeliveryDate);

public sealed record PurchaseOrderItem(
    Guid Id, string MaterialCode, string Description, decimal Quantity, decimal UnitPrice, decimal ReceivedQuantity, Guid? MissingMaterialId)
{
    public decimal Remaining => Math.Max(0, Quantity - ReceivedQuantity);
}

public sealed record PurchaseOrder(
    Guid Id, string Code, Guid SupplierId, string Status, DateTime CreatedAt, DateTime? ConfirmedAt, DateTime? ReceivedAt,
    DateTime? ExpectedDeliveryDate, List<PurchaseOrderItem> Items);

public sealed record LowStockMaterial(
    Guid Id, string Code, string Name, string Unit, decimal Stock, decimal MinStock, decimal SuggestedQuantity, bool AlreadyRequested);

public sealed record MissingMaterial(Guid Id, Guid? WithdrawalSlipId, string MaterialCode, decimal Quantity, string Source, string Status, DateTime CreatedAt);

/// <summary>Mirrors the API's policies: "Purchasing" confirms orders, "PurchasingOrWarehouse" receives
/// goods and sees orders and shortages.</summary>
public static class PurchasingRules
{
    public static bool CanSeeOrders(string role) => role is "Admin" or "Purchasing" or "Warehouse";

    public static bool CanConfirm(PurchaseOrder order, string role) => role is "Admin" or "Purchasing" && order.Status == "Draft";

    public static bool CanReceive(PurchaseOrder order, string role) =>
        CanSeeOrders(role) && order.Status is "Confirmed" or "PartiallyReceived" && order.Items.Any(i => i.Remaining > 0);

    public static bool IsLate(DateTime? expected, string status, DateTime now) =>
        expected is not null && status is "Confirmed" or "PartiallyReceived" && expected.Value.ToLocalTime().Date < now.ToLocalTime().Date;
}
