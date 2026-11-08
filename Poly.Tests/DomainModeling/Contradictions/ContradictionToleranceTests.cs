using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Contradictions;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Libraries.Contradictions;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Packs.Product;

namespace Poly.Tests.DomainModeling.Contradictions;

public class ContradictionToleranceTests {
    private static ContradictionClaim Claim(
        string name = "notes-width",
        string source = "Customer.Notes",
        string target = "Order.Notes",
        ContradictionStatus status = ContradictionStatus.Open,
        ContradictionShape? sourceShape = null,
        ContradictionShape? targetShape = null,
        IReadOnlyList<string>? ids = null) =>
        new(name, source, target, ids ?? ["claim-1"], status, sourceShape, targetShape);

    [Test]
    public async Task Strict_ClaimsWithoutUsesContradictions_BuildRefuses() {
        var builder = SessionBuilder.CreateEmpty().LoadCore().WithContradictionClaims([Claim()]);
        await Assert.That(() => builder.Build()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ClaimsBoundAfterLibraryLoad_Throws() {
        var builder = SessionBuilder.CreateEmpty().LoadCore().Load(new ContradictionsLibrary());
        await Assert.That(() => builder.WithContradictionClaims([Claim()])).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Strict_NoClaims_StillBuilds() {
        var session = SessionBuilder.CreateEmpty().LoadCore().WithContradictionClaims([]).Build();
        await Assert.That(session.ContradictionClaims.Count).IsEqualTo(0);
        var loaded = SessionBuilder.CreateEmpty().LoadCore().WithContradictionClaims([Claim()])
            .Load(new ContradictionsLibrary()).Build();
        await Assert.That(loaded.ContradictionClaims.Count).IsEqualTo(1);
    }

    private const string NotesDomain = """
        domain D
        uses contradictions
        Customer: entity {
          Notes: Text length(0, 200)
        }
        Order: entity {
          Notes: Text length(0, 50)
          Notes2: Text length(0, 50)
        }
        """;

    private static (DomainSession Session, Domain Domain, AnalysisResult Analysis, bool Succeeded) Analyze(
        string poly, IReadOnlyList<ContradictionClaim>? claims = null) {
        var session = DomainSession.ForAuthoringSource(
            poly, ExtensionCatalog.ProductAuthoring, ProductCatalog.Catalog, claims);
        var changes = new PolyDslParser(poly, session).Parse();
        var outcome = new DomainEvolution(new Domain("_", [])).ApplyAndAnalyze(changes, session: session);
        var domain = outcome.Succeeded ? outcome.Root! : null!;
        return (session, domain, outcome.Analysis, outcome.Succeeded);
    }

    private static ContradictionBag BagOf(Domain domain, AnalysisResult analysis) =>
        analysis.GetMetadata<ContradictionMetadata>(domain)!.Bag;

    [Test]
    public async Task ProductCatalog_BothCatalogs_ResolveContradictions() {
        await Assert.That(ProductCatalog.Catalog.Contains(ContradictionsLibrary.ExtensionId)).IsTrue();
        await Assert.That(ProductCatalog.Model.Contains(ContradictionsLibrary.ExtensionId)).IsTrue();
    }

    [Test]
    public async Task UsesContradictions_NoClaims_EmptyBag_And_Builds() {
        var (session, domain, analysis, ok) = Analyze(NotesDomain);
        await Assert.That(ok).IsTrue();
        await Assert.That(analysis.HasErrors).IsFalse();
        await Assert.That(BagOf(domain, analysis).Entries.Count).IsEqualTo(0);
        await Assert.That(session.Lower(domain, analysis).Count).IsGreaterThan(0);
    }

    [Test]
    public async Task Tolerant_WidthNarrowing_PublishesOpenEntry_AnalyzeClean() {
        var (_, domain, analysis, ok) = Analyze(NotesDomain, [Claim()]);
        await Assert.That(ok).IsTrue();
        await Assert.That(analysis.HasErrors).IsFalse();
        var entry = BagOf(domain, analysis).Entries.Single();
        await Assert.That(entry.Name).IsEqualTo("notes-width");
        await Assert.That(entry.Kinds.Single()).IsEqualTo(ContradictionKind.WidthNarrowing);
        await Assert.That(entry.Status).IsEqualTo(ContradictionStatus.Open);
        await Assert.That(entry.SourceShape.MaxLength).IsEqualTo(200);
        await Assert.That(entry.TargetShape.MaxLength).IsEqualTo(50);
        await Assert.That(entry.ClaimIds.Single()).IsEqualTo("claim-1");
    }

    [Test]
    public async Task Tolerant_OpenEntry_LowerRefuses_NamingTheEntry() {
        var (session, domain, analysis, _) = Analyze(NotesDomain, [Claim()]);
        var ex = Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));
        await Assert.That(ex.Message).Contains("notes-width");
        await Assert.That(session.ArtifactCatalog.Artifacts.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Tolerant_OpenEntry_EmitRefuses() {
        var (session, domain, analysis, _) = Analyze(NotesDomain, [Claim()]);
        var ex = Assert.Throws<InvalidOperationException>(() => session.Emit(domain, analysis));
        await Assert.That(ex.Message).Contains("notes-width");
    }

    [Test]
    public async Task Tolerant_OpenEntry_SimulationPathRefuses() {
        var (session, domain, analysis, _) = Analyze(NotesDomain, [Claim()]);
        var ex = Assert.Throws<InvalidOperationException>(() => RuntimeAnalysisCache.GetOrLower(domain, session, analysis));
        await Assert.That(ex.Message).Contains("notes-width");
    }

    [Test]
    public async Task Tolerant_OpenEntry_ExecutorRefuses() {
        var (_, domain, _, _) = Analyze(NotesDomain, [Claim()]);
        var ex = Assert.Throws<InvalidOperationException>(() => new ModuleInstanceExecutor(domain));
        await Assert.That(ex.Message).Contains("notes-width");
    }

    [Test]
    public async Task IdListed_ButNoBag_GateFailsClosed() {
        var (session, domain, _, _) = Analyze(NotesDomain, [Claim()]);
        var bare = DomainSession.ForExtensions([], ProductCatalog.Catalog);
        var foreign = bare.AnalyzeWithoutBind(domain);
        var ex = Assert.Throws<InvalidOperationException>(
            () => RuntimeAnalysisCache.GetOrLower(domain, session, foreign));
        await Assert.That(ex.Message).Contains("no contradiction bag");
    }

    [Test]
    public async Task Compatible_And_EqualWidth_ProduceNoEntry() {
        var (_, domain, analysis, _) = Analyze(NotesDomain, [
            Claim("narrower-source", "Order.Notes", "Customer.Notes"),
            Claim("equal", "Order.Notes", "Order.Notes2")
        ]);
        await Assert.That(BagOf(domain, analysis).Entries.Count).IsEqualTo(0);
    }

    private const string FlowDomain = """
        domain F
        uses contradictions
        Src: entity {
          Code: Text
          Qty: Number optional
          Amount: Decimal
        }
        Dst: entity {
          Code: Text
          Qty: Number
          Amount: Decimal
        }
        """;

    private static async Task<ContradictionEntry> SingleEntry(ContradictionClaim claim) {
        var (_, domain, analysis, ok) = Analyze(FlowDomain, [claim]);
        await Assert.That(ok).IsTrue();
        return BagOf(domain, analysis).Entries.Single();
    }

    [Test]
    public async Task ScaleNarrowing_PrecisionOrScale_ProducesEntry() {
        var byScale = await SingleEntry(Claim("s", "Src.Amount", "Dst.Amount",
            sourceShape: new ContradictionShape(Precision: 12, Scale: 4),
            targetShape: new ContradictionShape(Precision: 12, Scale: 2)));
        await Assert.That(byScale.Kinds.Single()).IsEqualTo(ContradictionKind.ScaleNarrowing);
        var byPrecision = await SingleEntry(Claim("p", "Src.Amount", "Dst.Amount",
            sourceShape: new ContradictionShape(Precision: 18, Scale: 2),
            targetShape: new ContradictionShape(Precision: 10, Scale: 2)));
        await Assert.That(byPrecision.Kinds.Single()).IsEqualTo(ContradictionKind.ScaleNarrowing);
    }

    [Test]
    public async Task Nullability_NullableSource_RequiredTarget_ProducesEntry() {
        var entry = await SingleEntry(Claim("n", "Src.Qty", "Dst.Qty"));
        await Assert.That(entry.Kinds.Single()).IsEqualTo(ContradictionKind.Nullability);
        await Assert.That(entry.SourceShape.IsNullable!.Value).IsTrue();
        await Assert.That(entry.TargetShape.IsNullable!.Value).IsFalse();
    }

    [Test]
    public async Task TypeCoercion_TextIntoNumber_ProducesEntry() {
        var entry = await SingleEntry(Claim("t", "Src.Code", "Dst.Qty",
            sourceShape: new ContradictionShape(Family: ContradictionFamily.Text, DisplayType: "VARCHAR2"),
            targetShape: new ContradictionShape(Family: ContradictionFamily.Number, DisplayType: "NUMBER")));
        await Assert.That(entry.Kinds.Single()).IsEqualTo(ContradictionKind.TypeCoercion);
        await Assert.That(entry.SourceShape.DisplayType).IsEqualTo("VARCHAR2");
    }

    [Test]
    public async Task TypeCoercion_DeclaredFamilies_ProduceEntry() {
        var entry = await SingleEntry(Claim("declared", "Src.Code", "Dst.Qty"));
        await Assert.That(entry.Kinds.Single()).IsEqualTo(ContradictionKind.TypeCoercion);
    }

    [Test]
    public async Task MultiKind_WidthAndNullability_Ordered() {
        var entry = await SingleEntry(Claim("both", "Src.Code", "Dst.Code",
            sourceShape: new ContradictionShape(MaxLength: 200, IsNullable: true),
            targetShape: new ContradictionShape(MaxLength: 50, IsNullable: false)));
        await Assert.That(entry.Kinds.Count).IsEqualTo(2);
        await Assert.That(entry.Kinds[0]).IsEqualTo(ContradictionKind.WidthNarrowing);
        await Assert.That(entry.Kinds[1]).IsEqualTo(ContradictionKind.Nullability);
    }

    [Test]
    public async Task UnknownFacts_ProduceNoEntry() {
        var (_, domain, analysis, ok) = Analyze(FlowDomain, [
            Claim("a", "Src.Amount", "Dst.Amount"),
            Claim("b", "Src.Code", "Dst.Code", sourceShape: new ContradictionShape(Scale: 4), targetShape: new ContradictionShape(Scale: 2)),
            Claim("c", "Src.Code", "Dst.Code", sourceShape: new ContradictionShape(MaxLength: 200))
        ]);
        await Assert.That(ok).IsTrue();
        await Assert.That(BagOf(domain, analysis).Entries.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Resolved_And_Withdrawn_DoNotBlock_AndStayInBag() {
        var (session, domain, analysis, _) = Analyze(NotesDomain, [
            Claim("done", status: ContradictionStatus.Resolved),
            Claim("gone", status: ContradictionStatus.Withdrawn)
        ]);
        var bag = BagOf(domain, analysis);
        await Assert.That(bag.Entries.Count).IsEqualTo(2);
        await Assert.That(bag.Open.Count).IsEqualTo(0);
        await Assert.That(session.Lower(domain, analysis).Count).IsGreaterThan(0);
        await Assert.That(session.Emit(domain, analysis).Count).IsGreaterThan(0);
    }

    [Test]
    public async Task Resolved_NowCompatible_StillInBag_WithNoKinds() {
        var (session, domain, analysis, _) = Analyze(NotesDomain, [
            Claim("widened", source: "Order.Notes", target: "Customer.Notes", status: ContradictionStatus.Resolved)
        ]);
        var entry = BagOf(domain, analysis).Entries.Single();
        await Assert.That(entry.Status).IsEqualTo(ContradictionStatus.Resolved);
        await Assert.That(entry.Kinds.Count).IsEqualTo(0);
        await Assert.That(session.Lower(domain, analysis).Count).IsGreaterThan(0);
    }

    [Test]
    public async Task Resolved_WithOpenSibling_StillBlocks() {
        var (session, domain, analysis, _) = Analyze(NotesDomain, [
            Claim("done", status: ContradictionStatus.Resolved),
            Claim("still-open")
        ]);
        var ex = Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));
        await Assert.That(ex.Message).Contains("still-open");
        await Assert.That(ex.Message.Contains("done")).IsFalse();
    }

    [Test]
    public async Task WithDomain_Reload_CarriesClaims_OrFailsClosed() {
        var (session, domain, _, _) = Analyze(NotesDomain, [Claim()]);
        var same = session.WithDomain(domain);
        await Assert.That(same.ContradictionClaims.Count).IsEqualTo(1);

        // Dropping the id is the strict-mode branch.
        var changed = domain with { Extensions = [] };
        await Assert.That(() => session.WithDomain(changed)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task WithDomain_Reload_UnknownId_FailsClosed() {
        var (session, domain, _, _) = Analyze(NotesDomain, [Claim()]);
        var reloaded = domain with { Extensions = ["contradictions", "storage"] };
        var ex = Assert.Throws<InvalidOperationException>(() => session.WithDomain(reloaded));
        await Assert.That(ex.Message).Contains("contradictions");
    }

    [Test]
    public async Task NullClaimElement_BindThrows() {
        var builder = SessionBuilder.CreateEmpty().LoadCore();
        await Assert.That(() => builder.WithContradictionClaims([null!])).Throws<ArgumentException>();
    }

    private static async Task AssertInvalid(params ContradictionClaim[] claims) {
        var (_, _, analysis, ok) = Analyze(NotesDomain, claims);
        await Assert.That(ok).IsFalse();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == "DMCONT001" && d.Severity == Poly.Analysis.DiagnosticSeverity.Error)).IsTrue();
    }

    [Test]
    public async Task UnresolvablePath_DuplicateName_BlankName_EmptyClaimIds_UndefinedStatus_SamePath_AreDMCONT001() {
        await AssertInvalid(Claim(source: "Customer.Missing"));
        await AssertInvalid(Claim(target: "Nobody.Notes"));
        await AssertInvalid(Claim(source: "Customer/Notes"));
        await AssertInvalid(Claim(source: "D.Customer.Notes"));
        await AssertInvalid(Claim("dup"), Claim("dup", "Customer.Notes", "Order.Notes2"));
        await AssertInvalid(Claim(" "));
        await AssertInvalid(Claim(ids: []));
        await AssertInvalid(Claim(ids: ["ok", " "]));
        await AssertInvalid(Claim(status: (ContradictionStatus)99));
        await AssertInvalid(Claim(target: "Customer.Notes"));
        await AssertInvalid(Claim(sourceShape: new ContradictionShape(MaxLength: -1)));
        await AssertInvalid(Claim(targetShape: new ContradictionShape(Precision: -1)));
        await AssertInvalid(Claim(targetShape: new ContradictionShape(Scale: -2)));
        await AssertInvalid(Claim(status: ContradictionStatus.Withdrawn, source: "Customer.Missing"));
    }

    [Test]
    public async Task InvalidClaim_LowerRefuses_ThroughAnalysisErrors() {
        var (clean, domain, _, _) = Analyze(NotesDomain);
        var claimed = DomainSession.ForAuthoringSource(
            NotesDomain, ExtensionCatalog.ProductAuthoring, ProductCatalog.Catalog, [Claim(source: "Customer.Missing")])
            .WithDomain(domain);
        var analysis = claimed.Analyze(domain);
        await Assert.That(analysis.HasErrors).IsTrue();
        await Assert.That(() => claimed.Lower(domain, analysis)).Throws<InvalidOperationException>();
        await Assert.That(clean.ContradictionClaims.Count).IsEqualTo(0);
    }
}