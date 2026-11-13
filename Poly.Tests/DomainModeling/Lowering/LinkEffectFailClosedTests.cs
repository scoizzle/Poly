using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// F-16: nothing lowers <c>link</c> / <c>unlink</c> until L-13. A caller that bypasses analysis and
/// lowers directly must get a thrown <see cref="InvalidOperationException"/>, never a null node or a
/// silently dropped effect.
/// </summary>
public sealed class LinkEffectFailClosedTests {
    private const string Dsl = """
        domain Board
        Task: entity { Name: Text }
        Board: entity {
          Note: Text optional
          Backlog: many Task
          Active: many Task
          Move: action { for t in Backlog { assign Note to "x" } }
        }
        """;

    private static (EffectLoweringPass Pass, Entity Board) Pass() {
        var (domain, analysis) = EvolvedDomain.FromDsl(Dsl);
        var board = domain.Types.OfType<Entity>().Single(e => e.Name == "Board");
        return (new EffectLoweringPass(board, new LoweringContext(
            new Parameter("entity", new TypeReference(board.Name)), analysis, domain, ActionResultType: new NamedTypeReference("DomainResult")) { ActionName = "Move" }), board);
    }

    private static readonly LinkEffect LinkActive = new("Active", DomainExpression.Property("t"));
    private static readonly UnlinkEffect UnlinkActive = new("Active", DomainExpression.Property("t"));

    [Test]
    public async Task LinkEffect_Lowering_Throws_NotNull() {
        var (pass, _) = Pass();

        var ex = Assert.Throws<InvalidOperationException>(() => pass.TryLowerVmNode(LinkActive))!;

        await Assert.That(ex.Message).Contains("Board.Move: link Active to t");
    }

    [Test]
    public async Task UnlinkEffect_Lowering_Throws_NotNull() {
        var (pass, _) = Pass();

        var ex = Assert.Throws<InvalidOperationException>(() => pass.TryLowerVmNode(UnlinkActive))!;

        await Assert.That(ex.Message).Contains("Board.Move: unlink Active from t");
    }

    [Test]
    public async Task LinkEffect_InActionForBody_ThrowsThroughLowerActionBody() {
        var (pass, _) = Pass();
        var body = new ForEachEffect("t", "Backlog", null, [LinkActive]);

        var ex = Assert.Throws<InvalidOperationException>(() => pass.LowerActionBody([body]))!;

        await Assert.That(ex.Message).Contains("Board.Move: link Active to t");
    }

    [Test]
    public async Task UnlinkEffect_InConditionalBody_ThrowsThroughLowerActionBody() {
        var (pass, _) = Pass();
        var body = new ConditionalEffect(DomainExpression.Literal(true), [UnlinkActive], null);

        var ex = Assert.Throws<InvalidOperationException>(() => pass.LowerActionBody([body]))!;

        await Assert.That(ex.Message).Contains("Board.Move: unlink Active from t");
    }
}