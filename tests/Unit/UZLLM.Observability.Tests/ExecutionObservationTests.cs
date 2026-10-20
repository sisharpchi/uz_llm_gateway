using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using UZLLM.Observability;

namespace UZLLM.Observability.Tests;

public sealed class ExecutionObservationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
    private static IReadOnlyDictionary<string, ObservationProducerTrust> Trust =>
        new Dictionary<string, ObservationProducerTrust> { ["gateway-a"] = new(Key, ObservationSource.Gateway, "test") };

    [Theory]
    [InlineData("/v1/Chat/Completions/", ObservationOperation.Chat)]
    [InlineData("/V1/Responses", ObservationOperation.Responses)]
    [InlineData("/v1/EMBEDDINGS/", ObservationOperation.Embeddings)]
    public async Task Capture_matches_the_case_and_trailing_slash_semantics_of_inference_endpoints(string path, ObservationOperation operation)
    {
        var context = Context();
        context.Request.Path = path;
        var buffer = new ObservationBuffer(new FixedClock());
        await new ExecutionObservationMiddleware(_ => Task.CompletedTask, buffer, Options(), new FixedClock()).InvokeAsync(context);
        var rows = buffer.Drain("test", false).Observations;
        Assert.Equal(2, rows.Length);
        Assert.All(rows, row => Assert.Equal(operation, row.Operation));
    }

    [Fact]
    public async Task Independent_Node_edge_fixture_verifies_in_Dotnet_with_exact_known_digest()
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Sli", "edge-manifest.json"));
        var trusted = new Dictionary<string, ObservationProducerTrust> { ["edge-a"] = new(Key, ObservationSource.Edge, "test") };
        var result = ObservationProtocol.Verify(bytes, trusted);
        Assert.Equal("b742e0d44148e184cc63b0c17b0822f16efb4062ece43d1331bf5414cf045b7e", result.Digest);
        var row = Assert.Single(result.Manifest.Observations);
        Assert.Equal(Guid.Parse("12345678-1234-1234-1234-1234567890ab"), row.ExecutionId);
        Assert.Equal(TimeSpan.FromMilliseconds(1250), row.At - row.StartedAt);
        Assert.True(row.GatewayFault);
        Assert.False(row.TerminalSuccess);
    }

    [Fact]
    public void Exact_byte_and_record_bounds_are_accepted_but_adjacent_bounds_are_rejected()
    {
        var signed = ObservationProtocol.Sign("gateway-a", Batch(Item()), Key);
        var padded = Enumerable.Repeat((byte)' ', ObservationProtocol.MaximumBytes).ToArray();
        signed.CopyTo(padded, 0);
        Assert.Equal("gateway-a", ObservationProtocol.Verify(padded, Trust).Producer);
        Assert.Throws<ArgumentException>(() => ObservationProtocol.Verify(new byte[ObservationProtocol.MaximumBytes + 1], Trust));
        var rows = Enumerable.Range(1, 1025).Select(sequence => Item() with { Sequence = sequence }).ToArray();
        var valid = Batch(rows[0]) with { LastSequence = 1024, Observations = rows[..1024] };
        ObservationProtocol.Validate(valid);
        var rich = valid with { IntervalEnd = Now.AddTicks(1234567), Observations = rows[..1024].Select(item => item with
        {
            Phase = ObservationPhase.Attempt, Mode = ObservationMode.Sse, Credential = ObservationCredential.Hybrid,
            ProviderFault = ObservationProviderFault.ContextExceeded, Certainty = ObservationExecutionCertainty.RejectedBeforeExecution,
            At = Now.AddTicks(1234567), GatewayFailureAt = Now.AddTicks(1234567), ProviderFailureAt = Now.AddTicks(1234567),
            ClientDisconnectedAt = Now.AddTicks(1234567), ClientDisconnected = true, GatewayFault = true,
            RequestId = Guid.NewGuid(), AttemptId = Guid.NewGuid()
        }).ToArray() };
        Assert.Equal(1024, ObservationProtocol.Verify(ObservationProtocol.Sign("gateway-a", rich, Key), Trust).Manifest.Observations.Length);
        Assert.Throws<ArgumentException>(() => ObservationProtocol.Validate(valid with { LastSequence = 1025, Observations = rows }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ObservationBuffer(new FixedClock(), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ObservationBuffer(new FixedClock(), 1025));
        var smallest = new ObservationBuffer(new FixedClock(), 1);
        Assert.True(smallest.TryRecord(Item()));
        Assert.False(smallest.TryRecord(Item()));
        Assert.Equal(1, smallest.Drain("test", false).Dropped);
    }

    [Fact]
    public async Task Service_failure_and_client_disconnect_order_is_recorded_independently_of_finances()
    {
        var clock = new MutableClock();
        var buffer = new ObservationBuffer(clock);
        var context = Context();
        using var abort = new CancellationTokenSource();
        context.RequestAborted = abort.Token;
        await new ExecutionObservationMiddleware(http =>
        {
            var state = ExecutionObservationState.From(http)!;
            clock.Current = Now.AddSeconds(1);
            state.ProviderFault = ObservationProviderFault.Timeout;
            state.Certainty = ObservationExecutionCertainty.Unknown;
            clock.Current = Now.AddSeconds(2);
            abort.Cancel();
            clock.Current = Now.AddSeconds(3);
            state.GatewayFault = true;
            return Task.CompletedTask;
        }, buffer, Options(), clock).InvokeAsync(context);
        var terminal = buffer.Drain("test", false).Observations.Last();
        Assert.Equal(Now.AddSeconds(1), terminal.ProviderFailureAt);
        Assert.Equal(Now.AddSeconds(2), terminal.ClientDisconnectedAt);
        Assert.Equal(Now.AddSeconds(3), terminal.GatewayFailureAt);
        Assert.Equal(ObservationExecutionCertainty.Unknown, terminal.Certainty);
        Assert.False(terminal.TerminalSuccess);
        Assert.True(terminal.GatewayFault);
        Assert.True(terminal.ClientDisconnected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("12345678-1234-1234-1234-1234567890ab")]
    [InlineData("123456781234123412341234567890ab,123456781234123412341234567890ab")]
    public void Physical_peer_cannot_make_invalid_or_combined_edge_headers_trusted(string header)
    {
        var context = Context();
        context.Request.Scheme = "https";
        context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:10.1.1.1");
        context.Request.Headers[ObservationEdgeTrust.Header] = header;
        Assert.Null(Options().EdgeTrust.Resolve(context));
        context.Request.Headers[ObservationEdgeTrust.Header] = "123456781234123412341234567890ab";
        Assert.Equal(Guid.Parse("12345678-1234-1234-1234-1234567890ab"), Options().EdgeTrust.Resolve(context));
    }

    [Fact]
    public void Signed_manifest_authenticates_exact_bytes_and_preserves_safe_origin_dimensions()
    {
        var item = Item() with { ProviderFault = ObservationProviderFault.Authentication,
            Credential = ObservationCredential.Byok, GatewayFault = true, ClientDisconnected = true };
        var manifest = Batch(item);
        var verified = ObservationProtocol.Verify(ObservationProtocol.Sign("gateway-a", manifest, Key), Trust);
        Assert.Equal("gateway-a", verified.Producer);
        Assert.Equal(manifest.Epoch, verified.Manifest.Epoch);
        Assert.Equal(item, Assert.Single(verified.Manifest.Observations));
        Assert.Equal(64, verified.Digest.Length);
        Assert.Equal("Partial", verified.Manifest.TerminalCoverage);
    }

    [Theory]
    [InlineData("key")]
    [InlineData("producer")]
    [InlineData("scope")]
    [InlineData("digest")]
    [InlineData("signature")]
    [InlineData("payload")]
    [InlineData("unknown-field")]
    [InlineData("missing-field")]
    [InlineData("duplicate-field")]
    [InlineData("oversize")]
    public void Collector_rejects_forgery_wrong_scope_and_noncanonical_envelopes(string change)
    {
        var bytes = ObservationProtocol.Sign("gateway-a", Batch(Item()), Key);
        var envelope = JsonSerializer.Deserialize<SignedObservationManifest>(bytes, ObservationProtocol.Json)!;
        var trusted = Trust;
        switch (change)
        {
            case "key": trusted = new Dictionary<string, ObservationProducerTrust> { ["gateway-a"] = new(new byte[32], ObservationSource.Gateway, "test") }; break;
            case "scope": trusted = new Dictionary<string, ObservationProducerTrust> { ["gateway-a"] = new(Key, ObservationSource.Edge, "other") }; break;
            case "producer": envelope = envelope with { Producer = "customer" }; break;
            case "digest": envelope = envelope with { Sha256 = new string('0', 64) }; break;
            case "signature": envelope = envelope with { Signature = new string('0', 64) }; break;
            case "payload": envelope = envelope with { Payload = Convert.ToBase64String("{}"u8) }; break;
        }
        bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, ObservationProtocol.Json);
        if (change == "unknown-field") bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("{", "{\"prompt\":\"private\",", StringComparison.Ordinal));
        if (change == "missing-field") bytes = "{}"u8.ToArray();
        if (change == "duplicate-field") bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes)
            .Replace("{", "{\"producer\":\"gateway-a\",", StringComparison.Ordinal));
        if (change == "oversize") bytes = new byte[ObservationProtocol.MaximumBytes + 1];
        if (change is "unknown-field" or "missing-field" or "duplicate-field")
            Assert.Throws<JsonException>(() => ObservationProtocol.Verify(bytes, trusted));
        else Assert.Throws<ArgumentException>(() => ObservationProtocol.Verify(bytes, trusted));
    }

    [Theory]
    [InlineData("sequence")]
    [InlineData("count")]
    [InlineData("empty-id")]
    [InlineData("future-event")]
    [InlineData("interval")]
    [InlineData("clock-offset")]
    [InlineData("false-success")]
    [InlineData("duplicate")]
    [InlineData("enum")]
    public void Manifest_validation_rejects_impossible_counts_times_identities_and_success(string change)
    {
        var item = Item();
        var batch = Batch(item);
        batch = change switch
        {
            "sequence" => batch with { FirstSequence = 0 },
            "count" => batch with { Dropped = 1 },
            "empty-id" => batch with { Observations = [item with { ExecutionId = Guid.Empty }] },
            "future-event" => batch with { Observations = [item with { At = Now.AddSeconds(1) }] },
            "interval" => batch with { IntervalEnd = Now.AddHours(2) },
            "clock-offset" => batch with { IntervalStart = Now.ToOffset(TimeSpan.FromHours(1)) },
            "false-success" => batch with { Observations = [item with { TerminalSuccess = true }] },
            "duplicate" => batch with { LastSequence = 2, Observations = [item, item with { Sequence = 2 }] },
            "enum" => batch with { Observations = [item with { Mode = (ObservationMode)999 }] },
            _ => batch
        };
        Assert.Throws<ArgumentException>(() => ObservationProtocol.Validate(batch));
    }

    [Fact]
    public void Concurrent_overflow_and_heartbeats_disclose_every_lost_sequence_without_sink_waits()
    {
        var buffer = new ObservationBuffer(new FixedClock());
        Parallel.For(0, 5120, _ => buffer.TryRecord(Item() with { ExecutionId = Guid.NewGuid() }));
        var batch = buffer.Drain("test", false);
        Assert.Equal(1, batch.FirstSequence);
        Assert.Equal(5120, batch.LastSequence);
        Assert.Equal(1024, batch.Observations.Length);
        Assert.Equal(4096, batch.Dropped);
        ObservationProtocol.Validate(batch);
        var empty = buffer.Drain("test", false);
        Assert.Equal(batch.Epoch, empty.Epoch);
        Assert.Equal(2, empty.BatchNumber);
        Assert.Equal(5121, empty.FirstSequence);
        Assert.Equal(5120, empty.LastSequence);
        Assert.Empty(empty.Observations);
        Assert.Equal(0, empty.Dropped);
        Assert.False(empty.CollectorContinuous);
        Assert.False(empty.ClockSynchronized);
        Assert.NotEqual(batch.Epoch, new ObservationBuffer(new FixedClock()).Drain("test", false).Epoch);
    }

    [Theory]
    [InlineData(true, "10.1.1.1", true)]
    [InlineData(false, "10.1.1.1", false)]
    [InlineData(true, "10.1.1.2", false)]
    public async Task Only_TLS_and_physical_trusted_peer_can_supply_execution_identity(bool tls, string peer, bool accepted)
    {
        var context = Context();
        context.Request.Scheme = tls ? "https" : "http";
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        var edgeId = Guid.NewGuid();
        context.Request.Headers[ObservationEdgeTrust.Header] = edgeId.ToString("N");
        context.Request.Headers["X-Forwarded-For"] = "10.1.1.1";
        var buffer = new ObservationBuffer(new FixedClock());
        await new ExecutionObservationMiddleware(_ => Task.CompletedTask, buffer, Options(), new FixedClock()).InvokeAsync(context);
        var items = buffer.Drain("test", false).Observations;
        Assert.Equal(2, items.Length);
        Assert.Equal(accepted, items[0].TrustedEdge);
        Assert.Equal(accepted, items[0].ExecutionId == edgeId);
        Assert.Equal(items[0].ExecutionId, items[1].ExecutionId);
        Assert.False(items[1].TerminalSuccess); // HTTP200 alone supplies no protocol evidence.
        Assert.False(context.Request.Headers.ContainsKey(ObservationEdgeTrust.Header));
        Assert.Equal("existing-correlation", context.Response.Headers["X-Request-Id"]);
    }

    [Theory]
    [InlineData("success", true, false, false)]
    [InlineData("partial", false, false, false)]
    [InlineData("cancel", false, true, false)]
    [InlineData("fault", false, false, true)]
    public async Task Terminal_evidence_requires_explicit_protocol_completion_not_HTTP200(string outcome,
        bool success, bool disconnected, bool gatewayFault)
    {
        var context = Context();
        var buffer = new ObservationBuffer(new FixedClock());
        Task Next(HttpContext http)
        {
            var state = ExecutionObservationState.From(http)!;
            state.Mode = ObservationMode.Sse;
            state.TerminalSuccess = outcome is "success" or "cancel";
            if (outcome == "cancel") http.RequestAborted = new CancellationToken(true);
            if (outcome == "fault") throw new IOException("must never be retained");
            return Task.CompletedTask;
        }
        var operation = new ExecutionObservationMiddleware(Next, buffer, Options(), new FixedClock()).InvokeAsync(context);
        if (outcome == "fault") await Assert.ThrowsAsync<IOException>(() => operation); else await operation;
        var terminal = buffer.Drain("test", false).Observations.Last();
        Assert.Equal(success, terminal.TerminalSuccess);
        Assert.Equal(disconnected, terminal.ClientDisconnected);
        Assert.Equal(gatewayFault, terminal.GatewayFault);
        Assert.Equal(200, terminal.HttpStatus);
        Assert.DoesNotContain("must never", JsonSerializer.Serialize(terminal, ObservationProtocol.Json));
    }

    private static DefaultHttpContext Context()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/v1/chat/completions";
        context.Response.Headers["X-Request-Id"] = "existing-correlation";
        return context;
    }
    private static ObservationCaptureOptions Options() => new(true, "gateway-a", "test", "unused", Key, false,
        new([IPAddress.Parse("10.1.1.1")]));
    private static ExecutionObservation Item() => new(1, Guid.NewGuid(), ObservationPhase.Started,
        ObservationOperation.Chat, ObservationMode.Unknown, Now, Now, true, 200, false, false, false, false,
        ObservationProviderFault.None, ObservationCredential.Unknown);
    private static ObservationManifest Batch(ExecutionObservation item) => new(1, Guid.NewGuid(), 1,
        ObservationSource.Gateway, "test", 1, 1, Now, Now, 0, false, false, "Partial", [item]);
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Current = Now;
        public override DateTimeOffset GetUtcNow() => Current;
    }
}
