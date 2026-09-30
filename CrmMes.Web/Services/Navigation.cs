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
        new("sales", "Clienti", "clienti", "Vendite"),
        new("sales", "Preventivi", "preventivi", "Vendite"),
        new("warehouse", "Materiali", "materiali", "Magazzino"),
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
