using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Compile;

public sealed class CoreLibraryTests {
    [Test]
    public async Task Core_ContributesItsPassesAtLoad() {
        var session = DomainSession.ForExtensions([]);

        await Assert.That(session.Contributions.First())
            .IsEqualTo(new StageContribution(PipelineStage.Load, CoreLibrary.CoreId));
        await Assert.That(session.Analyzers[0].PassName).IsEqualTo(StructuralDomainAnalyzer.Id);
        await Assert.That(session.Extensions).IsEmpty();
    }

    [Test]
    public async Task Core_RegistrationEntersThroughLoad() {
        var core = SessionBuilder.CreateEmpty().LoadCore().Build();
        await Assert.That(core.Contributions
                .Count(c => c == new StageContribution(PipelineStage.Analyze, CoreLibrary.CoreId))).IsEqualTo(1);
        await Assert.That(core.Contributions
                .Count(c => c == new StageContribution(PipelineStage.Lower, CoreLibrary.CoreId))).IsEqualTo(1);

        var bare = SessionBuilder.CreateEmpty().Load(new MarkerLibrary()).Build();
        var domain = DomainFactory.Create("D");
        var analysis = DomainSession.ForExtensions([]).Analyze(domain);

        await Assert.That(() => bare.Compile(domain, analysis))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("core");
        await Assert.That(() => bare.Analyze(domain))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("core");
        await Assert.That(() => bare.Lower(domain, analysis))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("core");
        var untouchedCatalog = bare.ArtifactCatalog;
        await Assert.That(() => bare.Emit(domain, analysis))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("core");
        // Emit refuses before replacing the catalog; pin the guard's placement, not only its message.
        await Assert.That(ReferenceEquals(bare.ArtifactCatalog, untouchedCatalog)).IsTrue();
    }

    [Test]
    public async Task Core_RegisteredOutsideLoad_AnalyzeStillRefuses() {
        var builder = SessionBuilder.CreateEmpty().Load(new MarkerLibrary());
        builder.AddContribution(PipelineStage.Analyze, CoreLibrary.CoreId);

        var session = builder.Build();

        await Assert.That(() => session.Analyze(DomainFactory.Create("D")))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("core");
    }

    [Test]
    public async Task Core_AnalyzerPipelineRequiresCoreLibrary() {
        var session = SessionBuilder.CreateEmpty().Load(new MarkerLibrary()).Build();

        await Assert.That(() => session.Analyze(DomainFactory.Create("D")))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("core");
    }

    [Test]
    public async Task Core_LoadingCoreTwice_Throws() {
        await Assert.That(() => SessionBuilder.CreateEmpty().LoadCore().LoadCore())
            .Throws<InvalidOperationException>()
            .WithMessageContaining(CoreLibrary.CoreId);
    }

    [Test]
    public async Task Core_IsNotAUsesId() {
        await Assert.That(() => DomainSession.ForExtensions([CoreLibrary.CoreId]))
            .Throws<InvalidOperationException>()
            .WithMessageContaining(CoreLibrary.CoreId);
    }

    private sealed class MarkerPass : INodeAnalyzer {
        public string PassName => "marker-pass";

        public void Analyze(AnalysisContext context, Node node) {
        }
    }

    private sealed class MarkerLibrary : IDomainLibrary {
        public string Id => "marker";

        public void Register(SessionBuilder builder) =>
            builder.AddAnalyzer(new MarkerPass());
    }
}