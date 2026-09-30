namespace CrmMes.Desktop.Tests;

public class AccessChannelsWindowTests
{
    [Fact]
    public void Restrictions_KeepOnlyRowsNotTickedEverywhere()
    {
        var ticks = new Dictionary<(string Key, string Channel), bool>
        {
            [("Sales", "desktop")] = false,
            [("Sales", "web")] = true,
            [("Sales", "mobile")] = false,
            [("Operator", "desktop")] = true,
            [("Operator", "web")] = true,
            [("Operator", "mobile")] = true,
        };

        var result = AccessChannelsWindow.ToRestrictions(ticks, ["desktop", "web", "mobile"]);

        Assert.Equal(["web"], result["Sales"]);
        Assert.False(result.ContainsKey("Operator"));
    }
}
