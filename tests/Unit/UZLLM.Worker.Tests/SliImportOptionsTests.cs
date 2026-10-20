using Microsoft.Extensions.Configuration;
using UZLLM.Observability;
using UZLLM.Worker;

namespace UZLLM.Worker.Tests;

public sealed class SliImportOptionsTests
{
    [Fact]
    public void Import_is_disabled_by_default_and_enabled_only_with_private_scoped_producer_trust()
    {
        var disabled = SliImportOptions.FromConfiguration(new ConfigurationBuilder().Build());
        Assert.False(disabled.Enabled);
        Assert.Empty(disabled.Trust.Producers);
        var enabled = SliImportOptions.FromConfiguration(Configuration());
        Assert.True(enabled.Enabled);
        Assert.True(Path.IsPathFullyQualified(enabled.Directory));
        var trust = Assert.Single(enabled.Trust.Producers);
        Assert.Equal("edge-a", trust.Key);
        Assert.Equal(ObservationSource.Edge, trust.Value.Source);
        Assert.Equal("test", trust.Value.Region);
        Assert.Equal(32, trust.Value.Key.Length);
    }

    [Theory]
    [InlineData("Sli:Import:Directory", "relative/inbox")]
    [InlineData("Sli:Import:Producers:edge-a:Source", "Customer")]
    [InlineData("Sli:Import:Producers:edge-a:Region", "customer/content")]
    [InlineData("Sli:Import:Producers:edge-a:Key", "AAAA")]
    public void Invalid_import_configuration_cannot_enable_a_permissive_collector(string setting, string value)
    {
        Assert.Throws<InvalidOperationException>(() => SliImportOptions.FromConfiguration(Configuration(setting, value)));
    }

    private static IConfiguration Configuration(string? setting = null, string? value = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Sli:Import:Enabled"] = "true", ["Sli:Import:Directory"] = Path.GetFullPath("unused-private-inbox"),
            ["Sli:Import:Producers:edge-a:Source"] = "Edge", ["Sli:Import:Producers:edge-a:Region"] = "test",
            ["Sli:Import:Producers:edge-a:Key"] = Convert.ToBase64String(new byte[32])
        };
        if (setting is not null) values[setting] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
