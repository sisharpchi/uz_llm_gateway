using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Payments.Contracts;

namespace UZLLM.Modules.Payments.Infrastructure;

/// <summary>Payme Merchant API response; exactly one of result or error is present.</summary>
public sealed record PaymeRpcResponse(
    [property: JsonPropertyName("id")] object? Id,
    [property: JsonPropertyName("result")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? Result,
    [property: JsonPropertyName("error")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PaymeRpcError? Error);

/// <summary>Payme's provider-native JSON-RPC error format.</summary>
public sealed record PaymeRpcError(
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("message")] object Message,
    [property: JsonPropertyName("data")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Data);

public sealed class PaymeMerchantApi(IPaymentService payments, PaymentConfiguration configuration)
{
    public async Task<PaymeRpcResponse> HandleAsync(string? authorization, string rawBody,
        CancellationToken cancellationToken = default)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(rawBody); }
        catch (JsonException) { return Failure(null, -32700, "Invalid JSON."); }
        using (document)
        {
            var root = document.RootElement;
            var id = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("id", out var idElement)
                ? (object)idElement.Clone() : null;
            if (string.IsNullOrWhiteSpace(configuration.PaymeKey))
                return Failure(id, -32400, "Merchant configuration is unavailable.");
            if (!VerifyBasicAuthorization(authorization, configuration.PaymeKey))
                return Failure(id, -32504, "Merchant authentication failed.");
            if (root.ValueKind != JsonValueKind.Object
                || !TryString(root, "method", out var method)
                || !root.TryGetProperty("params", out var parameters)
                || parameters.ValueKind != JsonValueKind.Object)
                return Failure(id, -32600, "Invalid request.");
            try
            {
                object? result = method switch
                {
                    "CheckPerformTransaction" => await CheckAsync(parameters, cancellationToken),
                    "CreateTransaction" => await CreateAsync(parameters, id, rawBody, cancellationToken),
                    "PerformTransaction" => await PerformAsync(parameters, id, rawBody, cancellationToken),
                    "CancelTransaction" => await CancelAsync(parameters, id, rawBody, cancellationToken),
                    "CheckTransaction" => await StatusAsync(parameters, cancellationToken),
                    "GetStatement" => await StatementAsync(parameters, cancellationToken),
                    _ => throw new PaymeProtocolException(-32601, "Unknown method.", method)
                };
                return new PaymeRpcResponse(id, result, null);
            }
            catch (PaymeProtocolException exception)
            {
                return Failure(id, exception.Code, exception.Message, exception.Field);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return Failure(id, -32400, "Merchant system error.");
            }
        }
    }

    private async Task<object> CheckAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var intentId = AccountIntentId(parameters);
        var amount = Amount(parameters);
        var result = await payments.CheckAsync(PaymentProvider.Payme, intentId, amount, cancellationToken);
        ThrowIfRejected(result, accountLookup: true);
        return new { allow = true };
    }

    private async Task<object> CreateAsync(JsonElement parameters, object? rpcId,
        string rawBody, CancellationToken cancellationToken)
    {
        var intentId = AccountIntentId(parameters);
        var amount = Amount(parameters);
        var externalId = RequiredString(parameters, "id");
        var time = RequiredLong(parameters, "time");
        if (time <= 0) throw new PaymeProtocolException(-32600, "Invalid transaction time.");
        var result = await payments.PrepareAsync(PaymentProvider.Payme, intentId, externalId,
            amount, time, ReceiptId("CreateTransaction", rpcId), Hash(rawBody), cancellationToken);
        ThrowIfRejected(result, accountLookup: true);
        return new
        {
            create_time = result.Intent!.BoundAt!.Value.ToUnixTimeMilliseconds(),
            transaction = result.Intent.Id.ToString("D"),
            state = State(result.Intent)
        };
    }

    private async Task<object> PerformAsync(JsonElement parameters, object? rpcId,
        string rawBody, CancellationToken cancellationToken)
    {
        var externalId = RequiredString(parameters, "id");
        var result = await payments.CompleteAsync(PaymentProvider.Payme, externalId, null, null,
            ReceiptId("PerformTransaction", rpcId), Hash(rawBody), cancellationToken);
        ThrowIfRejected(result);
        return new
        {
            transaction = result.Intent!.Id.ToString("D"),
            perform_time = result.Intent.PaidAt!.Value.ToUnixTimeMilliseconds(),
            state = 2
        };
    }

    private async Task<object> CancelAsync(JsonElement parameters, object? rpcId,
        string rawBody, CancellationToken cancellationToken)
    {
        var externalId = RequiredString(parameters, "id");
        var reason = RequiredInt(parameters, "reason");
        var result = await payments.CancelAsync(PaymentProvider.Payme, externalId, null,
            null, reason, ReceiptId("CancelTransaction", rpcId), Hash(rawBody), cancellationToken);
        ThrowIfRejected(result);
        return new
        {
            transaction = result.Intent!.Id.ToString("D"),
            cancel_time = result.Intent.CanceledAt!.Value.ToUnixTimeMilliseconds(),
            state = State(result.Intent)
        };
    }

    private async Task<object> StatusAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var externalId = RequiredString(parameters, "id");
        var intent = await payments.FindByExternalAsync(PaymentProvider.Payme, externalId,
            cancellationToken) ?? throw new PaymeProtocolException(-31003, "Transaction not found.");
        return StatusObject(intent);
    }

    private async Task<object> StatementAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var from = RequiredLong(parameters, "from");
        var to = RequiredLong(parameters, "to");
        IReadOnlyList<PaymentStatementEntry> entries;
        try { entries = await payments.GetPaymeStatementAsync(from, to, cancellationToken); }
        catch (ArgumentException) { throw new PaymeProtocolException(-32600, "Invalid statement range."); }
        return new
        {
            transactions = entries.Select(value => new
            {
                id = value.Intent.ExternalTransactionId,
                time = value.ProviderCreatedTimeUnixMs,
                amount = value.Intent.Amount.Value,
                account = new { intent_id = value.Intent.Id.ToString("D") },
                create_time = value.Intent.BoundAt!.Value.ToUnixTimeMilliseconds(),
                perform_time = value.Intent.PaidAt?.ToUnixTimeMilliseconds() ?? 0,
                cancel_time = value.Intent.CanceledAt?.ToUnixTimeMilliseconds() ?? 0,
                transaction = value.Intent.Id.ToString("D"),
                state = State(value.Intent),
                reason = value.Intent.CancelReason
            }).ToArray()
        };
    }

    private static object StatusObject(PaymentIntent intent) => new
    {
        create_time = intent.BoundAt?.ToUnixTimeMilliseconds() ?? 0,
        perform_time = intent.PaidAt?.ToUnixTimeMilliseconds() ?? 0,
        cancel_time = intent.CanceledAt?.ToUnixTimeMilliseconds() ?? 0,
        transaction = intent.Id.ToString("D"),
        state = State(intent),
        reason = intent.CancelReason
    };

    private static int State(PaymentIntent intent) => intent.Status switch
    {
        PaymentStatus.Created => 1,
        PaymentStatus.Paid => 2,
        PaymentStatus.Canceled when intent.PaidAt is not null => -2,
        PaymentStatus.Canceled => -1,
        _ => 0
    };

    private static void ThrowIfRejected(PaymentCommandResult result, bool accountLookup = false)
    {
        if (result.Status is PaymentCommandStatus.Accepted or PaymentCommandStatus.Duplicate) return;
        throw result.Status switch
        {
            PaymentCommandStatus.NotFound or PaymentCommandStatus.WrongProvider when accountLookup =>
                new PaymeProtocolException(-31050, "Payment intent not found.", "intent_id"),
            PaymentCommandStatus.NotFound => new PaymeProtocolException(-31003, "Transaction not found."),
            PaymentCommandStatus.WrongAmount => new PaymeProtocolException(-31001, "Incorrect amount."),
            _ => new PaymeProtocolException(-31008, "Transaction cannot be performed.")
        };
    }

    private static Guid AccountIntentId(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("account", out var account)
            || account.ValueKind != JsonValueKind.Object
            || !TryString(account, "intent_id", out var value)
            || !Guid.TryParse(value, out var id) || id == Guid.Empty)
            throw new PaymeProtocolException(-31050, "Payment intent not found.", "intent_id");
        return id;
    }

    private static UzsTiyinAmount Amount(JsonElement parameters)
    {
        var amount = RequiredLong(parameters, "amount");
        if (amount <= 0) throw new PaymeProtocolException(-31001, "Incorrect amount.");
        return new UzsTiyinAmount(amount);
    }

    private static string RequiredString(JsonElement value, string property)
    {
        if (!TryString(value, property, out var text) || string.IsNullOrWhiteSpace(text)
            || text.Length > 120)
            throw new PaymeProtocolException(-32600, $"Invalid {property}.");
        return text;
    }

    private static bool TryString(JsonElement value, string property, out string text)
    {
        text = string.Empty;
        if (!value.TryGetProperty(property, out var item) || item.ValueKind != JsonValueKind.String)
            return false;
        text = item.GetString() ?? string.Empty;
        return true;
    }

    private static long RequiredLong(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out var item) || item.ValueKind != JsonValueKind.Number
            || !item.TryGetInt64(out var number))
            throw new PaymeProtocolException(-32600, $"Invalid {property}.");
        return number;
    }

    private static int RequiredInt(JsonElement value, string property)
    {
        var number = RequiredLong(value, property);
        if (number is < int.MinValue or > int.MaxValue)
            throw new PaymeProtocolException(-32600, $"Invalid {property}.");
        return (int)number;
    }

    private static byte[] Hash(string rawBody) => SHA256.HashData(Encoding.UTF8.GetBytes(rawBody));

    private static string ReceiptId(string method, object? id)
    {
        var value = id?.ToString() ?? "none";
        if (value.Length > 80)
            value = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        return $"{method}:{value}";
    }

    private static PaymeRpcResponse Failure(object? id, int code, string message, string? data = null) =>
        new(id, null, new PaymeRpcError(code, new { en = message, uz = message, ru = message }, data));

    public static bool VerifyBasicAuthorization(string? header, string expectedKey)
    {
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            var bytes = Convert.FromBase64String(header[6..].Trim());
            if (bytes.Length > 4096) return false;
            var credential = Encoding.UTF8.GetString(bytes);
            var separator = credential.IndexOf(':');
            if (separator < 0 || !string.Equals(credential[..separator], "Paycom", StringComparison.Ordinal))
                return false;
            var actual = Encoding.UTF8.GetBytes(credential[(separator + 1)..]);
            var expected = Encoding.UTF8.GetBytes(expectedKey);
            return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }

    private sealed class PaymeProtocolException(int code, string message, string? data = null) : Exception(message)
    {
        public int Code { get; } = code;
        public string? Field { get; } = data;
    }
}
