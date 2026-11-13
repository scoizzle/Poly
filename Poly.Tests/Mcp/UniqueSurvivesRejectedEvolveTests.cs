namespace Poly.Tests.Mcp;

/// <summary>
/// #387, second path: a <b>rejected</b> evolution is still an analysis of the live domain.
/// <para>
/// <c>DomainEvolution.Apply</c> resolves its mutation index by analyzing <c>_current</c> when no
/// prior analysis is supplied, and <c>DomainSession.Analyze</c> binds that session into
/// <see cref="Poly.DomainModeling.Analysis.RuntimeAnalysisCache"/>. Binding a different session
/// evicts the module the executor is running, so a rejected <c>add</c> left the next create
/// lowering without <c>UniqueConstraintCompiler</c> — the same failure as
/// <see cref="UniqueSurvivesExportTests"/>, reached through the write path instead of the read one.
/// </para>
/// </summary>
public class UniqueSurvivesRejectedEvolveTests {
    private const string Dsl = """
        domain EvolveUnique
        Agent: entity {
          Handle: Text unique
        }
        """;

    [Test]
    public async Task Create_DuplicateUnique_AfterRejectedAdd_StillFails() {
        var (sessionId, _) = McpTestTools.Store.Create("EvolveUnique");
        await Assert.That(McpTestTools.Surface.Define(sessionId, Dsl).Success).IsTrue();

        await Assert.That(McpTestTools.Surface.Create(sessionId, "Agent", """{"Handle":"sam"}""").Ok).IsTrue();

        var rejected = McpTestTools.Evolve.Add(
            sessionId, "property", """{"entityName":"Agent","name":"Bad","typeName":"Ghost"}""");
        await Assert.That(rejected.Success).IsFalse().Because("the property references an unknown type");

        var duplicate = McpTestTools.Surface.Create(sessionId, "Agent", """{"Handle":"sam"}""");
        await Assert.That(duplicate.Ok).IsFalse()
            .Because("a rejected evolve must leave the running module exactly as it was");
        await Assert.That(duplicate.Summary).Contains("Unique constraint");
    }

    /// <summary>A rejected mutation against a missing entity is the second shape of the same path.</summary>
    [Test]
    public async Task Create_DuplicateUnique_AfterRejectedAddToMissingEntity_StillFails() {
        var (sessionId, _) = McpTestTools.Store.Create("EvolveUnique");
        await Assert.That(McpTestTools.Surface.Define(sessionId, Dsl).Success).IsTrue();

        await Assert.That(McpTestTools.Surface.Create(sessionId, "Agent", """{"Handle":"sam"}""").Ok).IsTrue();

        var rejected = McpTestTools.Evolve.Add(
            sessionId, "property", """{"entityName":"Nope","name":"Bad","typeName":"Text"}""");
        await Assert.That(rejected.Success).IsFalse();

        var duplicate = McpTestTools.Surface.Create(sessionId, "Agent", """{"Handle":"sam"}""");
        await Assert.That(duplicate.Ok).IsFalse()
            .Because("a rejected evolve must leave the running module exactly as it was");
        await Assert.That(duplicate.Summary).Contains("Unique constraint");
    }

    /// <summary>A rejected removal is the same gate by the other half of the same tool family.</summary>
    [Test]
    public async Task Create_DuplicateUnique_AfterRejectedRemove_StillFails() {
        var (sessionId, _) = McpTestTools.Store.Create("EvolveUnique");
        await Assert.That(McpTestTools.Surface.Define(sessionId, Dsl).Success).IsTrue();

        await Assert.That(McpTestTools.Surface.Create(sessionId, "Agent", """{"Handle":"sam"}""").Ok).IsTrue();

        var rejected = McpTestTools.Evolve.Remove(sessionId, "entity", """{"name":"Nope"}""");
        await Assert.That(rejected.Success).IsFalse().Because("the entity does not exist");

        var duplicate = McpTestTools.Surface.Create(sessionId, "Agent", """{"Handle":"sam"}""");
        await Assert.That(duplicate.Ok).IsFalse()
            .Because("a rejected removal must leave the running module exactly as it was");
        await Assert.That(duplicate.Summary).Contains("Unique constraint");
    }

    /// <summary>
    /// An accepted evolve replaces the state and its executor, and a replacement executor starts
    /// with no instances — so the duplicate is made twice after the change. This is the control
    /// that tells "stop analyzing the live domain" apart from "stop evolving".
    /// </summary>
    [Test]
    public async Task Create_DuplicateUnique_AfterAcceptedAdd_StillFails() {
        var (sessionId, _) = McpTestTools.Store.Create("EvolveUnique");
        await Assert.That(McpTestTools.Surface.Define(sessionId, Dsl).Success).IsTrue();

        var accepted = McpTestTools.Evolve.Add(
            sessionId, "property", """{"entityName":"Agent","name":"Memo","typeName":"Text"}""");
        await Assert.That(accepted.Success).IsTrue();

        var first = McpTestTools.Surface.Create(sessionId, "Agent", """{"Handle":"sam","Memo":"x"}""");
        await Assert.That(first.Ok).IsTrue();

        var duplicate = McpTestTools.Surface.Create(sessionId, "Agent", """{"Handle":"sam","Memo":"y"}""");
        await Assert.That(duplicate.Ok).IsFalse()
            .Because("the executor an accepted evolve installs still enforces unique");
        await Assert.That(duplicate.Summary).Contains("Unique constraint");
    }
}