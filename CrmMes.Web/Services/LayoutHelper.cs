namespace CrmMes.Web.Services;

/// <summary>Helper comune per le pagine che usano lo strumento Layout.</summary>
public static class LayoutHelper
{
    /// <summary>Etichetta come la vuole l'Admin, con l'asterisco se il campo è obbligatorio.</summary>
    public static string LabelOf(LayoutField f) => f.Required ? f.Label + " *" : f.Label;

    /// <summary>Nomi dei campi obbligatori visibili che risultano vuoti.</summary>
    public static List<string> Missing(IEnumerable<LayoutField> fields, Func<string, string?> valueOf) =>
        fields.Where(f => f.Visible && f.Required && string.IsNullOrWhiteSpace(valueOf(f.Key))).Select(f => f.Label).ToList();
}
