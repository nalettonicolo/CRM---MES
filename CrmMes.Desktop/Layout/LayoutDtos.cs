namespace CrmMes.Desktop.Layout;

/// <summary>Un campo di una schermata come lo restituisce l'API Layout (stesso contratto del web).</summary>
public sealed record LayoutFieldDto(
    string Key, string DefaultLabel, string Label, int Order, bool Visible, bool Required, bool CanHide, bool DefaultRequired);

public sealed record LayoutScreenDto(string Screen, string Name, List<LayoutFieldDto> Fields);
