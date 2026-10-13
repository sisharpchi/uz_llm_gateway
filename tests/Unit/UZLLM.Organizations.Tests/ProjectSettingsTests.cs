using System.Text.Json;
using UZLLM.Modules.Projects.Contracts;
using UZLLM.Modules.Projects.Domain;

namespace UZLLM.Organizations.Tests;

public sealed class ProjectSettingsTests
{
    [Theory]
    [InlineData(null, null, null)]
    [InlineData("price", null, "price")]
    [InlineData("price", "priority", null)]
    [InlineData("latency", "throughput", "throughput")]
    [InlineData("priority", "auto", "auto")]
    public void Default_inheritance_and_explicit_strategy_override_do_not_change_restrictions(
        string? configured, string? requested, string? expected)
    {
        var settings = new ProjectSettings(configured, "Production", ["gpt-test"], ["openai"]);
        Assert.Equal(expected, ProjectSettingsPolicy.ResolveRouting(settings, requested));
        Assert.True(ProjectSettingsPolicy.AllowsModel(settings, "gpt-test"));
        Assert.False(ProjectSettingsPolicy.AllowsModel(settings, "other-test"));
        Assert.False(ProjectSettingsPolicy.AllowsProvider(settings, "anthropic"));
        Assert.Equal(new ProjectSettings(), ProjectSettingsPolicy.Read("{}"));
    }

    [Fact]
    public void Allowlists_distinguish_inheritance_from_deny_all_and_copy_normalized_input()
    {
        string[] codes = [" GPT-TEST ", "gpt-test", "other:test"];
        var settings = ProjectSettingsPolicy.Normalize(new(AllowedModels: codes, AllowedProviders: []));
        codes[0] = "different";
        Assert.Equal(new[] { "gpt-test", "other:test" }, settings.AllowedModels);
        Assert.False(ProjectSettingsPolicy.AllowsProvider(settings, "openai"));
        Assert.True(ProjectSettingsPolicy.AllowsProvider(new(), "openai"));
        Assert.False(ProjectSettingsPolicy.AllowsModel(settings, "GPT-TEST"));
        Assert.Equal(settings.AllowedModels, ProjectSettingsPolicy.Normalize(settings).AllowedModels);
    }

    [Theory]
    [InlineData("{\"routing\":\"unknown\"}")]
    [InlineData("{\"environment\":\"\"}")]
    [InlineData("{\"environment\":\"<prod>\"}")]
    [InlineData("{\"allowedModels\":[\"openai/gpt-test\"]}")]
    [InlineData("{\"allowedProviders\":[\"*\"]}")]
    public void Invalid_settings_are_rejected(string json) => Assert.Throws<ArgumentException>(() =>
        ProjectSettingsPolicy.Read("{\"defaults\":" + json + "}"));

    [Theory]
    [InlineData("max_cost")]
    [InlineData("max_cost_usd")]
    [InlineData("provider_key_id")]
    [InlineData("allow_managed_fallback")]
    [InlineData("budget")]
    [InlineData("cache")]
    public void Settings_cannot_enable_execution_modes_or_duplicate_financial_controls(string field) =>
        Assert.Throws<JsonException>(() => ProjectSettingsPolicy.Read("{\"defaults\":{\"" + field + "\":1}}"));

    [Fact]
    public void Settings_enforce_exact_bounds_and_read_legacy_siblings()
    {
        var maximum = ProjectSettingsPolicy.Normalize(new("auto", new string('a', 32),
            Enumerable.Range(0, 128).Select(value => "model-" + value).ToArray(), [new string('a', 200)]));
        Assert.Equal(128, maximum.AllowedModels!.Count);
        Assert.Throws<ArgumentException>(() => ProjectSettingsPolicy.Normalize(maximum with { Environment = new string('a', 33) }));
        Assert.Throws<ArgumentException>(() => ProjectSettingsPolicy.Normalize(maximum with { AllowedProviders = [new string('a', 201)] }));
        Assert.Throws<ArgumentException>(() => ProjectSettingsPolicy.Normalize(maximum with { AllowedModels = Enumerable.Repeat("m", 129).ToArray() }));
        Assert.Equal(new ProjectSettings(), ProjectSettingsPolicy.Read("{\"futureSibling\":true}"));
        Assert.Throws<InvalidOperationException>(() => ProjectSettingsPolicy.Read("[]"));
        Assert.Throws<InvalidOperationException>(() => ProjectSettingsPolicy.Read("{\"defaults\":null}"));
    }
}
