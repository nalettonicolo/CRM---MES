namespace CrmMes.Core.Models;

/// <summary>A shipping carrier/courier (corriere) that moves goods in either direction — inbound
/// deliveries from suppliers, outbound deliveries to customers. Deliberately just a contact registry,
/// same shape as Supplier; the actual shipment tracking lives in Shipment.</summary>
public class Carrier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
