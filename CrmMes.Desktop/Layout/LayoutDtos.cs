namespace CrmMes.Desktop.Layout;

/// <summary>Un campo di una schermata come lo restituisce l'API Layout (stesso contratto del web).</summary>
public sealed record LayoutFieldDto(
    string Key, string DefaultLabel, string Label, int Order, bool Visible, bool Required, bool CanHide, bool DefaultRequired);

public sealed record LayoutScreenDto(string Screen, string Name, List<LayoutFieldDto> Fields);

/// <summary>Chi può modificare i layout: l'utente corrente, i ruoli autorizzati e quelli che l'Admin può autorizzare.</summary>
public sealed record LayoutAccessDto(bool CanEdit, List<string> GrantedRoles, List<string> GrantableRoles);
