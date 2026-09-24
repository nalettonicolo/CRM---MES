namespace CrmMes.Core.Models;

/// <summary>Which material lot fed which individual unit — the per-serial layer on top of
/// MaterialLotConsumption (which only knows "this withdrawal item consumed this lot", at batch
/// granularity). Derived, best-effort: when a withdrawal slip tied to a work order with tracked units is
/// closed, each material's consumed lot quantity is poured sequentially into consecutive units according
/// to the product's per-unit BOM quantity — the same "oldest lot first" ledger already used for
/// batch-level genealogy, just sliced further. Not a physically verified assignment (nothing tracks which
/// physical piece of raw material actually went into which physical unit), but a deterministic, reasonable
/// attribution from data already recorded — same spirit as the existing FIFO consumption comment in
/// WithdrawalSlipsController.</summary>
public class WorkOrderUnitMaterialLot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkOrderUnitId { get; set; }
    public WorkOrderUnit Unit { get; set; } = null!;
    public Guid MaterialLotId { get; set; }
    public MaterialLot MaterialLot { get; set; } = null!;
    public string MaterialCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
