namespace UZLLM.Management.Api.OpenApi;

public sealed record OperatorAccessResponse(bool RecentMfa);
public sealed record CreatedResourceResponse(Guid Id);
public sealed record ManagementErrorResponse(string Error);
