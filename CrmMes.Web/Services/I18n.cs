namespace CrmMes.Web.Services;

/// <summary>Minimal UI strings for IT/EN based on company locale.</summary>
public static class I18n
{
    public static bool IsEnglish(string? locale) =>
        locale?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true;

    public static string Locale(Session session) =>
        string.IsNullOrWhiteSpace(session.Company?.Locale) ? "it-IT" : session.Company!.Locale;

    public static string Save(Session session) => T(session, "Save");
    public static string Cancel(Session session) => T(session, "Cancel");
    public static string Loading(Session session) => T(session, "Loading");
    public static string Retry(Session session) => T(session, "Retry");
    public static string Dashboard(Session session) => T(session, "Dashboard");

    public static string T(Session session, string key) => T(Locale(session), key);

    public static string T(string? locale, string key)
    {
        var en = IsEnglish(locale);
        return key switch
        {
            "Save" => en ? "Save" : "Salva",
            "Cancel" => en ? "Cancel" : "Annulla",
            "Loading" => en ? "Loading..." : "Caricamento...",
            "Retry" => en ? "Retry" : "Riprova",
            "Dashboard" => en ? "Dashboard" : "Cruscotto",
            _ => key,
        };
    }
}
