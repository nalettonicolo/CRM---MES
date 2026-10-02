namespace CrmMes.Web.Services;

/// <summary>The web menu: an entry appears when the page exists on the web, the Admin shows its area on
/// the web (Canali di accesso) and the role may use it. Areas that exist only in the desktop program so far
/// are listed by name, so nobody wonders where they went.</summary>
public static class Navigation
{
    public sealed record Entry(string Area, string Title, string Href, string Group, IReadOnlyList<string>? Roles = null);

    public static readonly IReadOnlyList<Entry> WebPages =
    [
        new("dashboard", "Cruscotto", "cruscotto", "Produzione"),
        new("production", "Commesse", "commesse", "Produzione"),
        new("production", "Prodotti", "prodotti", "Produzione"),
        new("planning", "Pianificazione", "pianificazione", "Produzione"),
        new("planning", "Piano capacità", "piano-capacita", "Produzione"),
        new("quality", "Qualità", "qualita", "Produzione"),
        new("quality", "Strumenti di misura", "strumenti", "Qualità", ["Admin", "Management"]),
        new("quality", "CAPA", "capa", "Qualità", ["Admin", "Management"]),
        new("costing", "Margini", "margini", "Produzione", ["Admin", "Management"]),
        new("sales", "Clienti", "clienti", "Vendite"),
        new("sales", "Preventivi", "preventivi", "Vendite"),
        new("invoicing", "Fatture", "fatture", "Vendite", ["Admin", "Sales", "Management"]),
        new("purchasing", "Fornitori", "fornitori", "Acquisti"),
        new("purchasing", "Ordini fornitore", "ordini-fornitore", "Acquisti", ["Admin", "Purchasing", "Warehouse"]),
        new("purchasing", "Da ordinare", "da-ordinare", "Acquisti", ["Admin", "Purchasing", "Warehouse"]),
        new("purchasing", "Fabbisogni MRP", "mrp", "Acquisti", ["Admin", "Purchasing", "Management"]),
        new("purchasing", "Ricerca catalogo", "catalogo", "Acquisti"),
        new("purchasing", "Fatture passive", "fatture-passive", "Acquisti", ["Admin", "Purchasing"]),
        new("purchasing", "Scadenziario", "scadenziario", "Acquisti", ["Admin", "Purchasing", "Management"]),
        new("invoicing", "Scadenziario incassi", "scadenziario", "Vendite", ["Admin", "Sales", "Management"]),
        new("shipping", "Documenti di trasporto", "ddt", "Spedizioni"),
        new("shipping", "Spedizioni", "spedizioni", "Spedizioni"),
        new("subcontracting", "Conto lavoro", "conto-lavoro", "Spedizioni"),
        new("warehouse", "Materiali", "materiali", "Magazzino"),
        new("warehouse", "Ubicazioni", "ubicazioni", "Magazzino", ["Admin", "Warehouse"]),
        new("warehouse", "Distinte di prelievo", "distinte", "Magazzino"),
        new("warehouse", "Lotti materiali", "lotti", "Magazzino"),
        new("lot-expiry", "Scadenze lotti", "lotti", "Magazzino"),
        new("metel", "Listini Metel", "catalogo", "Acquisti"),
        new("food-labels", "Etichette alimentari", "etichette", "Qualità"),
        new("registry", "Anagrafiche", "anagrafiche", "Amministrazione"),
        new("registry", "Presenze", "presenze", "Amministrazione"),
        new("users", "Utenti", "utenti", "Amministrazione", ["Admin"]),
        new("users", "Documentazione API", "documentazione-api", "Amministrazione", ["Admin"]),
        new("users", "Pacchetti settore", "pacchetti-settore", "Amministrazione", ["Admin", "Management"]),
        new("users", "Origine software UE", "origine-software", "Amministrazione", ["Admin", "Management"]),
        new("users", "Assistente IA", "assistente", "Amministrazione", ["Admin", "Management"]),
        new("engineering", "Ufficio tecnico", "ufficio-tecnico", "Produzione"),
        new("machine-testing", "Collaudo e CE", "collaudi", "Produzione"),
        new("maintenance", "Manutenzione", "manutenzione", "Produzione"),
        new("service", "Service post-vendita", "service", "Produzione"),
        new("energy-monitoring", "Monitoraggio energetico", "energia", "Produzione"),
        new("haccp", "HACCP", "haccp", "Qualità"),
        new("site-work", "Rapportini cantiere", "cantiere", "Produzione"),
        new("panel-verification", "Verifica quadri", "verifica-quadri", "Produzione"),
    ];

    /// <summary>Area names (from the server) for areas without a web page yet.</summary>
    public static readonly IReadOnlySet<string> OnWeb = WebPages.Select(page => page.Area).ToHashSet();

    public static List<Entry> Visible(IReadOnlyList<string>? webAreas, string role) =>
        WebPages
            .Where(page => webAreas is null || webAreas.Contains(page.Area))
            .Where(page => page.Roles is null || page.Roles.Contains(role))
            .ToList();

    /// <summary>Areas the Admin shows on the web but that only the desktop program has for now.</summary>
    public static List<string> DesktopOnly(IReadOnlyList<string>? webAreas) =>
        (webAreas ?? []).Where(area => !OnWeb.Contains(area)).ToList();
}
