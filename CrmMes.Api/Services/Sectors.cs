namespace CrmMes.Api.Services;

/// <summary>The industries the system is configured for and the modules each one switches on. The core
/// (materials, products, work orders, work centers, lots, dashboard, users) is always on; everything else
/// is a module. A sector is only a starting preset: the Admin can then switch any module on or off.
///
/// Common modules serve every industry; sector modules ("a world that opens") only make sense in one:
/// the CEI EN 61439 panel verification for panel builders, Metel price lists for the electrical supply
/// chain, lot expiry for food, job-site work for installers.</summary>
public static class Sectors
{
    /// <summary>Available=false: announced for its sector but not built yet — shown, not selectable.</summary>
    public sealed record ModuleInfo(string Key, string Name, string Description, bool SectorSpecific, bool Available = true);

    public sealed record SectorInfo(string Key, string Name, string Description, IReadOnlyList<string> Modules);

    public static readonly IReadOnlyList<ModuleInfo> Modules =
    [
        new("sales", "Vendite", "Clienti, preventivi e conversione in commesse.", false),
        new("purchasing", "Acquisti", "Fornitori, ordini fornitore, cataloghi e listini.", false),
        new("planning", "Pianificazione", "Board delle scadenze e planning di produzione.", false),
        new("shopfloor", "Terminale di reparto", "Avanzamento fasi con QR e PIN operatore, anche offline.", false),
        new("quality", "Qualità", "Piani di controllo, non conformità, certificato di conformità.", false),
        new("maintenance", "Manutenzione", "Macchine e interventi preventivi e correttivi.", false),
        new("shipping", "Spedizioni e DDT", "Corrieri, spedizioni e documenti di trasporto.", false),
        new("subcontracting", "Conto lavoro", "Materiale inviato a terzisti e rientri delle lavorazioni.", false),
        new("costing", "Costi e margini", "Ore lavorate, costo reale e margine di commessa (solo direzione).", false),
        new("invoicing", "Fattura elettronica", "Fatture differite dai DDT e fatture immediate, file XML FatturaPA per lo SdI.", false),
        new("panel-verification", "Verifica quadri CEI EN 61439", "Verifica individuale e dichiarazione di conformità del quadro.", true),
        new("metel", "Listini Metel", "Importazione dei listini dei produttori di materiale elettrico.", true, Available: false),
        new("lot-expiry", "Scadenze lotti", "Data di scadenza dei lotti e prelievo del lotto che scade prima.", true),
        new("food-labels", "Etichette, allergeni e SSCC", "Ingredienti e allergeni dalla distinta, etichetta del lotto, etichetta pallet SSCC.", true),
        new("haccp", "Registri HACCP", "Punti di controllo, letture con limiti e azioni correttive.", true),
        new("site-work", "Lavori in cantiere", "Rapportini con ore, materiali e firma del cliente, anche da telefono.", true),
        new("engineering", "Ufficio tecnico", "Revisioni di distinte e cicli, disegni e schemi allegati, modifiche tecniche approvate.", true),
        new("machine-testing", "Collaudo macchine e CE", "Collaudi in fabbrica e presso il cliente, fascicolo tecnico, dichiarazione CE.", true),
        new("service", "Service post-vendita", "Macchine installate presso i clienti con matricola e garanzia, richieste di assistenza, interventi.", true, Available: false),
    ];

    private static readonly string[] Common = ["sales", "purchasing", "planning", "shopfloor", "quality", "maintenance", "shipping", "costing", "invoicing"];

    public static readonly IReadOnlyList<SectorInfo> All =
    [
        new("machine-building", "Costruzione macchine e impianti",
            "Macchine e impianti su progetto: lavorazioni, montaggio, quadri a bordo macchina, collaudo e assistenza.",
            [.. Common, "subcontracting", "panel-verification", "engineering", "machine-testing", "service"]),
        new("electrical-panels", "Quadri elettrici e automazione",
            "Quadristi, costruttori di quadri e bordo macchina.",
            [.. Common, "subcontracting", "panel-verification", "metel"]),
        new("mechanical", "Meccanica e carpenteria",
            "Lavorazioni meccaniche, carpenteria, lamiera, con trattamenti presso terzi.",
            [.. Common, "subcontracting"]),
        new("food", "Alimentare",
            "Produzioni alimentari con lotti a scadenza e tracciabilità di filiera.",
            [.. Common, "lot-expiry", "food-labels", "haccp"]),
        new("installations", "Impiantistica e installazioni",
            "Impianti elettrici, termoidraulici e tecnologici con lavori presso il cliente.",
            [.. Common.Where(module => module != "shopfloor"), "site-work", "metel"]),
        new("generic", "Manifattura generica",
            "Qualsiasi produzione su commessa o a lotti: solo i moduli comuni.",
            Common),
    ];

    public static SectorInfo? Find(string? key) =>
        All.FirstOrDefault(sector => string.Equals(sector.Key, key, StringComparison.OrdinalIgnoreCase));

    public static bool IsKnownModule(string key) =>
        Modules.Any(module => string.Equals(module.Key, key, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> Parse(string? enabledModules) =>
        (enabledModules ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsKnownModule)
            .Select(key => key.ToLowerInvariant())
            .Distinct()
            .ToList();
}
