namespace CrmMes.Desktop.Layout;

/// <summary>Campo personalizzato aggiunto dall'Admin a una schermata oltre ai campi predefiniti nel codice
/// (stesso contratto del web): es. "Giorni di pagamento" su fornitori o clienti. <see cref="FieldType"/> vale
/// "Text", "Number", "Date" o "Checkbox".</summary>
public sealed record CustomFieldDefinitionDto(Guid Id, string Key, string Label, string FieldType, int Order, bool Required);
