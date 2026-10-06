using CrmMes.Desktop.Layout;

namespace CrmMes.Desktop.Tests;

public class FormLayoutApplierTests
{
    private static LayoutFieldDto F(string key, string label, bool visible = true, bool required = false, bool canHide = true) =>
        new(key, label, label, 1, visible, required, canHide, required);

    [Fact]
    public void Missing_ListsOnlyVisibleRequiredFieldsThatAreEmpty()
    {
        var fields = new[]
        {
            F("equipment", "Macchina", required: true, canHide: false),
            F("title", "Titolo", required: true, canHide: false),
            F("description", "Descrizione", required: true),
            F("dueDate", "Scadenza", visible: false, required: true),
            F("recurrence", "Ricorrenza"),
        };

        var missing = FormLayoutApplier.Missing(fields, key => key == "title");

        Assert.Equal(["Macchina", "Descrizione"], missing);
    }

    [Fact]
    public void LabelText_MarksRequiredFields()
    {
        Assert.Equal("Titolo *", FormLayoutApplier.LabelText(F("title", "Titolo", required: true)));
        Assert.Equal("Titolo", FormLayoutApplier.LabelText(F("title", "Titolo")));
    }

    [Fact]
    public void Missing_WithoutLayout_IsEmpty()
    {
        Assert.Empty(FormLayoutApplier.Missing([], _ => false));
    }
}
