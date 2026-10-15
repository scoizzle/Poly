using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Poly.Mcp;
using Poly.Mcp.Sessions;

namespace Poly.Tests.Mcp;

/// <summary>
/// MCP2 store behavior: idle eviction, per-session locking, and the tool
/// exception boundary that keeps the response envelope.
/// </summary>
public class McpSessionStoreTests {
    private static McpSessionStore NewStore(TimeProvider time, TimeSpan idleTimeout) =>
        new(time, Options.Create(new McpSessionOptions { IdleTimeout = idleTimeout, SweepInterval = TimeSpan.Zero }),
            NullLogger<McpSessionStore>.Instance);

    private sealed class FakeTime(DateTimeOffset start) : TimeProvider {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    [Test]
    public async Task EvictIdle_RemovesIdleSession_KeepsTouchedSession() {
        var time = new FakeTime(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
        var store = NewStore(time, TimeSpan.FromHours(1));

        var (idle, _) = store.Create("Idle");
        var (touched, _) = store.Create("Touched");

        time.Advance(TimeSpan.FromMinutes(30));
        await Assert.That(store.TryGet(touched, out _)).IsTrue();

        time.Advance(TimeSpan.FromMinutes(45));
        await Assert.That(store.EvictIdle()).IsEqualTo(1);

        var remaining = store.ListSessions();
        await Assert.That(remaining).Contains(touched);
        await Assert.That(remaining).DoesNotContain(idle);
    }

    [Test]
    public async Task EvictIdle_WhenTimeoutDisabled_DoesNotEvict() {
        var time = new FakeTime(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
        var store = NewStore(time, TimeSpan.Zero);
        var (sessionId, _) = store.Create("Kept");

        time.Advance(TimeSpan.FromDays(100));

        await Assert.That(store.EvictIdle()).IsEqualTo(0);
        await Assert.That(store.ListSessions()).Contains(sessionId);
    }

    [Test]
    public async Task TryModifyInstances_DifferentSessions_DoNotBlockEachOther() {
        var store = NewStore(TimeProvider.System, TimeSpan.FromHours(1));
        var (first, _) = store.Create("First");
        var (second, _) = store.Create("Second");

        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var holding = Task.Run(() => store.TryModifyInstances(first, _ => {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        }));

        await Assert.That(entered.Wait(TimeSpan.FromSeconds(5))).IsTrue();

        var other = Task.Run(() => store.TryModifyInstances(second, _ => { }));
        var winner = await Task.WhenAny(other, Task.Delay(TimeSpan.FromSeconds(5)));
        await Assert.That(winner).IsSameReferenceAs(other);

        release.Set();
        await holding;
    }

    [Test]
    public async Task ToolExceptionBoundary_Failure_CarriesEnvelope() {
        var result = ToolExceptionBoundary.Failure(new InvalidOperationException("boom"));

        await Assert.That(result.IsError).IsTrue();
        var envelope = result.StructuredContent!.Value;
        await Assert.That(envelope.GetProperty("success").GetBoolean()).IsFalse();
        await Assert.That(envelope.GetProperty("message").GetString()).Contains("boom");
        await Assert.That(result.Content[0]).IsTypeOf<ModelContextProtocol.Protocol.TextContentBlock>();
    }
}