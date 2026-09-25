namespace UZLLM.Modules.Billing.Domain;

public enum BudgetPeriod { Lifetime, Daily, Weekly, Monthly }

/// <summary>UTC, half-open budget interval fixed at admission time.</summary>
public readonly record struct BudgetWindow(DateTimeOffset Start, DateTimeOffset? End)
{
    public static BudgetWindow For(BudgetPeriod period, DateTimeOffset instant)
    {
        var utc = instant.ToUniversalTime();
        var midnight = new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
        return period switch
        {
            BudgetPeriod.Lifetime => new(DateTimeOffset.UnixEpoch, null),
            BudgetPeriod.Daily => new(midnight, midnight.AddDays(1)),
            BudgetPeriod.Weekly => Weekly(midnight),
            BudgetPeriod.Monthly => new(new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0,
                TimeSpan.Zero), new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0,
                TimeSpan.Zero).AddMonths(1)),
            _ => throw new ArgumentOutOfRangeException(nameof(period))
        };
    }

    private static BudgetWindow Weekly(DateTimeOffset midnight)
    {
        var monday = midnight.AddDays(-(((int)midnight.DayOfWeek + 6) % 7));
        return new BudgetWindow(monday, monday.AddDays(7));
    }
}
