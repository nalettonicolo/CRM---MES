namespace CrmMes.Core.Models;

public class MissingMaterial
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? WithdrawalSlipId { get; set; }
    public WithdrawalSlip? WithdrawalSlip { get; set; }
    public string MaterialCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Source { get; set; } = "Manual";
    public string Status { get; set; } = "Open";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
