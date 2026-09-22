namespace CrmMes.Core.Models;

/// <summary>Records that a withdrawal item consumed a given quantity from a given material lot — the
/// edge that makes genealogy queryable both ways: from a lot, which withdrawals (and so which work
/// orders) it fed; from a withdrawal/work order, which lots supplied it.</summary>
public class MaterialLotConsumption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MaterialLotId { get; set; }
    public MaterialLot MaterialLot { get; set; } = null!;
    public Guid WithdrawalItemId { get; set; }
    public WithdrawalItem WithdrawalItem { get; set; } = null!;
    public decimal Quantity { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
