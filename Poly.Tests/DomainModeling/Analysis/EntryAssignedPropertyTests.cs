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
        await Assert.That(errors.Single().Message).Contains("may be set by");
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
        var result = new PolyCompiler(ProductCatalog.Catalog, new SessionOptions(ProductCatalog.Catalog, ExtensionCatalog.ProductAuthoring)).Compile(dsl, "entry-assigned.poly");

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
              token: Token via Items
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

    // ── Residuals and deliberate over-approximations (sign-off follow-ups) ───────────────

    /// <summary>
    /// Deliberate fail-closed over-approximation under ruling B: the entry <c>for</c> over the
    /// entity's own (empty at create) <c>many</c> never runs its body, so a supplied P would survive,
    /// but the may fact walks the body unconditionally and DMEFF019 fires.
    /// </summary>
    [Test]
    public async Task CreateSuppliesPropertyAssignedInsideZeroTripForLoop_IsDMEFF019_Deliberately() => await AssertOneOnInitializer("""
        domain D16
        Kid: entity { }
        Token: entity {
          Kids: many Kid
          P: Number optional
          Q: stage { entry { for k in Kids { assign P to 1 } } }
        }
        Box: entity { Make: action { create Token { P: 5 } } }
        """);

    /// <summary>
    /// RESIDUAL (named, fail open for DMEFF019 only): a fan-out <c>for Rel as x invoke x.Action</c> in
    /// the entry reaches the CHILD's action; the walk has no <c>ForEachInvokeEffect</c> case, so a
    /// property that action assigns is not in the may set and no DMEFF019 is reported.
    /// </summary>
    [Test]
    public async Task CreateSuppliesPropertyAssignedOnlyByEntryFanOutInvoke_IsClean() {
        var errors = Errors("""
            domain D17
            Kid: entity {
              owner: Token
              Touch: action { invoke owner.Fix }
            }
            Token: entity {
              Kids: many Kid
              P: Number optional
              Fix: action { assign P to 1 }
              Q: stage { entry { for Kids as k invoke k.Touch } }
            }
            Box: entity { Make: action { create Token { P: 5 } } }
            """);
        await Assert.That(errors.Count).IsEqualTo(0);
    }

    // ── Characterization pin (Step 4): the two facts stay apart ──────────────────────────

    /// <summary>
    /// Characterization, green on master: it is the drift alarm for DMEFF019, not a red test. A
    /// conditionally entry-assigned property keeps its constructor parameter and its requiredness
    /// (the definite fact excludes it), while the may fact holds it.
    /// </summary>
    [Test]
    public async Task ConditionallyEntryAssignedProperty_KeepsCtorParameterAndRequiredness() {
        var (domain, analysis) = Analyze("""
            domain D15
            Z: entity {
              P: Number
              Flag: Boolean default(false)
              Q: stage { entry { if (Flag is true) { assign P to 1 } } }
            }
            """);
        var z = domain.Types.OfType<Entity>().Single(e => e.Name == "Z");
        var structure = analysis.GetStructure(z)!;

        await Assert.That(structure.ConstructorParameters.Any(p => p.Name == "P")).IsTrue();
        await Assert.That(structure.RequiredPropertyNames.Contains("P")).IsTrue();
        await Assert.That(structure.EntryAssignedPropertyNames.Contains("P")).IsFalse();
        await Assert.That(StageEntryFacts.EntryAssignedProperties(z, domain).Contains("P")).IsTrue();
    }

    /// <summary>
    /// Probe (review-deep 7): an entry cannot <c>assign</c> a singular navigation (analysis rejects it,
    /// DMEFF001: not a property), so the fact never holds a nav name and the nav-binding
    /// <c>continue</c> before <c>ValidateNotEntryAssigned</c> cannot hide a DMEFF019.
    /// </summary>
    [Test]
    public async Task EntryAssignToSingularNavigation_IsRejected_SoNavBindingNeverNeedsDMEFF019() {
        var errors = Errors("""
            domain NavAssign
            Person: entity { Name: Text optional }
            Token: entity {
              Owner: Person
              Q: stage { entry { assign Owner to Owner } }
            }
            """);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Code).IsEqualTo(DomainModelDiagnosticCodes.EffectBinding);
        await Assert.That(errors[0].Message).Contains("does not exist on entity 'Token'");
    }

    // ── Corpus pin (A7) ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// The corpus is applied without a session, so files that need a pack (<c>uses</c>) or fail to
    /// apply are skipped. The analyzed count and the skipped set are pinned exactly, so a loading
    /// regression cannot hide a DMEFF019.
    /// </summary>
    [Test]
    public async Task Corpus_NoPolyFileReportsDMEFF019() {
        var root = FindRepoRoot();
        var offenders = new List<string>();
        var skipped = new List<string>();
        var analyzed = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*.poly", SearchOption.AllDirectories)
                     .Where(p => !IsBuildOutput(root, p))
                     .Order(StringComparer.Ordinal)) {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            try {
                var applied = new DomainEvolution(DomainTestFactory.Create("_", [], []))
                    .Apply(new PolyDslParser(File.ReadAllText(path)).Parse());
                if (!applied.Succeeded) {
                    skipped.Add($"{relative}: apply failed");
                    continue;
                }
                analyzed++;
                if (DomainModelAnalyzer.Analyze(applied.Root).Diagnostics.Any(d => d.Code == Code))
                    offenders.Add(relative);
            }
            catch (Exception e) {
                skipped.Add($"{relative}: {e.GetType().Name}");
            }
        }
        await Assert.That(offenders).IsEquivalentTo(Array.Empty<string>(), StringComparer.Ordinal);
        // The skipped files need a pack session (they throw InvalidOperationException without one).
        await Assert.That(skipped).IsEquivalentTo(new[] {
            "demo/Poly.RestApi/library.poly: InvalidOperationException",
            "docs/missions/evolve-2026-10-13/library.poly: InvalidOperationException",
            "docs/missions/evolve-2026-10-13/university-http.poly: InvalidOperationException",
            "docs/probes/grep-2026-10-17/grep.poly: InvalidOperationException",
            "docs/probes/scratch-2026-10-14/banking.poly: InvalidOperationException",
            "docs/probes/scratch-2026-10-14/fleet.poly: InvalidOperationException",
            "docs/probes/scratch-2026-10-14/gym.poly: InvalidOperationException",
            "docs/probes/scratch-2026-10-14/logistics.poly: InvalidOperationException",
            "docs/probes/scratch-2026-10-14/roastery.poly: InvalidOperationException"
        }, StringComparer.Ordinal);
        await Assert.That(analyzed).IsEqualTo(42);
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