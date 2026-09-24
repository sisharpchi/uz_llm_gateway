using UZLLM.Modules.ApiKeys.Contracts;

namespace UZLLM.Modules.ApiKeys.Application;

public sealed class RequestConstraintValidator : IRequestConstraintValidator
{
    public RequestConstraintDecision Validate(RequestConstraintInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.ContextLength <= 0 || input.ModelMaxOutputTokens <= 0
            || input.RequiredCapabilities is null || input.SupportedCapabilities is null
            || input.EstimatedInputTokens is < 0)
            throw new ArgumentException("Valid model limits, token estimate, and capabilities are required.", nameof(input));

        if (input.RequestedMaxOutputTokens <= 0
            || input.RequestedMaxOutputTokens > input.ModelMaxOutputTokens)
            return new RequestConstraintDecision(RequestConstraintOutcome.InvalidOutputLimit, null);
        if (input.RequestedMaxOutputTokens > input.ContextLength
            || input.EstimatedInputTokens is { } estimate
                && (long)estimate + input.RequestedMaxOutputTokens > input.ContextLength)
            return new RequestConstraintDecision(RequestConstraintOutcome.ContextExceeded, null);

        var supported = new HashSet<string>(input.SupportedCapabilities, StringComparer.OrdinalIgnoreCase);
        var unsupported = input.RequiredCapabilities.FirstOrDefault(value => !supported.Contains(value));
        return unsupported is null
            ? new RequestConstraintDecision(RequestConstraintOutcome.Allowed, null)
            : new RequestConstraintDecision(RequestConstraintOutcome.UnsupportedCapability, unsupported);
    }
}
