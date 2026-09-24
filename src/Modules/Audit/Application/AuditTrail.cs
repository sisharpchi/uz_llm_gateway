using System.Net;
using System.Text.Json;
using UZLLM.Modules.Audit.Contracts;

namespace UZLLM.Modules.Audit.Application;

public sealed class AuditTrail(IAuditEventStore store, TimeProvider timeProvider) : IAuditTrail
{
    public async Task<AuditEvent> RecordAsync(AuditEventInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateIdentifier(input.OrganizationId, nameof(input.OrganizationId));
        ValidateIdentifier(input.ActorAccountId, nameof(input.ActorAccountId));
        ValidateText(input.Action, 200, nameof(input.Action));
        ValidateText(input.ResourceType, 100, nameof(input.ResourceType));
        if (input.ResourceId == Guid.Empty)
        {
            throw new ArgumentException("Resource ID cannot be empty when supplied.", nameof(input.ResourceId));
        }

        if (!string.IsNullOrWhiteSpace(input.IpAddress) && !IPAddress.TryParse(input.IpAddress, out _))
        {
            throw new ArgumentException("IP address must be valid when supplied.", nameof(input.IpAddress));
        }

        var metadataJson = NormalizeMetadata(input.MetadataJson);
        var auditEvent = new AuditEvent(
            Guid.CreateVersion7(),
            input.OrganizationId,
            input.ActorAccountId,
            input.Action.Trim(),
            input.ResourceType.Trim(),
            input.ResourceId,
            input.IpAddress?.Trim(),
            metadataJson,
            timeProvider.GetUtcNow());
        await store.AppendAsync(auditEvent, cancellationToken);
        return auditEvent;
    }

    private static void ValidateIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty identifier is required.", parameterName);
        }
    }

    private static void ValidateText(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximumLength)
        {
            throw new ArgumentException($"A non-empty value up to {maximumLength} characters is required.", parameterName);
        }
    }

    private static string NormalizeMetadata(string? metadataJson)
    {
        var normalized = string.IsNullOrWhiteSpace(metadataJson) ? "{}" : metadataJson.Trim();
        try
        {
            using var document = JsonDocument.Parse(normalized);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Audit metadata must be a JSON object.", nameof(metadataJson));
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Audit metadata must be valid JSON.", nameof(metadataJson), exception);
        }

        return normalized;
    }
}
