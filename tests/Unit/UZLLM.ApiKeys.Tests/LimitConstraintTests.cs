using UZLLM.Modules.ApiKeys.Application;
using UZLLM.Modules.ApiKeys.Contracts;

namespace UZLLM.ApiKeys.Tests;

public sealed class LimitConstraintTests
{
    private readonly RequestConstraintValidator validator = new();

    [Fact]
    public void Known_tokens_at_context_boundary_and_supported_features_are_allowed()
    {
        var result = validator.Validate(new RequestConstraintInput(8192, 4096, 2048,
            6144, ["TOOLS", "json"], ["tools", "json", "vision"]));

        Assert.Equal(RequestConstraintOutcome.Allowed, result.Outcome);
        Assert.Null(result.Capability);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4097)]
    public void Invalid_output_limit_is_rejected(int requested)
    {
        var result = validator.Validate(new RequestConstraintInput(8192, 4096, requested,
            1, [], []));

        Assert.Equal(RequestConstraintOutcome.InvalidOutputLimit, result.Outcome);
    }

    [Fact]
    public void Input_plus_output_one_token_over_context_is_rejected()
    {
        var result = validator.Validate(new RequestConstraintInput(8192, 4096, 2048,
            6145, [], []));

        Assert.Equal(RequestConstraintOutcome.ContextExceeded, result.Outcome);
    }

    [Fact]
    public void Output_alone_over_context_is_rejected_when_input_estimate_is_unknown()
    {
        var result = validator.Validate(new RequestConstraintInput(1024, 4096, 2048,
            null, [], []));

        Assert.Equal(RequestConstraintOutcome.ContextExceeded, result.Outcome);
    }

    [Fact]
    public void Unknown_input_size_does_not_claim_a_context_validation_it_cannot_make()
    {
        var result = validator.Validate(new RequestConstraintInput(8192, 4096, 2048,
            null, [], []));

        Assert.Equal(RequestConstraintOutcome.Allowed, result.Outcome);
    }

    [Fact]
    public void Unsupported_required_capability_names_the_missing_feature()
    {
        var result = validator.Validate(new RequestConstraintInput(8192, 4096, 2048,
            1, ["tools", "vision"], ["tools", "json"]));

        Assert.Equal(RequestConstraintOutcome.UnsupportedCapability, result.Outcome);
        Assert.Equal("vision", result.Capability);
    }

    [Fact]
    public void Negative_input_estimate_is_rejected_as_invalid_contract()
    {
        Assert.Throws<ArgumentException>(() => validator.Validate(new RequestConstraintInput(
            8192, 4096, 2048, -1, [], [])));
    }
}
