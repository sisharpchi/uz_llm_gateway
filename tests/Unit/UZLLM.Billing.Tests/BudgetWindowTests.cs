using UZLLM.Modules.Billing.Domain;

namespace UZLLM.Billing.Tests;

public sealed class BudgetWindowTests
{
    [Fact]
    public void Daily_uses_utc_midnight_not_client_offset()
    {
        var before = BudgetWindow.For(BudgetPeriod.Daily,
            new DateTimeOffset(2026, 9, 25, 4, 59, 59, TimeSpan.FromHours(5)));
        var at = BudgetWindow.For(BudgetPeriod.Daily,
            new DateTimeOffset(2026, 9, 25, 5, 0, 0, TimeSpan.FromHours(5)));

        Assert.Equal(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero), before.Start);
        Assert.Equal(new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero), before.End);
        Assert.Equal(before.End, at.Start);
    }

    [Fact]
    public void Weekly_rolls_on_monday_utc()
    {
        var sunday = BudgetWindow.For(BudgetPeriod.Weekly,
            new DateTimeOffset(2026, 9, 27, 23, 59, 59, TimeSpan.Zero));
        var monday = BudgetWindow.For(BudgetPeriod.Weekly,
            new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero), sunday.Start);
        Assert.Equal(sunday.End, monday.Start);
        Assert.Equal(monday.Start.AddDays(7), monday.End);
    }

    [Fact]
    public void Monthly_rolls_on_first_day_across_year_boundary()
    {
        var december = BudgetWindow.For(BudgetPeriod.Monthly,
            new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero));
        var january = BudgetWindow.For(BudgetPeriod.Monthly,
            new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), december.Start);
        Assert.Equal(december.End, january.Start);
        Assert.Equal(new DateTimeOffset(2027, 2, 1, 0, 0, 0, TimeSpan.Zero), january.End);
    }

    [Fact]
    public void Lifetime_has_stable_origin_and_no_end()
    {
        var window = BudgetWindow.For(BudgetPeriod.Lifetime,
            new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(DateTimeOffset.UnixEpoch, window.Start);
        Assert.Null(window.End);
    }

    [Fact]
    public void Unknown_period_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BudgetWindow.For((BudgetPeriod)99, DateTimeOffset.UnixEpoch));
    }
}
