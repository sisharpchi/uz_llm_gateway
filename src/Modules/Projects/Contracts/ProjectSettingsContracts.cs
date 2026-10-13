using System.Text.Json.Serialization;

namespace UZLLM.Modules.Projects.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProjectSettings(string? Routing = null, string? Environment = null,
    IReadOnlyList<string>? AllowedModels = null, IReadOnlyList<string>? AllowedProviders = null);

public interface IProjectSettingsStore
{
    Task<bool> ReplaceAsync(Guid organizationId, Guid projectId, ProjectSettings settings,
        CancellationToken cancellationToken = default);
}

public interface IProjectSettingsService
{
    Task<ProjectSettings> ReadAsync(Guid accountId, Guid organizationId, Guid projectId,
        CancellationToken cancellationToken = default);
    Task<ProjectSettings> ReplaceAsync(Guid accountId, Guid organizationId, Guid projectId,
        ProjectSettings settings, CancellationToken cancellationToken = default);
}
