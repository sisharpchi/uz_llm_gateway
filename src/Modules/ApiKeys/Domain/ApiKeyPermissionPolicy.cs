using UZLLM.Modules.ApiKeys.Contracts;

namespace UZLLM.Modules.ApiKeys.Domain;

public static class ApiKeyPermissionPolicy
{
    public const int MaximumRulesPerList = 128;

    public static ApiKeyPermissions Normalize(ApiKeyPermissions permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        return new(NormalizeCodes(permissions.AllowedModels), NormalizeCodes(permissions.DeniedModels),
            NormalizeCodes(permissions.AllowedProviders), NormalizeCodes(permissions.DeniedProviders));
    }

    public static bool AllowsModel(ApiKeyPermissions? permissions, string canonicalModel) =>
        Allows(permissions?.AllowedModels, permissions?.DeniedModels, canonicalModel);

    public static bool AllowsProvider(ApiKeyPermissions? permissions, string provider) =>
        Allows(permissions?.AllowedProviders, permissions?.DeniedProviders, provider);

    private static bool Allows(IReadOnlyList<string>? allowed, IReadOnlyList<string>? denied, string code) =>
        (allowed is null || allowed.Contains(code, StringComparer.Ordinal))
        && !(denied?.Contains(code, StringComparer.Ordinal) ?? false);

    private static IReadOnlyList<string>? NormalizeCodes(IReadOnlyList<string>? codes)
    {
        if (codes is null) return null;
        if (codes.Count > MaximumRulesPerList)
            throw new ArgumentException($"Each permission list permits at most {MaximumRulesPerList} codes.");
        var normalized = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var code in codes)
        {
            var value = code?.Trim().ToLowerInvariant();
            // Match catalog code syntax; no provider prefixes, wildcards or aliases.
            if (value is null || value.Length is < 1 or > 200
                || !char.IsAsciiLetterOrDigit(value[0])
                || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.' and not ':'))
                throw new ArgumentException("Permission codes must start with an ASCII letter or digit and contain 1–200 ASCII letters, digits, '.', '_', ':' or '-'.");
            normalized.Add(value);
        }
        return Array.AsReadOnly(normalized.ToArray());
    }
}
