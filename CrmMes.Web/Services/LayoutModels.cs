namespace CrmMes.Web.Services;

/// <summary>Un campo di una schermata come lo restituisce l'API Layout.</summary>
public sealed record LayoutField(string Key, string Label, int Order, bool Visible, bool Required, bool CanHide)
{
    public string DefaultLabel { get; init; } = string.Empty;
    public bool DefaultRequired { get; init; }
}

public sealed record LayoutScreen(string Screen, string Name, List<LayoutField> Fields);

/// <summary>Chi può modificare i layout: l'utente corrente, i ruoli autorizzati e quelli che l'Admin può autorizzare.</summary>
public sealed record LayoutAccess(bool CanEdit, List<string> GrantedRoles, List<string> GrantableRoles);

/// <summary>Campo aggiunto dall'Admin a una schermata, oltre ai campi predefiniti nel codice (vedi LayoutField).</summary>
public sealed record CustomFieldDefinition(Guid Id, string Key, string Label, string FieldType, int Order, bool Required);

/// <summary>Tipi di campo personalizzato ammessi, con l'etichetta da mostrare nel menu a tendina.</summary>
public static class CustomFieldTypes
{
    public static readonly (string Value, string Label)[] All =
    [
        ("Text", "Testo"),
        ("Number", "Numero"),
        ("Date", "Data"),
        ("Checkbox", "Sì/No"),
    ];

    public static string LabelOf(string type) => All.FirstOrDefault(t => t.Value == type).Label is { } label && label.Length > 0 ? label : type;
}
