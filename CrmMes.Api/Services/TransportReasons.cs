namespace CrmMes.Api.Services;

/// <summary>Causali del trasporto offered on a DDT. Keys are stored, labels printed. "Subcontracting"
/// is the one that opens conto lavoro: its lines are expected back from the subcontractor.</summary>
public static class TransportReasons
{
    public const string Subcontracting = "Subcontracting";
    public const string Other = "Other";

    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        ["Sale"] = "Vendita",
        [Subcontracting] = "Conto lavorazione",
        ["Repair"] = "Riparazione",
        ["Approval"] = "Conto visione",
        ["ReturnToSupplier"] = "Reso a fornitore",
        ["Warranty"] = "Sostituzione in garanzia",
        ["Transfer"] = "Trasferimento tra sedi",
        ["FreeOfCharge"] = "Omaggio",
        [Other] = "Altro",
    };

    public static readonly IReadOnlyList<string> TransportBy = ["Sender", "Recipient", "Carrier"];

    public static readonly IReadOnlyList<string> Ports = ["Franco", "Assegnato"];

    public static bool IsKnown(string? reason) => reason is not null && Labels.ContainsKey(reason);

    public static string Label(string reason, string? detail) =>
        reason == Other && !string.IsNullOrWhiteSpace(detail) ? detail : Labels.GetValueOrDefault(reason, reason);
}
