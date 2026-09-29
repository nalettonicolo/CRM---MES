namespace CrmMes.Core.Models;

/// <summary>A customer (cliente): who a quote is addressed to and who a work order is built for.
/// Until now the only trace of a customer was the free-text <see cref="WorkOrder.CustomerReference"/>;
/// this is the proper registry behind quotes, and optionally behind work orders.</summary>
public class Customer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Partita IVA — kept as plain text: format checks belong to the fiscal ERP, not here.</summary>
    public string? VatNumber { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
