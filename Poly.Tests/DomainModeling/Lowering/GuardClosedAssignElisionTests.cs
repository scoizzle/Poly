using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// #367 C-1: <c>assign Target to Source</c> elides Target's assign guard only when Source is guard-closed
/// and Source's constraints imply Target's. The guard that remains is Source's own, on every write to Source.
/// </summary>
public class GuardClosedAssignElisionTests {
    private const string NumberQualifying = """
        domain Lab
        Item: entity {
          Qty: Number range(5, 10) default(5)
          Score: Number range(0, 20) default(0)
          Copy: action { assign Score to Qty }
        }
        """;

    [Test]
    public async Task QualifyingPair_TargetGuardIsGone() {
        var body = CopyBody(NumberQualifying, "Item", "Copy");

        await Assert.That(body).DoesNotContain("'Score' must be");
        await Assert.That(body).Contains("Score");
    }

    [Test]
    public async Task QualifyingPair_VerdictAndPostStateUnchanged() {
        var (domain, analysis, session) = Build(NumberQualifying);
        session.Lower(domain, analysis);
        var exec = ModuleExec.For(domain);
        var instance = exec.Create("Item", new Dictionary<string, object?> { ["Qty"] = 7.0 }).Instance!;

        var result = exec.Invoke(instance, "Copy");

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(Convert.ToDouble(instance.Bag["Score"])).IsEqualTo(7.0);
    }

    [Test]
    public async Task CannotHideViolation_OutOfRangeSourceIsRejectedBySourcesOwnGuard() {
        var (domain, analysis, session) = Build(NumberQualifying);
        session.Lower(domain, analysis);
        var exec = ModuleExec.For(domain);

        var tooHigh = exec.Create("Item", new Dictionary<string, object?> { ["Qty"] = 99.0 });
        var tooLow = exec.Create("Item", new Dictionary<string, object?> { ["Qty"] = 1.0 });

        await Assert.That(tooHigh.Succeeded).IsFalse();
        await Assert.That(tooHigh.Error).Contains("'Qty' must be <= 10.");
        await Assert.That(tooHigh.Error).DoesNotContain("'Score'");
        await Assert.That(tooLow.Succeeded).IsFalse();
        await Assert.That(tooLow.Error).Contains("'Qty' must be >= 5.");
        await Assert.That(tooLow.Error).DoesNotContain("'Score'");
    }

    [Test]
    public async Task CannotHideViolation_SourceWriteActionKeepsItsOwnGuard_NotClosed_SoTargetGuardStays() {
        // Qty gains a parameter writer: it is no longer guard-closed, so Copy keeps Score's guard,
        // and the parameter write is itself rejected by Qty's assign guard.
        const string poly = """
            domain Lab
            Item: entity {
              Qty: Number range(5, 10) default(5)
              Score: Number range(0, 20) default(0)
              SetQty: action (n: Number) { assign Qty to n }
              Copy: action { assign Score to Qty }
            }
            """;
        var (domain, analysis, session) = Build(poly);
        session.Lower(domain, analysis);
        var item = domain.Types.OfType<Entity>().Single();
        await Assert.That(analysis.GetStructure(item)!.GuardClosedPropertyNames!.Contains("Qty")).IsFalse();
        await Assert.That(CopyBody(poly, "Item", "Copy")).Contains("'Score' must be");

        var exec = ModuleExec.For(domain);
        var instance = exec.Create("Item", new Dictionary<string, object?> { ["Qty"] = 7.0 }).Instance!;
        var set = exec.Invoke(instance, "SetQty", new Dictionary<string, object?> { ["n"] = 99.0 });
        await Assert.That(set.Succeeded).IsFalse();
        await Assert.That(set.Error).Contains("'Qty' must be <= 10.");
        await Assert.That(Convert.ToDouble(instance.Bag["Qty"])).IsEqualTo(7.0);
    }

    [Test]
    public async Task NegativeNotGuardClosed_SourceAssignedFromParameter_KeepsTargetGuard() {
        const string poly = """
            domain Lab
            Item: entity {
              Qty: Number range(5, 10) default(5)
              Score: Number range(0, 20) default(0)
              SetQty: action (n: Number) { assign Qty to n }
              Copy: action { assign Score to Qty }
            }
            """;

        await Assert.That(CopyBody(poly, "Item", "Copy")).Contains("'Score' must be >= 0.");
        await Assert.That(CopyBody(poly, "Item", "Copy")).Contains("'Score' must be <= 20.");
    }

    [Test]
    public async Task Negative_ParameterNamedLikeAProperty_KeepsGuard() {
        const string poly = """
            domain Lab
            Item: entity {
              Qty: Number range(5, 10) default(5)
              Score: Number range(0, 20) default(0)
              Copy: action (Qty: Number) { assign Score to Qty }
            }
            """;

        await Assert.That(CopyBody(poly, "Item", "Copy")).Contains("'Score' must be");
    }

    [Test]
    public async Task Negative_SourceWeakerThanTarget_KeepsGuard_Number() {
        // Source [0,20] does not imply target [5,10]: the analysis downgrades to a warning, the guard stays.
        const string poly = """
            domain Lab
            Item: entity {
              Qty: Number range(0, 20) default(5)
              Score: Number range(5, 10) default(5)
              Copy: action { assign Score to Qty }
            }
            """;

        await Assert.That(CopyBody(poly, "Item", "Copy")).Contains("'Score' must be");
    }

    [Test]
    public async Task Negative_OneSidedRange_KeepsGuardForTheUncoveredSide() {
        // Source bounds only the minimum; target also bounds the maximum.
        const string poly = """
            domain Lab
            Item: entity {
              Qty: Number range(5, 100) default(5)
              Score: Number range(0, 20) default(5)
              Copy: action { assign Score to Qty }
            }
            """;

        await Assert.That(CopyBody(poly, "Item", "Copy")).Contains("'Score' must be <= 20.");
    }

    [Test]
    public async Task Text_LengthImplied_Elides_LengthNotImplied_Keeps() {
        const string implied = """
            domain Lab
            Item: entity {
              Code: Text length(3, 5)
              Label: Text length(1, 10)
              Copy: action { assign Label to Code }
            }
            """;
        const string notImplied = """
            domain Lab
            Item: entity {
              Code: Text length(1, 10)
              Label: Text length(3, 5)
              Copy: action { assign Label to Code }
            }
            """;

        await Assert.That(CopyBody(implied, "Item", "Copy")).DoesNotContain("'Label' must be");
        await Assert.That(CopyBody(notImplied, "Item", "Copy")).Contains("'Label' must be at least 3");
    }

    [Test]
    public async Task Text_PatternIdentical_Elides_PatternDifferent_Keeps() {
        const string same = """
            domain Lab
            Item: entity {
              Code: Text pattern("^[A-Z]+$")
              Label: Text pattern("^[A-Z]+$")
              Copy: action { assign Label to Code }
            }
            """;
        const string different = """
            domain Lab
            Item: entity {
              Code: Text pattern("^[A-Z]{3}$")
              Label: Text pattern("^[A-Z]+$")
              Copy: action { assign Label to Code }
            }
            """;

        await Assert.That(CopyBody(same, "Item", "Copy")).DoesNotContain("required pattern");
        await Assert.That(CopyBody(different, "Item", "Copy")).Contains("does not match the required pattern");
    }

    [Test]
    public async Task Text_Equality_IdenticalElides_MissingKeeps() {
        const string same = """
            domain Lab
            Item: entity {
              Code: Text equals("Active")
              Label: Text equals("Active")
              Copy: action { assign Label to Code }
            }
            """;
        const string unconstrainedSource = """
            domain Lab
            Item: entity {
              Code: Text
              Label: Text equals("Active")
              Copy: action { assign Label to Code }
            }
            """;

        await Assert.That(CopyBody(same, "Item", "Copy")).DoesNotContain("must equal");
        await Assert.That(CopyBody(unconstrainedSource, "Item", "Copy")).Contains("must equal");
    }

    [Test]
    public async Task NonQualifyingConstrainedEntity_ParameterAssign_LoweredTreeStillCarriesTheGuard() {
        // Control for byte-identity: a parameter-sourced assign is untouched by the elision.
        const string poly = """
            domain Lab
            Item: entity {
              Score: Number range(0, 20) default(0)
              Set: action (n: Number) { assign Score to n }
            }
            """;
        var body = CopyBody(poly, "Item", "Set");

        await Assert.That(body).Contains("'Score' must be >= 0.");
        await Assert.That(body).Contains("'Score' must be <= 20.");
    }

    private const string Chain = """
        domain Lab
        Item: entity {
          Qty: Number range(5, 10) default(5)
          Mid: Number range(0, 20) default(0)
          Top: Number range(0, 30) default(0)
          CopyMid: action { assign Mid to Qty }
          CopyTop: action { assign Top to Mid }
        }
        """;

    [Test]
    public async Task Widening_ChainedCopy_SecondLinkGuardIsNowGone() {
        // Mid is only written by a copy from closed Qty, so (fixpoint) it is closed and CopyTop needs no guard.
        await Assert.That(CopyBody(Chain, "Item", "CopyMid")).DoesNotContain("'Mid' must be");
        await Assert.That(CopyBody(Chain, "Item", "CopyTop")).DoesNotContain("'Top' must be");
    }

    [Test]
    public async Task Widening_ChainedCopy_CannotHideViolation_SourcesGuardStillFires() {
        var (domain, analysis, session) = Build(Chain);
        session.Lower(domain, analysis);
        var exec = ModuleExec.For(domain);

        var tooHigh = exec.Create("Item", new Dictionary<string, object?> { ["Qty"] = 99.0 });
        await Assert.That(tooHigh.Succeeded).IsFalse();
        await Assert.That(tooHigh.Error).Contains("'Qty' must be <= 10.");

        var instance = exec.Create("Item", new Dictionary<string, object?> { ["Qty"] = 7.0 }).Instance!;
        await Assert.That(exec.Invoke(instance, "CopyMid").Succeeded).IsTrue();
        await Assert.That(exec.Invoke(instance, "CopyTop").Succeeded).IsTrue();
        await Assert.That(Convert.ToDouble(instance.Bag["Top"])).IsEqualTo(7.0);
    }

    [Test]
    public async Task Widening_ChainedCopy_WeakerMiddle_KeepsTopGuard() {
        const string poly = """
            domain Lab
            Item: entity {
              Qty: Number range(5, 10) default(5)
              Mid: Number range(0, 50) default(0)
              Top: Number range(0, 30) default(0)
              CopyMid: action { assign Mid to Qty }
              CopyTop: action { assign Top to Mid }
            }
            """;

        await Assert.That(CopyBody(poly, "Item", "CopyMid")).DoesNotContain("'Mid' must be");
        await Assert.That(CopyBody(poly, "Item", "CopyTop")).Contains("'Top' must be <= 30.");
    }

    [Test]
    public async Task AssignElision_DecimalBoundAbove2Pow53_KeepsGuard() {
        // V-05a B3: S's maximum ...992.0000001 is above T's maximum ...992, so S does not imply T
        // and the printed C# must keep T's create guard. Both round to ...992.0 through double.
        const string poly = """
            domain Lab
            Item: entity {
              S: Decimal range(0, 9007199254740992.0000001) default(0)
              T: Decimal range(0, 9007199254740992) default(0)
              Copy: action { assign T to S }
            }
            """;

        await Assert.That(CopyBody(poly, "Item", "Copy")).Contains("'T' must be");
    }

    private static string CopyBody(string poly, string entityName, string actionName) {
        var (domain, analysis, session) = Build(poly);
        session.Lower(domain, analysis);
        if (!ActionBodies.TryGetModuleBody(domain, entityName, actionName, out var method)
            || method?.Body is null)
            throw new InvalidOperationException($"No module body for {entityName}.{actionName}.");
        return new CSharpGenerator().Generate(method.Body);
    }

    private static (Domain Domain, AnalysisResult Analysis, DomainSession Session) Build(string poly) {
        var session = DomainSession.ForSource(poly, ExtensionCatalog.ProductAuthoring);
        var changes = new PolyDslParser(poly, session).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes, session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        return (result.Root!, result.Analysis, session);
    }
}