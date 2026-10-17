using UZLLM.Modules.Billing.Domain;

namespace UZLLM.Billing.Tests;

public sealed class WalletAdjustmentPolicyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1_000_000)]
    [InlineData(long.MaxValue)]
    public void Every_positive_credit_requires_a_different_operator(long amount)
    {
        WalletAdjustmentPolicy.EnsureCredit(amount);
        var maker = Guid.NewGuid();
        WalletAdjustmentPolicy.EnsureDifferentOperator(maker, Guid.NewGuid());
        Assert.Throws<UnauthorizedAccessException>(() => WalletAdjustmentPolicy.EnsureDifferentOperator(maker, maker));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public void Zero_and_debits_are_forbidden(long amount) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => WalletAdjustmentPolicy.EnsureCredit(amount));

    [Fact]
    public void Capacity_allows_exact_boundary_but_rejects_overflow()
    {
        WalletAdjustmentPolicy.EnsureCapacity(long.MaxValue - 1, 1);
        Assert.Throws<InvalidOperationException>(() => WalletAdjustmentPolicy.EnsureCapacity(long.MaxValue, 1));
        Assert.Throws<InvalidOperationException>(() => WalletAdjustmentPolicy.EnsureCapacity(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => WalletAdjustmentPolicy.EnsureCapacity(0, 0));
    }

    [Fact]
    public void Missing_operator_ids_are_invalid()
    {
        Assert.Throws<ArgumentException>(() => WalletAdjustmentPolicy.EnsureDifferentOperator(Guid.Empty, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => WalletAdjustmentPolicy.EnsureDifferentOperator(Guid.NewGuid(), Guid.Empty));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("        ")]
    [InlineData("1234567")]
    [InlineData("12345678\n")]
    [InlineData("1234\t5678")]
    public void Invalid_keys_and_reasons_are_rejected(string? value)
    {
        Assert.Throws<ArgumentException>(() => WalletAdjustmentPolicy.Key(value));
        Assert.Throws<ArgumentException>(() => WalletAdjustmentPolicy.Reason(value));
    }

    [Fact]
    public void Printable_terms_are_trimmed_and_length_bounded()
    {
        Assert.Equal("12345678", WalletAdjustmentPolicy.Key(" 12345678 "));
        Assert.Equal("12345678", WalletAdjustmentPolicy.Reason(" 12345678 "));
        Assert.Equal(new string('k', 120), WalletAdjustmentPolicy.Key(new string('k', 120)));
        Assert.Equal(new string('r', 500), WalletAdjustmentPolicy.Reason(new string('r', 500)));
        Assert.Throws<ArgumentException>(() => WalletAdjustmentPolicy.Key(new string('k', 121)));
        Assert.Throws<ArgumentException>(() => WalletAdjustmentPolicy.Reason(new string('r', 501)));
    }

    [Fact]
    public void Unicode_terms_use_postgresql_character_lengths_not_utf16_units()
    {
        var minimum = string.Concat(Enumerable.Repeat("😀", 8));
        Assert.Equal(minimum, WalletAdjustmentPolicy.Key(minimum));
        Assert.Equal(minimum, WalletAdjustmentPolicy.Reason(minimum));
        var tooShort = string.Concat(Enumerable.Repeat("😀", 7));
        Assert.Throws<ArgumentException>(() => WalletAdjustmentPolicy.Key(tooShort));
        Assert.Throws<ArgumentException>(() => WalletAdjustmentPolicy.Reason(tooShort));
        var maximum = string.Concat(Enumerable.Repeat("😀", 120));
        Assert.Equal(maximum, WalletAdjustmentPolicy.Key(maximum));
        Assert.Throws<ArgumentException>(() => WalletAdjustmentPolicy.Key(maximum + "😀"));
    }

    [Fact]
    public void Timestamp_is_utc_and_truncated_to_postgres_precision()
    {
        var now = new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero).AddTicks(19);
        Assert.Equal(now.AddTicks(-9), WalletAdjustmentPolicy.Utc(now));
        Assert.Throws<ArgumentException>(() => WalletAdjustmentPolicy.Utc(now.ToOffset(TimeSpan.FromHours(5))));
    }
}
