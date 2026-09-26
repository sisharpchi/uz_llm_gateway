using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using UZLLM.Modules.Usage.Infrastructure;
using UZLLM.Gateway.Api.Inference;

namespace UZLLM.Gateway.ContractTests;

public sealed class PayloadProtectionTests
{
    [Fact]
    public void Envelope_binds_ciphertext_to_tenant_request_purpose_and_version()
    {
        var protector = CreateProtector("v1", includeOldKey: true);
        var org = Guid.NewGuid();
        var project = Guid.NewGuid();
        var request = Guid.NewGuid();
        var plaintext = Encoding.UTF8.GetBytes("private payload");
        var encrypted = protector.Protect(org, project, request, "request", plaintext);

        Assert.Equal("v1", encrypted.KeyVersion);
        Assert.DoesNotContain("private payload", Encoding.UTF8.GetString(encrypted.Ciphertext));
        Assert.Equal(plaintext, protector.Unprotect(org, project, request, "request", encrypted));
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(Guid.NewGuid(), project,
            request, "request", encrypted));
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(org, Guid.NewGuid(),
            request, "request", encrypted));
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(org, project,
            Guid.NewGuid(), "request", encrypted));
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(org, project,
            request, "response", encrypted));
        var tampered = encrypted with { Ciphertext = (byte[])encrypted.Ciphertext.Clone() };
        tampered.Ciphertext[12] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(org, project,
            request, "request", tampered));
    }

    [Fact]
    public void Key_rotation_reads_old_ciphertext_while_old_key_is_retained()
    {
        var org = Guid.NewGuid();
        var project = Guid.NewGuid();
        var request = Guid.NewGuid();
        var old = CreateProtector("v1", includeOldKey: true).Protect(org, project,
            request, "request", Encoding.UTF8.GetBytes("before rotation"));
        var rotated = CreateProtector("v2", includeOldKey: true);
        Assert.Equal("before rotation", Encoding.UTF8.GetString(rotated.Unprotect(org, project,
            request, "request", old)));
        Assert.Equal("v2", rotated.Protect(org, project, request, "response",
            Encoding.UTF8.GetBytes("after rotation")).KeyVersion);
        Assert.Throws<InvalidOperationException>(() => CreateProtector("v2", includeOldKey: false)
            .Unprotect(org, project, request, "request", old));
    }

    [Fact]
    public async Task Response_capture_keeps_exact_sent_bytes_and_discards_oversized_payload()
    {
        await using var destination = new MemoryStream();
        await using var capture = new ResponseCaptureStream(destination, 8);
        await capture.WriteAsync(Encoding.UTF8.GetBytes("hello"));
        await capture.WriteAsync(Encoding.UTF8.GetBytes("!"));
        Assert.Equal("hello!", Encoding.UTF8.GetString(capture.CompletePayload!));
        await capture.WriteAsync(Encoding.UTF8.GetBytes(" longer"));
        Assert.Null(capture.CompletePayload);
        Assert.Equal("hello! longer", Encoding.UTF8.GetString(destination.ToArray()));
    }

    [Fact]
    public async Task Response_capture_sees_json_written_via_aspnet_body_writer()
    {
        var context = new DefaultHttpContext();
        var destination = new MemoryStream();
        context.Response.Body = destination;
        var capture = new ResponseCaptureStream(destination, 1024);
        context.Response.Body = capture;

        await context.Response.WriteAsJsonAsync(new { message = "private reply" });

        Assert.Contains("private reply", Encoding.UTF8.GetString(capture.CompletePayload!));
        Assert.Equal(destination.ToArray(), capture.CompletePayload);
    }

    private static PayloadEnvelopeProtector CreateProtector(string active, bool includeOldKey)
    {
        var values = new Dictionary<string, string?>
        {
            ["PayloadSecrets:ActiveKeyVersion"] = active,
            ["PayloadSecrets:Keys:v2"] = Convert.ToBase64String(Enumerable.Repeat((byte)2, 32).ToArray())
        };
        if (includeOldKey)
            values["PayloadSecrets:Keys:v1"] = Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray());
        return new(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }
}
