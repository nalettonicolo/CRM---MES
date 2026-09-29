using CrmMes.Desktop;

namespace CrmMes.Desktop.Tests;

public class CostingClientTests
{
    [Theory]
    [InlineData("1:30", 90)]
    [InlineData("0:45", 45)]
    [InlineData("1,5", 90)]
    [InlineData("1.5", 90)]
    [InlineData("2", 120)]
    [InlineData("0,25", 15)]
    public void TryParseDuration_AcceptsHoursMinutesAndDecimalHours(string text, double expectedMinutes)
    {
        Assert.True(LaborEntriesWindow.TryParseDuration(text, out var minutes));
        Assert.Equal((decimal)expectedMinutes, minutes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("0:00")]
    [InlineData("1:75")]
    [InlineData("-1")]
    [InlineData("un'ora")]
    public void TryParseDuration_RejectsInvalidOrZero(string text)
    {
        Assert.False(LaborEntriesWindow.TryParseDuration(text, out _));
    }

    [Theory]
    [InlineData(null, "Unknown")]
    [InlineData(-0.05, "Loss")]
    [InlineData(0.0, "Low")]
    [InlineData(0.1499, "Low")]
    [InlineData(0.15, "Good")]
    [InlineData(0.62, "Good")]
    public void MarginLevel_FollowsThresholds(double? ratio, string expected)
    {
        var row = new MarginRowDto(Guid.NewGuid(), "WO", "Prodotto", null, "Completed",
            1000m, 800m, 850m, null, 150m, ratio is null ? null : (decimal)ratio.Value, 0);

        Assert.Equal(expected, row.MarginLevel);
    }

    [Fact]
    public void CanViewMargins_OnlyForAdminAndManagement()
    {
        var client = new ApiClient();
        foreach (var role in new[] { "Operator", "Sales", "Warehouse", "Purchasing", null })
        {
            client.CurrentRole = role;
            Assert.False(client.CanViewMargins);
        }

        client.CurrentRole = "Admin";
        Assert.True(client.CanViewMargins);
        client.CurrentRole = "Management";
        Assert.True(client.CanViewMargins);
    }
}
