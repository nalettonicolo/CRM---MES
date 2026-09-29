using CrmMes.Desktop;

namespace CrmMes.Desktop.Tests;

public class GridViewFillTests
{
    [Fact]
    public void Distribute_SharesSpareWidthInProportion_KeepingButtonColumnsFixed()
    {
        // Nome 200, Email 200, Ruolo 100 (stretch) + a 100px button column (fixed), in 1100px.
        var widths = GridViewFill.Distribute([(200, true), (200, true), (100, true), (100, false)], 1100);

        Assert.Equal([400, 400, 200, 100], widths);
    }

    [Fact]
    public void Distribute_NoSpareWidth_KeepsDeclaredWidths()
    {
        var widths = GridViewFill.Distribute([(300, true), (300, true)], 500);

        Assert.Equal([300, 300], widths);
    }

    [Fact]
    public void Distribute_OnlyFixedColumns_KeepsDeclaredWidths()
    {
        var widths = GridViewFill.Distribute([(100, false), (100, false)], 900);

        Assert.Equal([100, 100], widths);
    }
}
