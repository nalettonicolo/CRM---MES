namespace CrmMes.Core.Models;

public class WithdrawalItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WithdrawalSlipId { get; set; }
    public WithdrawalSlip WithdrawalSlip { get; set; } = null!;
    public string MaterialCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "pz";
    public bool IsMissing { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
