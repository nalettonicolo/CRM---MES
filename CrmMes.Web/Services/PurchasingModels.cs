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

public sealed record CatalogSearchResult(
    string MaterialCode, string MaterialName, Guid SupplierId, string SupplierName, string SupplierCode,
    string? SupplierWebsite, string PartNumber, string? Description, decimal UnitPrice, decimal LeadTimeDays);

public sealed record CatalogImportSummary(int Imported, int CreatedMaterials, int CreatedLinks);

public sealed record MrpWorkOrderDemand(Guid WorkOrderId, string WorkOrderCode, string Status, DateTime? DueDate, string ProductCode, decimal Quantity);

public sealed record MrpSuggestion(
    string MaterialCode, string MaterialName, string Unit,
    decimal GrossRequirement, decimal Stock, decimal OnOrder, decimal NetRequirement, decimal SuggestedQuantity,
    decimal MinStock, bool UnknownMaterial, List<MrpWorkOrderDemand> WorkOrders);

public sealed record MrpRun(DateTime GeneratedAt, int WorkOrdersConsidered, int Materials, int MaterialsToOrder, List<MrpSuggestion> Suggestions);

public sealed record WithdrawalSlipSummary(
    Guid Id, string Code, Guid AreaId, Guid RequestedByUserId, string Status, DateTime CreatedAt, int ItemCount, int MissingItemCount,
    Guid? WorkOrderId = null, string? WorkOrderCode = null);

public sealed record WithdrawalSlipItem(Guid Id, string MaterialCode, string Description, decimal Quantity, string Unit, bool IsMissing);

public sealed record WithdrawalSlipDetail(
    Guid Id, string Code, Guid AreaId, Guid RequestedByUserId, Guid? WorkOrderId, string Status, string? Notes,
    DateTime CreatedAt, List<WithdrawalSlipItem> Items, string? WorkOrderCode = null);

public sealed record MaterialLotSummary(
    Guid Id, string MaterialCode, string LotNumber, decimal Quantity, decimal InitialQuantity,
    Guid? SupplierId, Guid? PurchaseOrderId, DateTime ReceivedAt, DateTime? ExpiryDate = null);

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
