namespace UZLLM.Modules.Billing.Domain;

public static class WalletAdjustmentPolicy
{
    public static string Key(string? value) => Normalize(value, 120, "Adjustment key");
    public static string Reason(string? value) => Normalize(value, 500, "Reason");

    public static void EnsureCredit(long amount)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Only positive credits are authorized.");
    }

    public static void EnsureDifferentOperator(Guid maker, Guid checker)
    {
        if (maker == Guid.Empty || checker == Guid.Empty) throw new ArgumentException("Operator IDs are required.");
        if (maker == checker) throw new UnauthorizedAccessException("Self-approval is forbidden.");
    }

    public static void EnsureCapacity(long postedBalance, long credit)
    {
        EnsureCredit(credit);
        if (postedBalance < 0 || credit > long.MaxValue - postedBalance)
            throw new InvalidOperationException("Wallet credit exceeds supported balance capacity.");
    }

    public static DateTimeOffset Utc(DateTimeOffset value) => value.Offset == TimeSpan.Zero
        ? new DateTimeOffset(value.Ticks - value.Ticks % 10, TimeSpan.Zero)
        : throw new ArgumentException("UTC time is required.");

    private static string Normalize(string? value, int maximum, string name)
    {
        var normalized = value?.Trim() ?? string.Empty;
        // PostgreSQL varchar/length count Unicode scalar values, not UTF-16 code units.
        var length = 0;
        foreach (var _ in normalized.EnumerateRunes()) length++;
        if (value is null || length < 8 || length > maximum || value.Any(char.IsControl))
            throw new ArgumentException($"{name} must contain 8–{maximum} printable characters.");
        return normalized;
    }
}
