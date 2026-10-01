namespace CrmMes.Licensing;

/// <summary>The optional modules that can be sold, as the vendor console lists them. The same keys as the
/// modules of the management software (Sectors in CrmMes.Api); a test keeps the two lists aligned.</summary>
public static class ModuleCatalog
{
    public sealed record Module(string Key, string Name);

    public static readonly IReadOnlyList<Module> All =
    [
        new("sales", "Vendite"),
        new("purchasing", "Acquisti"),
        new("planning", "Pianificazione"),
        new("shopfloor", "Terminale di reparto"),
        new("quality", "Qualità"),
        new("maintenance", "Manutenzione"),
        new("shipping", "Spedizioni e DDT"),
        new("subcontracting", "Conto lavoro"),
        new("costing", "Costi e margini"),
        new("invoicing", "Fattura elettronica"),
        new("panel-verification", "Verifica quadri CEI EN 61439"),
        new("metel", "Listini Metel"),
        new("lot-expiry", "Scadenze lotti"),
        new("food-labels", "Etichette, allergeni e SSCC"),
        new("haccp", "Registri HACCP"),
        new("site-work", "Lavori in cantiere"),
        new("engineering", "Ufficio tecnico"),
        new("machine-testing", "Collaudo macchine e CE"),
        new("service", "Service post-vendita"),
        new("energy-monitoring", "Monitoraggio energetico"),
    ];

    public static string NameOf(string key) => All.FirstOrDefault(m => m.Key == key)?.Name ?? key;
}
