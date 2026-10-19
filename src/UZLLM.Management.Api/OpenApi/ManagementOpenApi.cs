using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;
using UZLLM.Management.Api.Administration;
using UZLLM.Management.Api.Budgets;
using UZLLM.Management.Api.Catalog;
using UZLLM.Modules.ApiKeys.Contracts;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Notifications.Infrastructure;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Organizations.Infrastructure;
using UZLLM.Modules.Payments.Contracts;
using UZLLM.Modules.Projects.Contracts;
using UZLLM.Modules.Providers.Contracts;
using UZLLM.Modules.Providers.Infrastructure;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Management.Api.OpenApi;

public static class ManagementOpenApi
{
    public static IServiceCollection AddManagementOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi("v1", options =>
        {
            options.ShouldInclude = description => description.RelativePath?.StartsWith("management/v1/",
                StringComparison.Ordinal) == true;
            options.AddOperationTransformer<ManagementOperationTransformer>();
            options.AddSchemaTransformer((schema, context, _) =>
            {
                // Output numbers are JSON numbers. HTTP input coercion is not a client contract.
                if (schema.Type is { } type && (type & (JsonSchemaType.Integer | JsonSchemaType.Number)) != 0)
                    schema.Type = type & ~JsonSchemaType.String;
                if (context.JsonTypeInfo.Type == typeof(ApiKeyPermissions) && schema.Properties is not null)
                    foreach (var property in schema.Properties.Values.OfType<OpenApiSchema>())
                        property.MaxItems = 128;
                if (context.JsonTypeInfo.Type == typeof(TenantLimits) && schema.Properties is { } limitProperties)
                {
                    schema.AdditionalPropertiesAllowed = false;
                    foreach (var (name, property) in limitProperties)
                        if (property is OpenApiSchema value)
                        {
                            value.Minimum = "1";
                            value.Maximum = name == "requestsPerMinute" ? "1000000" : "10000";
                        }
                }
                if ((context.JsonTypeInfo.Type == typeof(PublishProviderDataPolicyRequest)
                    || context.JsonTypeInfo.Type == typeof(ProviderDataPolicyResponse)) && schema.Properties is { } policyProperties)
                {
                    foreach (var (name, values) in new Dictionary<string, string[]>
                    {
                        ["retention"] = ["Unknown", "MayRetain", "NoRetentionReported"],
                        ["training"] = ["Unknown", "MayTrain", "NoTrainingReported"],
                        ["residency"] = ["Unknown", "ProviderReported"]
                    })
                        if (policyProperties[name] is OpenApiSchema classification)
                            classification.Enum = values.Select(value => (JsonNode)JsonValue.Create(value)!).ToList();
                    if (policyProperties["regions"] is OpenApiSchema regions)
                    {
                        regions.MaxItems = 16;
                        if (regions.Items is OpenApiSchema item) { item.MinLength = 1; item.MaxLength = 80; }
                    }
                    foreach (var (name, minimum, maximum) in new[] { ("scope", 8, 500), ("conditions", 8, 2000), ("sourceUrl", 1, 2048) })
                        if (policyProperties[name] is OpenApiSchema text) { text.MinLength = minimum; text.MaxLength = maximum; }
                    if (policyProperties.TryGetValue("reason", out var reasonProperty) && reasonProperty is OpenApiSchema reason)
                    { reason.MinLength = 8; reason.MaxLength = 500; }
                    schema.Description = "Dated, conditional provider-reported information only. Unknown is explicit; no ZDR or residency guarantee and no routing enforcement.";
                }
                if (context.JsonTypeInfo.Type == typeof(ProjectSettings) && schema.Properties is { } settingsProperties)
                {
                    schema.AdditionalPropertiesAllowed = false;
                    foreach (var name in new[] { "allowedModels", "allowedProviders" })
                        if (settingsProperties[name] is OpenApiSchema list) list.MaxItems = 128;
                    if (settingsProperties["routing"] is OpenApiSchema routing)
                    {
                        routing.Type = JsonSchemaType.String | JsonSchemaType.Null;
                        routing.Enum = new[] { "priority", "price", "latency", "throughput", "auto" }
                            .Select(value => (JsonNode)JsonValue.Create(value)!).Append(null!).ToList();
                    }
                    if (settingsProperties["environment"] is OpenApiSchema environment)
                    {
                        environment.MinLength = 1; environment.MaxLength = 32;
                        environment.Pattern = "^[A-Za-z0-9_-]+$";
                    }
                }
                // Optional record-constructor defaults must also accept the actual
                // null serialized by newly issued/unrestricted keys.
                if (context.JsonTypeInfo.Type == typeof(GatewayApiKey)
                    && schema.Properties is { } properties && properties.TryGetValue("permissions", out var permissionSchema)
                    && permissionSchema is OpenApiSchema permissions)
                    permissions.Type = JsonSchemaType.Object | JsonSchemaType.Null;
                return Task.CompletedTask;
            });
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new() { Title = "UZLLM Management API", Version = "1.0.0",
                    Description = "Implemented session-backed v1 contracts. All responses are no-store. "
                        + "Breaking changes require a new path and 180 days of migration overlap. "
                        + "Payment callbacks and Telegram ingress have separate provider-native protocols." };
                document.Servers = [new() { Url = "/" }];
                var paths = new OpenApiPaths();
                foreach (var entry in document.Paths) paths.Add(entry.Key.TrimEnd('/'), entry.Value);
                document.Paths = paths;
                document.Components ??= new();
                // GetOrCreateSchemaAsync can inline the array item without its
                // nullable approval arm. Fix this property only, not the shared
                // approval definition (which must stay non-null for oneOf refs).
                if (document.Components.Schemas?.TryGetValue(nameof(AdminWalletAdjustmentResponse) + "List", out var adjustments) == true
                    && adjustments.Items?.Properties is { } adjustmentProperties)
                    adjustmentProperties["approval"] = new OpenApiSchema { AnyOf =
                        [new OpenApiSchema { Type = JsonSchemaType.Null },
                         new OpenApiSchemaReference(nameof(AdminWalletAdjustmentApprovalResponse), document)] };
                document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
                {
                    ["Session"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey,
                        In = ParameterLocation.Cookie, Name = IdentityCookieNames.Session,
                        Description = "Opaque HttpOnly Secure SameSite=Strict server-backed session." }
                };
                return Task.CompletedTask;
            });
        });
        return services;
    }
}

public sealed class ManagementOperationTransformer : IOpenApiOperationTransformer
{
    private sealed record Response(Type? Type, int[] Success, int[] Errors);
    private static readonly IReadOnlyDictionary<string, Response> Responses = CreateResponses();
    private static readonly HashSet<string> PublicAuth =
        ["register", "login", "verify-email", "recover", "reset-password"];

    public async Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var path = System.Text.RegularExpressions.Regex.Replace("/" + context.Description.RelativePath,
            @"\{([^}:]+):[^}]+\}", "{$1}").TrimEnd('/');
        var method = context.Description.HttpMethod!;
        if (!Responses.TryGetValue(method + " " + path, out var response))
            throw new InvalidOperationException($"Document the new management operation: {method} {path}");
        operation.OperationId ??= method.ToLowerInvariant() + "_" + path.Trim('/').Replace('/', '_')
            .Replace("{", "").Replace("}", "").Replace('-', '_');
        operation.Responses = new();
        foreach (var status in response.Success)
        {
            var result = new OpenApiResponse { Description = status is 202 or 204 ? "Accepted; empty body." : "Success." };
            if (path.EndsWith("/usage/export", StringComparison.Ordinal))
            {
                result.Content = new Dictionary<string, OpenApiMediaType>
                { ["text/csv"] = new() { Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" } } };
                result.Headers = new Dictionary<string, IOpenApiHeader>();
                foreach (var header in new[] { "Content-Disposition", "X-Content-Type-Options", "X-Usage-From",
                    "X-Usage-To", "X-Usage-Data-As-Of", "X-Usage-Source", "X-Usage-Pending-Buckets", "X-Usage-Row-Count" })
                    result.Headers[header] = new OpenApiHeader { Schema = new OpenApiSchema { Type = JsonSchemaType.String } };
            }
            else if (response.Type is { } type)
                result.Content = new Dictionary<string, OpenApiMediaType>
                { ["application/json"] = new() { Schema = await SchemaForAsync(type, context, cancellationToken) } };
            operation.Responses[status.ToString()] = result;
        }
        foreach (var status in response.Errors)
        {
            var result = new OpenApiResponse { Description = status switch
            {
                401 => "Missing, expired or invalid session/credentials; empty body.",
                403 => "Tenant/role, recent operator MFA or CSRF proof denied; empty body.",
                404 => "Resource absent or outside the authorized scope; empty body.",
                429 => "Attempt limit reached; Retry-After is in seconds; empty body.",
                500 => "Policy update failed; safe Problem Details, no successful mutation response.",
                _ => "Rejected request. Body may be empty; JSON error and Problem Details forms are endpoint-specific."
            } };
            if (status is 400 or 409 or 503
                || path.EndsWith("/usage/export", StringComparison.Ordinal) && status is 413 or 504)
            {
                result.Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new() { Schema = await SchemaForAsync(
                        typeof(ManagementErrorResponse), context, cancellationToken) },
                    ["application/problem+json"] = new() { Schema = new OpenApiSchema { AnyOf =
                    [await SchemaForAsync(typeof(Microsoft.AspNetCore.Mvc.ProblemDetails), context, cancellationToken),
                        await SchemaForAsync(typeof(HttpValidationProblemDetails), context, cancellationToken)] } }
                };
            }
            if (status == 429) result.Headers = new Dictionary<string, IOpenApiHeader>
                { ["Retry-After"] = new OpenApiHeader { Schema = new OpenApiSchema { Type = JsonSchemaType.String } } };
            if (status == 500) result.Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/problem+json"] = new() { Schema = await SchemaForAsync(
                    typeof(Microsoft.AspNetCore.Mvc.ProblemDetails), context, cancellationToken) }
            };
            operation.Responses[status.ToString()] = result;
        }
        foreach (OpenApiResponse result in operation.Responses.Values)
        {
            result.Headers ??= new Dictionary<string, IOpenApiHeader>();
            result.Headers["Cache-Control"] = new OpenApiHeader
                { Schema = new OpenApiSchema { Type = JsonSchemaType.String, Enum = [System.Text.Json.Nodes.JsonValue.Create("no-store")] } };
        }
        var anonymous = path.StartsWith("/management/v1/auth/", StringComparison.Ordinal)
            && PublicAuth.Contains(path.Split('/')[^1]);
        if (!anonymous)
        {
            operation.Security = [new OpenApiSecurityRequirement
                { [new OpenApiSecuritySchemeReference("Session", context.Document)] = [] }];
            if (method != "GET") AddHeader(operation, IdentityCookieNames.CsrfHeader, true,
                "Must match the readable CSRF cookie and the current session.");
        }
        if (method == "POST" && path.EndsWith("/billing/topups", StringComparison.Ordinal))
            AddHeader(operation, "Idempotency-Key", true, "Replay is valid only for the same immutable quote.");
        if (path.Contains("/admin/", StringComparison.Ordinal))
            operation.Description = "Operator grant required. Except /access, recent MFA (15 minutes) is mandatory.";
        if (path.EndsWith("/usage/export", StringComparison.Ordinal))
        {
            // Shared parameter binding rejects HTTP paging on complete exports.
            // Do not advertise rejected paging parameters to generated clients.
            foreach (var paging in operation.Parameters?.Where(parameter => parameter.Name is "limit" or "cursor").ToArray() ?? [])
                operation.Parameters!.Remove(paging);
            foreach (var parameter in operation.Parameters?.OfType<OpenApiParameter>() ?? [])
            {
                if (parameter.Name == "kind") parameter.Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.String, Default = System.Text.Json.Nodes.JsonValue.Create("activity"),
                    Enum = [System.Text.Json.Nodes.JsonValue.Create("activity"), System.Text.Json.Nodes.JsonValue.Create("daily")]
                };
                if (parameter.Name == "rowLimit") parameter.Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.Integer, Minimum = "1", Maximum = "5000",
                    Default = System.Text.Json.Nodes.JsonValue.Create(1000)
                };
            }
        }
    }

    private static void AddHeader(OpenApiOperation operation, string name, bool required, string description)
    {
        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter { Name = name, In = ParameterLocation.Header,
            Required = required, Description = description, Schema = new OpenApiSchema { Type = JsonSchemaType.String } });
    }

    private static async Task<IOpenApiSchema> SchemaForAsync(Type type,
        OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var name = type.IsArray ? type.GetElementType()!.Name + "List" : type.Name;
        var document = context.Document ?? throw new InvalidOperationException("An OpenAPI document is required.");
        document.Components ??= new();
        var schemas = document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        if (!schemas.ContainsKey(name))
            schemas[name] = await context.GetOrCreateSchemaAsync(type,
                cancellationToken: cancellationToken);
        return new OpenApiSchemaReference(name, document);
    }

    private static IReadOnlyDictionary<string, Response> CreateResponses()
    {
        var result = new Dictionary<string, Response>(StringComparer.Ordinal);
        const string root = "/management/v1";
        const string org = "/organizations/{organizationId}";
        const string project = org + "/projects/{projectId}";
        void Add(string method, string path, Type? type = null, int success = 200, params int[] errors) =>
            result.Add(method + " " + root + path, new(type, [success], errors));
        void Read<T>(string path, params int[] errors) => Add("GET", path, typeof(T), 200, [401, 403, .. errors]);
        void Write<T>(string method, string path, int success = 200, params int[] errors) =>
            Add(method, path, typeof(T) == typeof(void) ? null : typeof(T), success, [400, 401, 403, .. errors]);
        void Empty(string method, string path, params int[] errors) =>
            Add(method, path, null, 204, [400, 401, 403, .. errors]);
        void Replay(string path) => result["POST " + root + path] = result["POST " + root + path] with { Success = [200, 201] };

        Add("POST", "/auth/register", null, 202, 400, 413, 429, 503);
        Add("POST", "/auth/recover", null, 202, 413, 429, 503);
        Add("POST", "/auth/login", null, 204, 400, 401, 413, 429, 503);
        foreach (var path in new[] { "verify-email", "reset-password" })
            Add("POST", "/auth/" + path, null, 204, 400, 413, 429, 503);
        Empty("POST", "/auth/logout");
        Read<UZLLM.Modules.Identity.Infrastructure.IdentityServiceCollectionExtensions.SessionResponse>("/auth/session");
        foreach (var path in new[] { "verify", "confirm" }) Empty("POST", "/auth/operator/mfa/" + path);
        Write<OperatorMfaEnrollment>("POST", "/auth/operator/mfa/enroll", 200, 409);
        Read<Organization[]>("/organizations");
        Write<Organization>("POST", "/organizations", 201);
        Read<Project[]>(org + "/projects");
        Write<Project>("POST", org + "/projects", 201, 409);
        Read<Project>(project, 404);
        Read<ProjectSettings>(project + "/settings", 404);
        Write<ProjectSettings>("PUT", project + "/settings", 200, 404);
        Empty("POST", project + "/archive", 404);
        Read<GatewayApiKey[]>("/projects/{projectId}/api-keys", 404);
        Write<IssuedGatewayApiKey>("POST", "/projects/{projectId}/api-keys", 201, 404, 409);
        Empty("PATCH", "/api-keys/{apiKeyId}", 404);
        Empty("PUT", "/api-keys/{apiKeyId}/permissions", 404);
        Read<TenantLimitView>("/projects/{projectId}/limits", 404);
        Write<TenantLimitView>("PUT", "/projects/{projectId}/limits", 200, 404);
        Read<TenantLimitView>("/api-keys/{apiKeyId}/limits", 404);
        Write<TenantLimitView>("PUT", "/api-keys/{apiKeyId}/limits", 200, 404);
        Write<IssuedGatewayApiKey>("POST", "/api-keys/{apiKeyId}/rotate", 200, 404, 409);
        Read<BudgetPolicyResponse[]>("/projects/{projectId}/budgets", 404);
        Write<BudgetPolicyResponse>("PUT", "/projects/{projectId}/budgets/{period}", 200, 404, 409);
        Read<CustomerCatalogResponse>(project + "/catalog/models", 404, 503);
        Read<ProviderDataPolicyResponse>(project + "/catalog/providers/{providerCode}/data-policy", 400, 404);
        Read<AdminProviderDataPolicyResponse[]>("/admin/providers/{providerId}/data-policies", 400, 409);
        Read<AdminProviderDataPolicyResponse>("/admin/provider-data-policies/{id}", 400, 404, 409);
        Write<AdminProviderDataPolicyResponse>("POST", "/admin/providers/{providerId}/data-policies", 201, 404, 409);
        Read<WalletResponse>(org + "/billing/wallet", 404);
        Write<PaymentQuoteResponse>("POST", org + "/billing/quotes", 200, 503);
        Write<CreatedTopUpResponse>("POST", org + "/billing/topups", 201, 404, 409);
        Replay(org + "/billing/topups");
        Read<CustomerTopUp[]>(org + "/billing/topups");
        Read<PaymentIntentResponse>(org + "/billing/topups/{intentId}", 404);
        Read<CustomerSettlementRefund[]>(org + "/billing/refunds");
        Read<UsageActivityPage>(org + "/usage/activity", 400);
        Read<UsageRequestDetail>(org + "/usage/requests/{requestId}", 404);
        Read<UsageSummary>(org + "/usage/summary", 400);
        Read<UsageTimeSeries>(org + "/usage/timeseries", 400);
        Add("GET", org + "/usage/export", null, 200, 400, 401, 403, 413, 504);
        foreach (var dimension in new[] { "model", "provider", "api-key", "project" })
            Read<UsageBreakdown>(org + "/usage/by-" + dimension, 400);
        Read<PayloadRetentionPolicy>(project + "/payload-retention", 404);
        Write<PayloadRetentionPolicy>("PUT", project + "/payload-retention", 200, 404, 500, 503);
        Read<RetainedPayload>(project + "/payload-retention/requests/{requestId}", 404);
        Read<TeamMemberResponse[]>(org + "/team/members");
        Read<TeamInvitationResponse[]>(org + "/team/invitations");
        Write<TeamInvitationResponse>("POST", org + "/team/invitations", 201, 409, 413, 429, 503);
        Write<TeamMemberResponse>("POST", "/team/invitations/accept", 200, 404, 413, 429, 503);
        Empty("PATCH", org + "/team/members/{accountId}/role", 404, 409);
        Empty("DELETE", org + "/team/members/{accountId}", 404, 409);
        foreach (var method in new[] { "PUT", "DELETE" })
            Empty(method, org + "/team/members/{accountId}/projects/{projectId}", 404, 409);
        Read<ByokCredentialResponse[]>(org + "/provider-keys");
        Read<ByokProviderOption[]>(org + "/provider-keys/providers");
        Read<ByokCredentialResponse>(org + "/provider-keys/{id}", 404);
        Write<ByokCredentialResponse>("POST", org + "/provider-keys", 201, 404);
        Write<ByokCredentialResponse>("PATCH", org + "/provider-keys/{id}", 200, 404, 409);
        Write<ByokTestResponse>("POST", org + "/provider-keys/{id}/test", 200, 404, 409);
        Write<ByokCredentialResponse>("PUT", org + "/provider-keys/{id}/restrictions", 200, 404);
        Empty("POST", org + "/provider-keys/{id}/disable", 404);
        Empty("DELETE", org + "/provider-keys/{id}", 404);
        foreach (var method in new[] { "PUT", "DELETE" }) Empty(method, org + "/provider-keys/{id}/projects/{projectId}", 404);
        Read<AlertRuleView[]>(org + "/alerts/rules");
        Read<AlertEventView[]>(org + "/alerts/events");
        Write<AlertRuleView>("POST", org + "/alerts/rules", 201, 404);
        Empty("PATCH", org + "/alerts/rules/{ruleId}", 404);
        Read<AlertDestinationView[]>(org + "/alerts/destinations");
        Write<TelegramLink>("POST", org + "/alerts/telegram/link", 200, 503);
        Write<WebhookDestinationCreated>("POST", org + "/alerts/webhook/destination", 201, 409, 503);
        Empty("DELETE", org + "/alerts/destinations/{destinationId}", 404);

        Read<OperatorAccessResponse>("/admin/access");
        Read<AdminAccountResponse[]>("/admin/accounts", 400, 404, 409);
        Read<AdminOrganizationResponse[]>("/admin/organizations", 400, 404, 409);
        Read<AdminProviderResponse[]>("/admin/providers", 400, 404, 409);
        Read<AdminPriceResponse[]>("/admin/mappings/{id}/prices", 400, 404, 409);
        Read<AdminFeePolicyResponse[]>("/admin/fee-policies/{code}", 400, 404, 409);
        Read<AdminFxRateResponse[]>("/admin/fx-rates", 400, 404, 409);
        Read<AdminSettlementRefundResponse[]>("/admin/refunds", 400, 404, 409);
        Read<AdminWalletAdjustmentResponse[]>("/admin/wallet-adjustments", 400, 404, 409);
        Read<AdminWalletAdjustmentResponse>("/admin/wallet-adjustments/{id}", 400, 404, 409);
        Write<AdminWalletAdjustmentResponse>("POST", "/admin/wallet-adjustments", 201, 404, 409);
        Replay("/admin/wallet-adjustments");
        Write<AdminWalletAdjustmentResponse>("POST", "/admin/wallet-adjustments/{id}/approve", 200, 404, 409);
        Read<AdminSettlementRefundResponse>("/admin/refunds/{id}", 400, 404, 409);
        Read<AdminLedgerEntryResponse[]>("/admin/organizations/{id}/ledger", 400, 404, 409);
        Read<AdminPaymentResponse[]>("/admin/payments", 400, 404, 409);
        Read<AdminAuditResponse[]>("/admin/audit", 400, 404, 409);
        Read<AdminPlatformControlResponse[]>("/admin/controls", 400, 404, 409);
        Read<DeadLetteredWork[]>("/admin/work/dead-letters", 400, 404, 409);
        Read<OperationalAlertDelivery[]>("/admin/work/alerts", 400, 404, 409);
        Read<AdminFinancialRiskResponse>("/admin/financial/risk", 400, 404, 409);
        Read<PaymentReconciliationCase[]>("/admin/payment-reconciliation/cases", 400, 404, 409);
        Read<AdminProviderObservationResponse>("/admin/payment-reconciliation/observations/{id}", 400, 404, 409);
        Write<ProviderObservationResult>("POST", "/admin/payment-reconciliation/observations", 201, 404, 409);
        Replay("/admin/payment-reconciliation/observations");
        Empty("PATCH", "/admin/payment-reconciliation/cases/{id}/resolve", 404, 409);
        Empty("POST", "/admin/operators/{accountId}/mfa/reset", 404, 409);
        foreach (var path in new[] { "providers", "models", "mappings", "credentials", "prices", "fee-policies", "fx-rates" })
            Write<CreatedResourceResponse>("POST", "/admin/" + path, 201, 404, 409);
        Write<AdminSettlementRefundResponse>("POST", "/admin/refunds", 201, 404, 409);
        Replay("/admin/refunds");
        foreach (var path in new[] { "providers", "models", "mappings", "credentials" })
            Empty("PATCH", "/admin/" + path + "/{id}", 404, 409);
        Empty("PATCH", "/admin/controls/{feature}", 404, 409);
        return result;
    }
}
