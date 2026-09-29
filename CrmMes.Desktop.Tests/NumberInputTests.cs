using CrmMes.Desktop;

namespace CrmMes.Desktop.Tests;

public class NumberInputTests
{
    [Theory]
    [InlineData("12,5", 12.5)]          // Italian keyboard: was read as 125 by the quality screens
    [InlineData("12.5", 12.5)]          // numeric keypad / habit: was read as 125 by every other screen
    [InlineData("1.250,50", 1250.50)]
    [InlineData("1250,50", 1250.50)]
    [InlineData("1250.50", 1250.50)]
    [InlineData("1.250.000", 1250000)]
    [InlineData(" 40 ", 40)]
    [InlineData("-0,25", -0.25)]
    [InlineData("0", 0)]
    public void TryParseDecimal_ReadsBothKeyboardHabits(string text, double expected)
    {
        Assert.True(NumberInput.TryParseDecimal(text, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("1,2,3")]
    public void TryParseDecimal_RejectsInvalidInput(string? text)
    {
        Assert.False(NumberInput.TryParseDecimal(text, out _));
    }

    [Fact]
    public void ParseOptionalDecimal_ReturnsNullForEmpty()
    {
        Assert.Null(NumberInput.ParseOptionalDecimal(""));
        Assert.Equal(9.8m, NumberInput.ParseOptionalDecimal("9,8"));
    }
}
