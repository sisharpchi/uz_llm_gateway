using System.Text.Json;
using UZLLM.Modules.Projects.Contracts;

namespace UZLLM.Modules.Projects.Domain;

public static class ProjectSettingsPolicy
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ProjectSettings Normalize(ProjectSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var routing = settings.Routing;
        if (routing is not (null or "priority" or "price" or "latency" or "throughput" or "auto"))
            throw new ArgumentException("Unsupported project routing strategy.");
        var environment = settings.Environment;
        if (environment is not null && (environment.Length is < 1 or > 32
            || environment.Any(value => !char.IsAsciiLetterOrDigit(value) && value is not '-' and not '_')))
            throw new ArgumentException("Environment must contain 1–32 ASCII letters, digits, '-' or '_'.");
        return new(routing, environment, Codes(settings.AllowedModels), Codes(settings.AllowedProviders));
    }

    public static ProjectSettings Read(string settingsJson)
    {
        using var document = JsonDocument.Parse(settingsJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Invalid stored project settings.");
        return document.RootElement.TryGetProperty("defaults", out var value)
            ? Normalize(value.Deserialize<ProjectSettings>(Json)
                ?? throw new InvalidOperationException("Invalid stored project defaults.")) : new();
    }

    public static string Serialize(ProjectSettings settings) => JsonSerializer.Serialize(settings, Json);
    public static string? ResolveRouting(ProjectSettings settings, string? requested) =>
        (requested ?? settings.Routing) is { } routing && routing != "priority" ? routing : null;
    public static bool AllowsModel(ProjectSettings settings, string code) =>
        settings.AllowedModels is null || settings.AllowedModels.Contains(code, StringComparer.Ordinal);
    public static bool AllowsProvider(ProjectSettings settings, string code) =>
        settings.AllowedProviders is null || settings.AllowedProviders.Contains(code, StringComparer.Ordinal);

    private static IReadOnlyList<string>? Codes(IReadOnlyList<string>? codes)
    {
        if (codes is null) return null;
        if (codes.Count > 128) throw new ArgumentException("At most 128 codes are allowed per list.");
        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var code in codes)
        {
            var value = code?.Trim().ToLowerInvariant();
            if (value is null || value.Length is < 1 or > 200 || !char.IsAsciiLetterOrDigit(value[0])
                || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.' and not ':'))
                throw new ArgumentException("Use canonical model/provider codes (1–200 ASCII code characters).");
            result.Add(value);
        }
        return Array.AsReadOnly(result.ToArray());
    }
}
