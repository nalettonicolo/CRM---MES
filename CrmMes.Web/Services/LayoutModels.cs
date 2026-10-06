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
