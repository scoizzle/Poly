using Poly.Ast.Nodes;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// A lowering pass refuses a context with no analysis or no domain at construction, so the
/// refusal is positioned at the boundary and not a later null dereference.
/// </summary>
public class LoweringContextBoundaryTests {
    static Entity Widget() => new("Widget", Properties: [], Actions: [], Policies: [], Stages: []);

    [Test]
    public async Task EffectLoweringPass_NullAnalysis_ThrowsArgumentNull() {
        var entity = Widget();
        var (domain, _) = LoweringFixtures.Analyze(entity);
        await Assert.That(() => new EffectLoweringPass(entity, new LoweringContext(new ThisReference(), null!, domain)))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task EffectLoweringPass_NullDomain_ThrowsArgumentNull() {
        var entity = Widget();
        var (_, analysis) = LoweringFixtures.Analyze(entity);
        await Assert.That(() => new EffectLoweringPass(entity, new LoweringContext(new ThisReference(), analysis, null!)))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task DomainExpressionLoweringPass_NullAnalysis_ThrowsArgumentNull() {
        var (domain, _) = LoweringFixtures.Analyze(Widget());
        await Assert.That(() => new DomainExpressionLoweringPass(new LoweringContext(new ThisReference(), null!, domain)))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task DomainExpressionLoweringPass_NullDomain_ThrowsArgumentNull() {
        var (_, analysis) = LoweringFixtures.Analyze(Widget());
        await Assert.That(() => new DomainExpressionLoweringPass(new LoweringContext(new ThisReference(), analysis, null!)))
            .Throws<ArgumentNullException>();
    }
}