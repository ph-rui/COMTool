using COMTool.Connections.Servers;

namespace COMTool.Connections.Tests;

/// <summary>
/// SRV-01: the cron expression parser is a pure function, so it gets a full
/// unit-test matrix: single values, ranges, steps, lists and the error cases.
/// </summary>
public sealed class CronPatternTests
{
    // 2026-10-25 is a Sunday, 2026-11-01 is a Sunday, 2026-10-31 is a Saturday.
    private static readonly DateTime Sun1230 = new(2026, 10, 25, 12, 30, 0);
    private static readonly DateTime Sat0900 = new(2026, 10, 31, 9, 0, 0);

    private static bool Matches(string expression, DateTime time)
    {
        Assert.True(CronPattern.TryParse(expression, out var pattern, out _),
            $"expression should parse: {expression}");
        return pattern.Matches(time);
    }

    [Fact]
    public void SRV01_WildcardMatchesEverything()
    {
        Assert.True(Matches("* * * * *", Sun1230));
        Assert.True(Matches("* * * * *", new DateTime(2000, 2, 29, 23, 59, 0)));
    }

    [Fact]
    public void SRV01_SingleValuesMatchExactly()
    {
        Assert.True(Matches("30 12 * * *", Sun1230));
        Assert.False(Matches("30 12 * * *", Sun1230.AddMinutes(1)));
        Assert.False(Matches("30 12 * * *", Sun1230.AddMinutes(-1)));
        Assert.False(Matches("30 11 * * *", Sun1230));
    }

    [Fact]
    public void SRV01_StepsMatchTheirTicks()
    {
        Assert.True(Matches("*/15 * * * *", new DateTime(2026, 10, 25, 8, 0, 0)));
        Assert.True(Matches("*/15 * * * *", new DateTime(2026, 10, 25, 8, 45, 0)));
        Assert.False(Matches("*/15 * * * *", new DateTime(2026, 10, 25, 8, 7, 0)));
        Assert.True(Matches("0 */6 * * *", new DateTime(2026, 10, 25, 18, 0, 0)));
        Assert.False(Matches("0 */6 * * *", new DateTime(2026, 10, 25, 19, 0, 0)));
    }

    [Fact]
    public void SRV01_RangesAndLists()
    {
        Assert.True(Matches("0 9-17 * * *", new DateTime(2026, 10, 25, 9, 0, 0)));
        Assert.True(Matches("0 9-17 * * *", new DateTime(2026, 10, 25, 17, 0, 0)));
        Assert.False(Matches("0 9-17 * * *", new DateTime(2026, 10, 25, 18, 0, 0)));
        Assert.True(Matches("5,10,20 * * * *", new DateTime(2026, 10, 25, 3, 20, 0)));
        Assert.False(Matches("5,10,20 * * * *", new DateTime(2026, 10, 25, 3, 21, 0)));
        Assert.True(Matches("10-20/5 * * * *", new DateTime(2026, 10, 25, 3, 15, 0)));
        Assert.False(Matches("10-20/5 * * * *", new DateTime(2026, 10, 25, 3, 13, 0)));
    }

    [Fact]
    public void SRV01_DaysOfTheWeekUseZeroForSunday()
    {
        Assert.True(Matches("30 12 * * 0", Sun1230));
        Assert.True(Matches("0 9 * * 6", Sat0900));
        Assert.False(Matches("30 12 * * 6", Sun1230));
        Assert.False(Matches("30 12 * * 0", Sat0900));
    }

    [Fact]
    public void SRV01_MonthAndDayOfMonth()
    {
        Assert.True(Matches("30 12 25 10 *", Sun1230));
        Assert.False(Matches("30 12 25 11 *", Sun1230));
        Assert.False(Matches("30 12 26 10 *", Sun1230));
    }

    [Theory]
    [InlineData("")]
    [InlineData("* * * *")]
    [InlineData("* * * * * *")]
    [InlineData("60 * * * *")]
    [InlineData("* 24 * * *")]
    [InlineData("* * 0 * *")]
    [InlineData("* * * 13 *")]
    [InlineData("* * * * 7")]
    [InlineData("*/0 * * * *")]
    [InlineData("a * * * *")]
    [InlineData("5-3 * * * *")]
    [InlineData("5, * * * *")]
    public void SRV01_InvalidExpressionsAreRejected(string expression)
    {
        Assert.False(CronPattern.TryParse(expression, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }
}
