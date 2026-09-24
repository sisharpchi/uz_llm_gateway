using UZLLM.Modules.ApiKeys.Contracts;

namespace UZLLM.Gateway.Api.Inference;

public sealed record GatewayOptions(string FeePolicyCode, int MaximumRequestBytes,
    TimeSpan ProviderTimeout, TimeSpan ReservationLifetime, LimitPolicy Limits)
{
    public static GatewayOptions FromConfiguration(IConfiguration configuration)
    {
        var feeCode = configuration["Gateway:FeePolicyCode"] ?? "default";
        var bytes = configuration.GetValue("Gateway:MaximumRequestBytes", 1_048_576);
        var timeout = TimeSpan.FromSeconds(configuration.GetValue("Gateway:ProviderTimeoutSeconds", 120));
        var reservation = TimeSpan.FromSeconds(configuration.GetValue("Gateway:ReservationLifetimeSeconds", 900));
        var policy = new LimitPolicy(
            configuration.GetValue("Gateway:Limits:ApiKeyRpm", 60),
            configuration.GetValue("Gateway:Limits:ProjectRpm", 600),
            configuration.GetValue("Gateway:Limits:ApiKeyConcurrency", 8),
            configuration.GetValue("Gateway:Limits:ProjectConcurrency", 64),
            TimeSpan.FromSeconds(configuration.GetValue("Gateway:Limits:LeaseSeconds", 900)));
        if (string.IsNullOrWhiteSpace(feeCode) || feeCode.Length > 100 || bytes is < 1 or > 4_194_304
            || timeout < TimeSpan.FromSeconds(1) || timeout > TimeSpan.FromMinutes(10)
            || reservation < timeout || reservation > TimeSpan.FromHours(1)
            || policy.ApiKeyRequestsPerMinute <= 0 || policy.ProjectRequestsPerMinute <= 0
            || policy.ApiKeyConcurrency <= 0 || policy.ProjectConcurrency <= 0
            || policy.LeaseDuration < timeout || policy.LeaseDuration > TimeSpan.FromHours(1))
            throw new InvalidOperationException("Gateway limits, timeout, reservation, or fee policy are invalid.");
        return new GatewayOptions(feeCode, bytes, timeout, reservation, policy);
    }
}
