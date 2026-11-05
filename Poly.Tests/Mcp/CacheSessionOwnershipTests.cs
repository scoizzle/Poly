using Poly.DomainModeling.Analysis;
using Poly.Mcp.Sessions;
using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

/// <summary>
/// #398: `RuntimeAnalysisCache` keeps one compilation per <see cref="Poly.DomainModeling.Ontology.Domain"/>
/// reference, keyed by the session that bound it. Two sessions over one domain therefore race, and
/// the loser's module is evicted silently.
/// <para>
/// A hard "refuse a second bind" guard was written and measured: it fails 29 tests that lower one
/// domain through two sessions on purpose, so it was rejected rather than weakening those tests.
/// What ships instead is the property that makes the hazard unreachable from the harness, asserted
/// here: a state owns exactly one session/analysis pair, and every state change produces a new
/// domain reference, so no two live states can bind the same domain.
/// </para>
/// </summary>
public class CacheSessionOwnershipTests {
    private const string Dsl = """
        domain Owned
        Agent: entity {
          Handle: Text unique
        }
        """;

    [Test]
    public async Task ExecutionPair_IsCreatedOnce_AndReused() {
        var (sessionId, _) = McpTestTools.Store.Create("Owned");
        await Assert.That(McpTestTools.Dsl.ApplyDsl(sessionId, Dsl).Success).IsTrue();
        await Assert.That(McpTestTools.Store.TryGet(sessionId, out var state)).IsTrue();

        var first = state.Execution;
        var second = state.Execution;
        await Assert.That(ReferenceEquals(first.Session, second.Session)).IsTrue()
            .Because("a second pair for one state would rebind the cache and evict the module");
        await Assert.That(ReferenceEquals(first.Analysis, second.Analysis)).IsTrue();
    }

    /// <summary>
    /// The executor and the export are the two consumers that used to disagree. Both must resolve
    /// the same session object, or one of them evicts the other.
    /// </summary>
    [Test]
    public async Task ExecutorAndExport_UseTheSameSession() {
        var (sessionId, _) = McpTestTools.Store.Create("Owned");
        await Assert.That(McpTestTools.Dsl.ApplyDsl(sessionId, Dsl).Success).IsTrue();
        await Assert.That(McpTestTools.Runtime.CreateInstance(sessionId, "Agent", """{"Handle":"a"}""").Success).IsTrue();
        await Assert.That(McpTestTools.Oracle.ExportDomainToCSharp(sessionId).Success).IsTrue();

        await Assert.That(McpTestTools.Store.TryGet(sessionId, out var state)).IsTrue();
        await Assert.That(ReferenceEquals(state.ExecutionSession, state.Execution.Session)).IsTrue();
        await Assert.That(state.ExecutorIfCreated).IsNotNull();
    }

    /// <summary>
    /// A successful change replaces the state with a new domain reference. That is what keeps two
    /// states from sharing one cache entry; if a future change reused the reference, the guard
    /// this issue declined to ship would become necessary.
    /// </summary>
    [Test]
    public async Task AcceptedChange_ProducesANewDomainReference() {
        var (sessionId, _) = McpTestTools.Store.Create("Owned");
        await Assert.That(McpTestTools.Dsl.ApplyDsl(sessionId, Dsl).Success).IsTrue();
        await Assert.That(McpTestTools.Store.TryGet(sessionId, out var before)).IsTrue();
        var oldDomain = before.Domain;

        var accepted = McpTestTools.Evolve.Add(
            sessionId, "property", """{"entityName":"Agent","name":"Memo","typeName":"Text"}""");
        await Assert.That(accepted.Success).IsTrue();

        await Assert.That(McpTestTools.Store.TryGet(sessionId, out var after)).IsTrue();
        await Assert.That(ReferenceEquals(oldDomain, after.Domain)).IsFalse()
            .Because("two states sharing one Domain would share one RuntimeAnalysisCache holder");
        await Assert.That(ReferenceEquals(before, after)).IsFalse();
    }

    /// <summary>A rejected change keeps the state, so it keeps its pair and its module.</summary>
    [Test]
    public async Task RejectedChange_KeepsTheStateAndItsPair() {
        var (sessionId, _) = McpTestTools.Store.Create("Owned");
        await Assert.That(McpTestTools.Dsl.ApplyDsl(sessionId, Dsl).Success).IsTrue();
        await Assert.That(McpTestTools.Store.TryGet(sessionId, out var before)).IsTrue();
        var pair = before.Execution;

        var rejected = McpTestTools.Evolve.Add(
            sessionId, "property", """{"entityName":"Agent","name":"Bad","typeName":"Ghost"}""");
        await Assert.That(rejected.Success).IsFalse();

        await Assert.That(McpTestTools.Store.TryGet(sessionId, out var after)).IsTrue();
        await Assert.That(ReferenceEquals(before, after)).IsTrue();
        await Assert.That(ReferenceEquals(pair.Session, after.Execution.Session)).IsTrue();
    }
}