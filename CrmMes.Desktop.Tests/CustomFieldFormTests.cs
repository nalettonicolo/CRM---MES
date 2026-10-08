using CrmMes.Desktop.Layout;

namespace CrmMes.Desktop.Tests;

/// <summary>Solo la logica pura (Missing/Validate): BuildControls/GetValue/ToValues creano controlli WPF reali,
/// che richiedono un thread STA non disponibile nel test runner xunit — restano provati dall'uso dell'app, come
/// FormLayoutApplier.Apply.</summary>

public class CustomFieldFormTests
{
    private static CustomFieldDefinitionDto F(string key, string label, string type = "Text", bool required = false, int order = 1) =>
        new(Guid.NewGuid(), key, label, type, order, required);

    [Fact]
    public void Missing_ListsOnlyRequiredFieldsWithoutAValue()
    {
        var fields = new[]
        {
            F("payment-days", "Giorni di pagamento", required: true),
            F("payment-type", "Tipologia di pagamento"),
        };
        var values = new Dictionary<string, string?> { ["payment-days"] = "", ["payment-type"] = "Bonifico" };

        Assert.Equal(["Giorni di pagamento"], CustomFieldForm.Missing(fields, values));
    }

    [Fact]
    public void Missing_WithoutCustomFields_IsEmpty()
    {
        Assert.Empty(CustomFieldForm.Missing([], new Dictionary<string, string?>()));
    }

    [Fact]
    public void Validate_RefusesNonNumericValueForNumberField()
    {
        var fields = new[] { F("payment-days", "Giorni di pagamento", "Number") };
        var values = new Dictionary<string, string?> { ["payment-days"] = "non-un-numero" };

        var error = CustomFieldForm.Validate(fields, values);

        Assert.NotNull(error);
        Assert.Contains("Giorni di pagamento", error);
    }

    [Fact]
    public void Validate_RefusesInvalidDate()
    {
        var fields = new[] { F("expiry", "Scadenza", "Date") };
        var values = new Dictionary<string, string?> { ["expiry"] = "non-una-data" };

        Assert.NotNull(CustomFieldForm.Validate(fields, values));
    }

    [Fact]
    public void Validate_AcceptsEmptyValuesAndCorrectTypes()
    {
        var fields = new[]
        {
            F("payment-days", "Giorni di pagamento", "Number"),
            F("expiry", "Scadenza", "Date"),
        };
        var values = new Dictionary<string, string?> { ["payment-days"] = "30", ["expiry"] = "" };

        Assert.Null(CustomFieldForm.Validate(fields, values));
    }

}
