using Poly.Analysis;
using Poly.Compile;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;
using Poly.Packs.Product;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// S-14 (#681, Q15 ruling B): a create that supplies a property the new instance's entry chain may
/// assign is a positioned analysis error, <c>DMEFF019</c>, on the initializer. The check reads the
/// may-assign fact (<see cref="StageEntryFacts.EntryAssignedProperties"/>); every relief consumer
/// keeps the definite flat fact, which the characterization pin at the end of this class holds apart.
/// <para>
/// Residuals, recorded for L-09 and failing open for <c>DMEFF019</c> only: a subscription-handler
/// assign reachable through an entry <c>create in</c> or a notify (see
/// <see cref="CreateSuppliesPropertyAssignedOnlyBySubscriptionHandler_IsClean"/>), and cross-entity and
/// <c>for … invoke</c> reach-back.
/// </para>
/// </summary>
public sealed class EntryAssignedPropertyTests {
    private const string Code = DomainModelDiagnosticCodes.CreateSuppliesEntryAssignedProperty;

    /// <summary>Applies without the analysis gate so a failing analysis still returns its diagnostics.</summary>
    private static (Domain Domain, AnalysisResult Analysis) Analyze(string dsl) {
        var session = ExtensionCatalog.Core.Authoring;
        var applied = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .Apply(new PolyDslParser(dsl, session).Parse(), session: session);
        if (!applied.Succeeded)
            throw new InvalidOperationException(applied.FailureSummary ?? "mutation failed");
        return (applied.Root, session.Analyze(applied.Root));
    }

    private static IReadOnlyList<Diagnostic> Errors(AnalysisResult analysis) =>
        analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

    private static IReadOnlyList<Diagnostic> Errors(string dsl) => Errors(Analyze(dsl).Analysis);

    private static string[] Codes(string dsl) =>
        Errors(dsl).Select(d => d.Code ?? "").Distinct().Order(StringComparer.Ordinal).ToArray();

    private static async Task AssertOneOnInitializer(string dsl, string property = "P") {
        var errors = Errors(dsl);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Code).IsEqualTo(Code);
        await Assert.That(errors[0].Node is PropertyBinding binding && binding.PropertyName == property).IsTrue();
        await Assert.That(errors[0].Message).Contains($"'{property}'");
        await Assert.That(errors[0].Message).Contains("default(");
        await Assert.That(errors[0].Message).Contains("optional");
        await Assert.That(errors[0].Message).DoesNotContain("single");
    }

    private const string Token = """
        Token: entity {
          P: Number optional
          N: Number default(0)
          Flag: Boolean default(false)
          Q: stage { entry { assign P to 1 } }
        }
        """;

    // ── Direct create ────────────────────────────────────────────────────────────────────

    [Test]
    public async Task CreateSuppliesEntryAssignedProperty_IsDMEFF019() => await AssertOneOnInitializer("""
        domain D1
        Token: entity {
          P: Number optional
          Q: stage { entry { assign P to 1 } }
        }
        Box: entity { Make: action { create Token { P: 5 } } }
        """);

    [Test]
    public async Task CreateSuppliesEntryAssignedProperty_MessageNamesTheInitialStage() {
        var errors = Errors("""
            domain D1b
            Token: entity {
              P: Number optional
              Q: stage { entry { assign P to 1 } }
            }
            Box: entity { Make: action { create Token { P: 5 } } }
            """);
        await Assert.That(errors.Single().Message).Contains("'Q'");
    }

    [Test]
    public async Task CreateSuppliesConditionallyEntryAssignedProperty_IsDMEFF019() {
        const string dsl = """
            domain D2
            Token: entity {
              P: Number optional
              Flag: Boolean default(false)
              Q: stage { entry { if (Flag is true) { assign P to 1 } } }
            }
            Box: entity { Make: action { create Token { P: 5 } } }
            """;
        await AssertOneOnInitializer(dsl);

        // The two-fact drift pin: the definite fact does not hold P, the may fact does.
        var (domain, analysis) = Analyze(dsl);
        var token = domain.Types.OfType<Entity>().Single(e => e.Name == "Token");
        await Assert.That(analysis.GetStructure(token)!.EntryAssignedPropertyNames.Contains("P")).IsFalse();
        await Assert.That(StageEntryFacts.EntryAssignedProperties(token, domain).Contains("P")).IsTrue();
    }

    [Test]
    public async Task CreateSuppliesNestedTransitionEntryAssignedProperty_IsDMEFF019() => await AssertOneOnInitializer("""
        domain D3
        Token: entity {
          P: Number optional
          A: stage { entry { transition to B } }
          B: stage { entry { assign P to 1 } }
        }
        Box: entity { Make: action { create Token { P: 5 } } }
        """);

    // ── create in ────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task CreateInSuppliesEntryAssignedProperty_IsDMEFF019() => await AssertOneOnInitializer("""
        domain D4
        Token: entity {
          P: Number optional
          Q: stage { entry { assign P to 1 } }
        }
        Box: entity {
          Tokens: many Token
          Make: action { create in Tokens { P: 5 } }
        }
        """);

    [Test]
    public async Task CreateInSuppliesConditionallyEntryAssignedProperty_IsDMEFF019() => await AssertOneOnInitializer("""
        domain D4b
        Token: entity {
          P: Number optional
          Flag: Boolean default(false)
          Q: stage { entry { if (Flag is true) { assign P to 1 } } }
        }
        Box: entity {
          Tokens: many Token
          Make: action { create in Tokens { P: 5 } }
        }
        """);

    // ── Invoked-action transition (Step 1's widened fact) ────────────────────────────────

    [Test]
    public async Task CreateSuppliesPropertyAssignedByInvokedActionTransition_IsDMEFF019() => await AssertOneOnInitializer("""
        domain D5
        Token: entity {
          P: Number optional
          Go: action { transition to B }
          A: stage { entry { invoke Go } }
          B: stage { entry { assign P to 1 } }
        }
        Box: entity { Make: action { create Token { P: 5 } } }
        """);

    // ── Clean shapes ─────────────────────────────────────────────────────────────────────

    [Test]
    public async Task CreateOmitsEntryAssignedProperty_IsClean() {
        var (_, analysis) = Analyze("""
            domain D6
            Token: entity {
              P: Number optional
              Q: stage { entry { assign P to 1 } }
            }
            Box: entity { Make: action { create Token { } } }
            """);
        await Assert.That(Errors(analysis).Count).IsEqualTo(0);
    }

    [Test]
    public async Task CreateSuppliesPropertyNoEntryAssigns_IsClean() {
        var errors = Errors("""
            domain D7
            Token: entity {
              P: Number optional
              N: Number default(0)
              Q: stage { entry { assign P to 1 } }
            }
            Box: entity { Make: action { create Token { N: 5 } } }
            """);
        await Assert.That(errors.Count).IsEqualTo(0);
    }

    // ── The DMEFF011 / DMEFF019 squeeze ──────────────────────────────────────────────────

    private const string SqueezeToken = """
        Token: entity {
          P: Number
          Flag: Boolean default(false)
          Q: stage { entry { if (Flag is true) { assign P to 1 } } }
        }
        """;

    [Test]
    public async Task CreateOmitsConditionallyEntryAssignedRequiredProperty_IsDMEFF011_NotDMEFF019() =>
        await Assert.That(Codes("domain D8a\n" + SqueezeToken + "\nBox: entity { Make: action { create Token { } } }"))
            .IsEquivalentTo(new[] { DomainModelDiagnosticCodes.CreateMissingRequiredProperty }, StringComparer.Ordinal);

    [Test]
    public async Task CreateSuppliesConditionallyEntryAssignedRequiredProperty_IsOnlyDMEFF019() =>
        await Assert.That(Codes("domain D8b\n" + SqueezeToken + "\nBox: entity { Make: action { create Token { P: 5 } } }"))
            .IsEquivalentTo(new[] { Code }, StringComparer.Ordinal);

    [Test]
    public async Task CreateSuppliesEntryAssignedProperty_WithDefaultEscape_OmittingIsClean() {
        var errors = Errors("""
            domain D8c
            Token: entity {
              P: Number default(0)
              Flag: Boolean default(false)
              Q: stage { entry { if (Flag is true) { assign P to 1 } } }
            }
            Box: entity { Make: action { create Token { } } }
            """);
        await Assert.That(errors.Count).IsEqualTo(0);
    }

    // ── Q21: the designated initial stage ────────────────────────────────────────────────

    [Test]
    public async Task CreateSuppliesPropertyAssignedByDesignatedInitialStage_IsDMEFF019() => await AssertOneOnInitializer("""
        domain D9
        Token: entity initial(Other) {
          P: Number optional
          First: stage { }
          Other: stage { entry { assign P to 1 } }
        }
        Box: entity { Make: action { create Token { P: 5 } } }
        """);

    [Test]
    public async Task CreateOmitsPropertyAssignedByDesignatedInitialStage_IsClean() {
        var errors = Errors("""
            domain D9b
            Token: entity initial(Other) {
              P: Number optional
              First: stage { }
              Other: stage { entry { assign P to 1 } }
            }
            Box: entity { Make: action { create Token { } } }
            """);
        await Assert.That(errors.Count).IsEqualTo(0);
    }

    // ── Unresolvable hops never throw out of analysis ────────────────────────────────────

    [Test]
    public async Task CreateOfEntityWithUnresolvableHop_ReportsDMEFF001Only_NoThrow() {
        var codes = Codes("""
            domain D10
            Token: entity {
              P: Number optional
              A: stage { entry { transition to Nope } }
            }
            Box: entity { Make: action { create Token { P: 5 } } }
            """);
        await Assert.That(codes).IsEquivalentTo(new[] { DomainModelDiagnosticCodes.EffectBinding }, StringComparer.Ordinal);
    }

    [Test]
    public async Task CreateOfEntityWithUnresolvableHopInInvokedCallee_ReportsDMEFF001Only_NoThrow() {
        var codes = Codes("""
            domain D10b
            Token: entity {
              P: Number optional
              Go: action { transition to Nope }
              A: stage { entry { invoke Go } }
            }
            Box: entity { Make: action { create Token { P: 5 } } }
            """);
        await Assert.That(codes).IsEquivalentTo(new[] { DomainModelDiagnosticCodes.EffectBinding }, StringComparer.Ordinal);
    }

    // ── Host placement ───────────────────────────────────────────────────────────────────

    [Test]
    public async Task Polyc_PlacesDMEFF019OnTheInitializer() {
        const string dsl = """
            domain D11
            Token: entity {
              P: Number optional
              Q: stage { entry { assign P to 1 } }
            }
            Box: entity {
              Make: action {
                create Token {
                  P: 5
                }
              }
            }
            """;
        var result = new PolyCompiler(ProductCatalog.Catalog, "authoring").Compile(dsl, "entry-assigned.poly");

        var errors = result.Diagnostics.Where(d => d.Code == Code).ToList();
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Placed).IsTrue();
        await Assert.That(errors[0].File).IsEqualTo("entry-assigned.poly");
        // An initializer has no written path of its own, so the host places the error on the
        // nearest placed ancestor: the creating action's line (7), not the initializer's (9).
        await Assert.That(errors[0].Line).IsEqualTo(7);
        await Assert.That(result.Succeeded).IsFalse();
    }

    // ── G7: the lifecycle property ───────────────────────────────────────────────────────

    [Test]
    public async Task RequiredLifecycleProperty_OmittedIsDMEFF011_SuppliedIsDMEFF019() {
        const string Lifecycle = """
            OrderState: enum { Pending, Paid }
            Order: entity {
              State: OrderState
              Pending: stage { entry { transition to Paid } }
              Paid: stage { }
            }
            """;
        await Assert.That(Codes("domain D12a\n" + Lifecycle + "\nShop: entity { Make: action { create Order { } } }"))
            .IsEquivalentTo(new[] { DomainModelDiagnosticCodes.CreateMissingRequiredProperty }, StringComparer.Ordinal);
        await Assert.That(Codes("domain D12b\n" + Lifecycle + "\nShop: entity { Make: action { create Order { State: Pending } } }"))
            .IsEquivalentTo(new[] { Code }, StringComparer.Ordinal);
    }

    // ── One verdict per initializer (fork 6) ─────────────────────────────────────────────

    [Test]
    public async Task CreateSuppliesViolatingLiteralOnEntryAssignedProperty_IsOnlyDMEFF019() {
        // Control: the violating literal is an error of its own on a property no entry assigns.
        var control = Codes("""
            domain D13a
            Token: entity {
              P: Number range(10, 20) optional
              N: Number optional
              Q: stage { entry { assign N to 1 } }
            }
            Box: entity { Make: action { create Token { P: 5 } } }
            """);
        await Assert.That(control.Length).IsGreaterThan(0);
        await Assert.That(control.Contains(Code)).IsFalse();

        var codes = Codes("""
            domain D13b
            Token: entity {
              P: Number range(10, 20) optional
              Q: stage { entry { assign P to 15 } }
            }
            Box: entity { Make: action { create Token { P: 5 } } }
            """);
        await Assert.That(codes).IsEquivalentTo(new[] { Code }, StringComparer.Ordinal);
    }

    // ── Residual (A3, Q2): subscription handlers are not modelled ────────────────────────

    /// <summary>
    /// RESIDUAL, fail open for DMEFF019 only: Token's <c>when Items S2</c> handler assigns <c>P</c>, and
    /// a handler fires during a create (an entry <c>create in</c> or a notify reaches it), so the
    /// supplied <c>P</c> can be overwritten at runtime. The fact excludes subscription handlers, so no
    /// DMEFF019 is reported. Recorded for L-09, which owns the lowered runtime rejection; the
    /// notify/watched-stage model is L-14 territory.
    /// </summary>
    [Test]
    public async Task CreateSuppliesPropertyAssignedOnlyBySubscriptionHandler_IsClean() {
        var errors = Errors("""
            domain D14
            Item: entity {
              S1: stage { Next: action { transition to S2 } }
              S2: stage { }
            }
            Token: entity {
              Items: many Item
              P: Number optional
              A: stage { when Items S2 { assign P to 1 } }
            }
            Box: entity { Make: action { create Token { P: 5 } } }
            """);
        await Assert.That(errors.Count).IsEqualTo(0);
    }

    // ── Corpus pin (A7) ──────────────────────────────────────────────────────────────────

    [Test]
    public async Task Corpus_NoPolyFileReportsDMEFF019() {
        var root = FindRepoRoot();
        var offenders = new List<string>();
        var analyzed = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*.poly", SearchOption.AllDirectories)
                     .Where(p => !IsBuildOutput(root, p))
                     .Order(StringComparer.Ordinal)) {
            try {
                var applied = new DomainEvolution(DomainTestFactory.Create("_", [], []))
                    .Apply(new PolyDslParser(File.ReadAllText(path)).Parse());
                if (!applied.Succeeded)
                    continue;
                analyzed++;
                if (DomainModelAnalyzer.Analyze(applied.Root).Diagnostics.Any(d => d.Code == Code))
                    offenders.Add(Path.GetRelativePath(root, path).Replace('\\', '/'));
            }
            catch (Exception) {
                continue;
            }
        }
        await Assert.That(analyzed).IsGreaterThanOrEqualTo(30);
        await Assert.That(offenders).IsEquivalentTo(Array.Empty<string>(), StringComparer.Ordinal);
    }

    private static bool IsBuildOutput(string root, string path) =>
        Path.GetRelativePath(root, path)
            .Split(Path.DirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj" or "node_modules" or ".git");

    private static string FindRepoRoot() {
        var dir = AppContext.BaseDirectory;
        while (dir is not null) {
            if (File.Exists(Path.Combine(dir, "docs/CORE.md")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not find repo root from " + AppContext.BaseDirectory);
    }
}