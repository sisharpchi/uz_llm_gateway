using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Payments.Contracts;

namespace UZLLM.Modules.Payments.Infrastructure;

/// <summary>CLICK Shop API Prepare/Complete response.</summary>
public sealed record ClickShopResponse(
    [property: JsonPropertyName("click_trans_id")] string? ClickTransactionId,
    [property: JsonPropertyName("merchant_trans_id")] string? MerchantTransactionId,
    [property: JsonPropertyName("merchant_prepare_id")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? MerchantPrepareId,
    [property: JsonPropertyName("merchant_confirm_id")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? MerchantConfirmId,
    [property: JsonPropertyName("error")] int Error,
    [property: JsonPropertyName("error_note")] string ErrorNote);

public static class ClickSignature
{
    public static bool Verify(IReadOnlyDictionary<string, string> fields, string secretKey)
    {
        if (string.IsNullOrEmpty(secretKey)
            || !Get(fields, "action", out var action)
            || !Get(fields, "click_trans_id", out var transactionId)
            || !Get(fields, "service_id", out var serviceId)
            || !Get(fields, "merchant_trans_id", out var merchantId)
            || !Get(fields, "amount", out var amount)
            || !Get(fields, "sign_time", out var signTime)
            || !Get(fields, "sign_string", out var signedHex)
            || signedHex.Length != 32)
            return false;
        var prepareId = string.Empty;
        if (action == "1" && !Get(fields, "merchant_prepare_id", out prepareId))
            return false;
        if (action is not ("0" or "1")) return false;
        byte[] provided;
        try { provided = Convert.FromHexString(signedHex); }
        catch (FormatException) { return false; }
        // The CLICK protocol signs the exact textual amount and timestamp fields.
        // Parsing/normalizing before hashing would reject valid signatures.
        var material = transactionId + serviceId + secretKey + merchantId
            + prepareId + amount + action + signTime;
        var expected = MD5.HashData(Encoding.UTF8.GetBytes(material));
        return CryptographicOperations.FixedTimeEquals(provided, expected);
    }

    private static bool Get(IReadOnlyDictionary<string, string> fields, string key, out string value) =>
        fields.TryGetValue(key, out value!) && !string.IsNullOrEmpty(value) && value.Length <= 256;
}

public sealed class ClickShopApi(IPaymentService payments, PaymentConfiguration configuration)
{
    public async Task<ClickShopResponse> HandleAsync(IReadOnlyDictionary<string, string> fields,
        string rawBody, int expectedAction, CancellationToken cancellationToken = default)
    {
        fields.TryGetValue("click_trans_id", out var clickTransactionId);
        fields.TryGetValue("merchant_trans_id", out var merchantTransactionId);
        if (rawBody.Length > 32_768 || expectedAction is not (0 or 1)
            || !Required(fields, "service_id", "amount", "action", "error",
                "error_note", "sign_time", "sign_string", "click_paydoc_id")
            || string.IsNullOrWhiteSpace(clickTransactionId)
            || string.IsNullOrWhiteSpace(merchantTransactionId)
            || clickTransactionId.Length > 120
            || merchantTransactionId.Length > 120
            || expectedAction == 1 && !fields.ContainsKey("merchant_prepare_id"))
            return Error(-8, "Error in request from click", clickTransactionId, merchantTransactionId);
        if (string.IsNullOrWhiteSpace(configuration.ClickSecretKey)
            || string.IsNullOrWhiteSpace(configuration.ClickServiceId))
            return Error(-7, "Merchant configuration unavailable", clickTransactionId, merchantTransactionId);
        if (!ClickSignature.Verify(fields, configuration.ClickSecretKey))
            return Error(-1, "SIGN CHECK FAILED!", clickTransactionId, merchantTransactionId);
        if (!string.Equals(fields["service_id"], configuration.ClickServiceId, StringComparison.Ordinal))
            return Error(-8, "Error in request from click", clickTransactionId, merchantTransactionId);
        if (!int.TryParse(fields["action"], NumberStyles.None, CultureInfo.InvariantCulture, out var action)
            || action != expectedAction)
            return Error(-3, "Action not found", clickTransactionId, merchantTransactionId);
        if (!Guid.TryParse(merchantTransactionId, out var intentId) || intentId == Guid.Empty)
            return Error(-5, "User does not exist", clickTransactionId, merchantTransactionId);
        if (!TryParseTiyin(fields["amount"], out var amount))
            return Error(-2, "Incorrect parameter amount", clickTransactionId, merchantTransactionId);
        if (!int.TryParse(fields["error"], NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var providerError))
            return Error(-8, "Error in request from click", clickTransactionId, merchantTransactionId);
        var requestHash = SHA256.HashData(Encoding.UTF8.GetBytes(rawBody));
        var receiptId = $"{clickTransactionId}:{action}";
        try
        {
            PaymentCommandResult result;
            if (action == 0)
            {
                if (providerError != 0)
                    return Error(-9, "Transaction cancelled", clickTransactionId, merchantTransactionId);
                result = await payments.PrepareAsync(PaymentProvider.Click, intentId,
                    clickTransactionId!, amount, null, receiptId, requestHash, cancellationToken);
            }
            else
            {
                if (!int.TryParse(fields["merchant_prepare_id"], NumberStyles.None,
                        CultureInfo.InvariantCulture, out var prepareId) || prepareId <= 0)
                    return Error(-6, "Transaction does not exist", clickTransactionId, merchantTransactionId);
                result = providerError < 0
                    ? await payments.CancelAsync(PaymentProvider.Click, clickTransactionId!,
                        prepareId, amount, providerError, receiptId, requestHash, cancellationToken)
                    : providerError == 0
                        ? await payments.CompleteAsync(PaymentProvider.Click, clickTransactionId!,
                            prepareId, amount, receiptId, requestHash, cancellationToken)
                        : new PaymentCommandResult(PaymentCommandStatus.InvalidState, null);
            }
            var mapped = Map(result, action, providerError);
            return new ClickShopResponse(clickTransactionId, merchantTransactionId,
                result.Intent?.ClickPrepareId, action == 1 ? result.Intent?.ClickPrepareId : null,
                mapped.Error, mapped.Note);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Error(-7, "Failed to update user", clickTransactionId, merchantTransactionId);
        }
    }

    private static (int Error, string Note) Map(PaymentCommandResult result, int action, int providerError) =>
        result.Status switch
        {
            PaymentCommandStatus.Accepted when action == 1 && providerError < 0 => (-9, "Transaction cancelled"),
            PaymentCommandStatus.Accepted => (0, "Success"),
            PaymentCommandStatus.Duplicate when result.Intent?.Status == PaymentStatus.Paid => (-4, "Already paid"),
            PaymentCommandStatus.Duplicate when result.Intent?.Status == PaymentStatus.Canceled => (-9, "Transaction cancelled"),
            PaymentCommandStatus.Duplicate => (0, "Success"),
            PaymentCommandStatus.NotFound or PaymentCommandStatus.WrongProvider => (-5, "User does not exist"),
            PaymentCommandStatus.WrongAmount => (-2, "Incorrect parameter amount"),
            PaymentCommandStatus.WrongPrepareId => (-6, "Transaction does not exist"),
            PaymentCommandStatus.InvalidState when action == 1 => (-6, "Transaction does not exist"),
            PaymentCommandStatus.Expired => (-9, "Transaction cancelled"),
            _ => (-4, "Already paid")
        };

    private static bool TryParseTiyin(string raw, out UzsTiyinAmount value)
    {
        value = default;
        if (!decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                out var sum) || sum <= 0)
            return false;
        var tiyin = sum * 100m;
        if (tiyin != decimal.Truncate(tiyin) || tiyin > long.MaxValue)
            return false;
        value = new UzsTiyinAmount((long)tiyin);
        return true;
    }

    private static bool Required(IReadOnlyDictionary<string, string> fields, params string[] names) =>
        names.All(name => fields.TryGetValue(name, out var value)
            && !string.IsNullOrEmpty(value) && value.Length <= 256);

    private static ClickShopResponse Error(int code, string note, string? transactionId, string? merchantId) =>
        new(transactionId, merchantId, null, null, code, note);
}
