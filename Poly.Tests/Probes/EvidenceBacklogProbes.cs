using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.Tests.Probes;

/// <summary>Probe tests for the evidence backlog. Red is intended: each states the EXPECTED behaviour.</summary>
public class EvidenceBacklogProbes {
    private static IReadOnlyList<Diagnostic> Diagnose(string dsl) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(dsl).Parse());
        var all = result.Analysis.Diagnostics.ToList();
        if (result.Root is not null)
            all.AddRange(DomainModelAnalyzer.Analyze(result.Root).Diagnostics);
        return all;
    }

    private static string Dump(IEnumerable<Diagnostic> ds) =>
        string.Join(" | ", ds.Select(d => $"{d.Severity} {d.Code} {d.Message}"));

    [Test]
    public async Task Item08_MixedLongUlongRange_DoesNotThrow() {
        var domain = DomainTestFactory.Create("D", [
            new ValueType("V", [new Property("Value", new DomainTypeReference("Number"),
                [new RangeConstraint(0L, ulong.MaxValue)])], [])]);
        Exception? thrown = null;
        try { DomainModelAnalyzer.Analyze(domain); } catch (Exception e) { thrown = e; }
        await Assert.That(thrown?.ToString()).IsNull();
    }

    [Test]
    public async Task Item19_UnknownFunctionReturnType_ReportsPositionedErrorNamingMoney() {
        var all = Diagnose("domain P\nfunction F(): Money = 1\n");
        var errors = all.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        await Assert.That("all=[" + Dump(all) + "]" + (errors.Any(d => d.Message.Contains("Money")) ? "ok" : "NOERRORNAMINGMONEY"))
            .DoesNotContain("NOERRORNAMINGMONEY");
    }

    [Test]
    public async Task Item20_ValueDoubleUnderscoreStageName_RejectedOrCompiles() {
        const string dsl = "domain P\nX: entity {\n  value__: stage { }\n  Two: stage { }\n}\n";
        string cs;
        try {
            var (domain, analysis) = EvolvedDomain.FromDsl(dsl);
            cs = new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis));
        }
        catch (InvalidOperationException) {
            return; // analysis rejected it: expected
        }
        var thrown = Record.Exception(() => ExportedCSharp.CompileAndLoad(cs, "ValueUnderscore"));
        await Assert.That(thrown?.Message.Split('\n')[0]).IsNull();
    }

    /// <summary>Error text from parse or analysis, or "" when the document is accepted.</summary>
    private static string ErrorsOf(string dsl) {
        try {
            return Dump(Diagnose(dsl).Where(d => d.Severity == DiagnosticSeverity.Error));
        }
        catch (FormatException e) {
            return "PARSE " + e.Message;
        }
    }

    private const string EntryHead = "domain E\nLine: entity {\n  N: Text optional\n  Z: action { assign N to \"z\" }\n}\n";

    [Test]
    public async Task Item02a_EntryInvoke_IsRejected() {
        var errors = ErrorsOf(EntryHead + "X: entity {\n  Count: Number optional\n  A: stage {\n    entry { invoke Bump }\n    Bump: action { assign Count to 1 }\n  }\n}\n");
        await Assert.That(errors).IsNotEmpty();
    }

    [Test]
    public async Task Item02b_EntryCreate_IsRejected() {
        var errors = ErrorsOf(EntryHead + "X: entity {\n  Count: Number optional\n  A: stage {\n    entry { create Line { N: \"a\" } }\n  }\n}\n");
        await Assert.That(errors).IsNotEmpty();
    }

    [Test]
    public async Task Item02c_EntryForInvoke_IsRejected() {
        var errors = ErrorsOf(EntryHead + "X: entity {\n  lines: many Line\n  A: stage {\n    entry { for lines as l invoke l.Z }\n  }\n}\n");
        await Assert.That(errors).IsNotEmpty();
    }

    [Test]
    public async Task Item17_ManyToOneToKeywordNamedEntity_PrintsAndRereadsCleanly() {
        const string dsl = "domain M\nOrder: entity {\n  Code: Text\n}\nSchedule: entity {\n  Day: Text\n}\n";
        var parsed = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(dsl).Parse());
        var added = new DomainEvolution(parsed.Root).ApplyAndAnalyze([
            new AddRelationshipChange("schedule", new DomainTypeReference("Order"),
                new DomainTypeReference("Schedule"), RelationshipCardinality.ManyToOne, [])]);
        var printed = new DomainDslPrinter().Print(added.Root);
        string outcome;
        try {
            var reread = new DomainEvolution(DomainTestFactory.Create("_", [], []))
                .ApplyAndAnalyze(new PolyDslParser(printed).Parse());
            outcome = reread.Succeeded ? "ok" : "ANALYSIS " + reread.FailureSummary;
        }
        catch (FormatException e) {
            outcome = "PARSE " + e.Message;
        }
        await Assert.That(outcome + " <<" + printed.Replace('\n', '~') + ">>").StartsWith("ok");
    }

    [Test]
    public async Task Item17_F8_OmittedUsesRedefine_ReproducesFreshModel() {
        const string withUses = "domain A\nuses http\nT: entity { N: Text }\n";
        const string without = "domain B\nU: entity { M: Text }\n";
        var h = PolySurfaceHarness.New();
        var fresh = h.Open();
        h.Define(fresh, without);
        var freshDsl = h.Export(fresh, "dsl").Content;
        var dirty = h.Open();
        var first = h.Define(dirty, withUses);
        var second = h.Define(dirty, without);
        var dirtyDsl = h.Export(dirty, "dsl").Content;
        await Assert.That($"first={first.Success} second={second.Success}\n{dirtyDsl}").IsEqualTo($"first={first.Success} second={second.Success}\n{freshDsl}");
    }

    // ---- Item 13: [Range] bound beyond int --------------------------------------------------
    [Test]
    public async Task Item13_RangeBoundBeyondInt_OnLong_IsExact() {
        var lines = new List<string>();
        foreach (var bound in new[] { 4_294_967_296L, 9_007_199_254_740_992L, 9_007_199_254_740_993L, long.MaxValue - 1 }) {
            var args = Poly.Packs.Product.RangeAttributeArguments.Build("long", 0L, bound);
            var emitted = ((Poly.Ast.Nodes.Constant)args[1]).Value;
            var back = emitted is double d ? (d >= 9.2233720368547758E18 ? "overflow" : ((long)d).ToString()) : Convert.ToString(emitted)!;
            lines.Add($"{bound}->{emitted?.GetType().Name}:{back}{(back == bound.ToString() ? "" : " LOSSY")}");
        }
        await Assert.That(string.Join("; ", lines)).DoesNotContain("LOSSY");
    }

    // ---- Item 14: FoldNumber past 28 significant digits -------------------------------------
    private static string DefaultLiteral(string text) {
        try {
            var (domain, _) = EvolvedDomain.FromDsl($"domain F\nX: entity {{\n  Amount: Decimal default({text})\n}}\n");
            var prop = domain.Types.OfType<Entity>().Single().Properties.Single();
            var lit = prop.Constraints.OfType<DefaultValueConstraint>().Single().Expression as Literal;
            var v = lit!.Value;
            return $"{v?.GetType().Name}:{Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)}";
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException) {
            return "ERROR:" + e.Message;
        }
    }

    [Test]
    public async Task Item14_FoldNumber_BeyondDecimalPrecision_IsExactOrError() {
        var cases = new[] {
            "0.1234567890123456789012345678",            // 28 sig digits: control
            "0.12345678901234567890123456789",           // 29
            "1.00000000000000000000000000001",           // 30 (rounds to integral 1)
            "12345678901234567890123456789.5",           // 31
            "99999999999999999999999999999999",           // 32-digit integer > decimal.MaxValue
        };
        var report = new List<string>();
        foreach (var c in cases) {
            var r = DefaultLiteral(c);
            var exact = r.StartsWith("ERROR") || r.EndsWith(":" + c);
            report.Add($"{c} => {r}{(exact ? "" : " LOSSY")}");
        }
        await Assert.That(string.Join("\n", report)).DoesNotContain("LOSSY");
    }

    // ---- Item 07: require-pinned entity-level action ----------------------------------------
    [Test]
    public async Task Item07_RequirePinnedEntityLevelAction_IsNotRejectedByDmeff017() {
        const string dsl = """
            domain R
            Z: entity {
              Mode: Text optional
              InB: policy { Mode is "b" }
              Go: action require InB { transition to B }
              A: stage require not InB { exit { transition to C } }
              B: stage { }
              C: stage { }
            }
            """;
        var all = Diagnose(dsl);
        await Assert.That(Dump(all.Where(d => d.Severity == DiagnosticSeverity.Error)) + "|ALL:" + Dump(all)).DoesNotContain("DMEFF017");
    }

    // ---- Item 12: parity harness temporal gap -----------------------------------------------
    [Test]
    public async Task Item12_ParityScenario_OverDateProperty_BuildsAndAgrees() {
        const string dsl = """
            domain T
            uses temporal
            Z: entity {
              Name: Text
              Due: Date optional
              Renew: action { assign Due to Today }
            }
            """;
        var scenario = ParityScenario.FromDsl(dsl, "TemporalParity");
        var outcomes = await scenario.AssertAgree(side => {
            side.Create("Z", ("name", "a"));
            side.Invoke("Renew");
        });
        await Assert.That(outcomes.Count).IsGreaterThan(0);
    }

    // ---- Item 18: GrammarChangeProtocolTests has no pattern/printer-fill rung ----------------
    [Test]
    public async Task Item18_GrammarChangeProtocol_ChecksPatternRowsAgainstPrinterFills() {
        var methods = typeof(Poly.Tests.Grammar.GrammarChangeProtocolTests)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Select(m => m.Name).ToList();
        // A row added to the Core grammar is invisible to every check below (they enumerate token kinds only).
        var core = DslGrammar.Core;
        var rows = core.KnownRules.Sum(r => core.GetPatterns(r).Count);
        await Assert.That(methods.Any(n => n.Contains("Pattern") || n.Contains("Fill") || n.Contains("Printer")))
            .IsTrue().Because($"protocol tests = [{string.Join(", ", methods)}]; Core grammar has {rows} pattern rows none of them checked");
    }

    // ---- Item 23h: guide markers served to clients ------------------------------------------
    [Test]
    public async Task Item23h_GuideServedToClients_CarriesNoPolyVerifyMarkers() {
        var h = PolySurfaceHarness.New();
        var counts = new List<string>();
        var total = 0;
        foreach (var topic in new string?[] { null, "all", "1", "9", "13" }) {
            var r = h.Guide(topic);
            var text = r.Summary + (r.Data?.ToJsonString() ?? "");
            var n = System.Text.RegularExpressions.Regex.Matches(text, "poly-verify").Count;
            total += n;
            counts.Add($"{topic ?? "(quickstart)"}:{n}(len {text.Length})");
        }
        await Assert.That($"markers={total} [{string.Join(", ", counts)}]").IsEqualTo("markers=0 [" + string.Join(", ", counts) + "]");
    }

    // ---- Item 23d: Length / Pattern / Equality constant writes ------------------------------
    private static string WriteFindings(string constraint, string value) {
        var all = Diagnose($"domain W\nX: entity {{\n  V: Text {constraint}\n  Set: action {{ assign V to \"{value}\" }}\n}}\n");
        return Dump(all.Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
    }

    [Test]
    public async Task Item23d_AssignTooLongConstantToLengthProperty_IsReported() =>
        await Assert.That(WriteFindings("length(1, 3)", "toolong")).IsNotEmpty();

    [Test]
    public async Task Item23d_AssignPatternViolatingConstant_IsReported() =>
        await Assert.That(WriteFindings("pattern(\"^[A-Z]+$\")", "abc")).IsNotEmpty();

    [Test]
    public async Task Item23d_AssignDifferentConstantToEqualityProperty_IsReported() =>
        await Assert.That(WriteFindings("equals(\"Active\")", "Other")).IsNotEmpty();

    // ---- Item 23c: create initializer judged against declared range only --------------------
    private static string RangeFindings(string dsl) =>
        Dump(Diagnose(dsl).Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning
            && d.Code is DomainModelDiagnosticCodes.EffectConstraintViolation or DomainModelDiagnosticCodes.ConstraintUndecided));

    [Test]
    public async Task Item23c_CreateInitializerNarrowedByRequire_IsNotFlagged() {
        var control = RangeFindings("""
            domain C
            Order: entity {
              Qty: Number range(0, 100)
              Local: Number range(0, 10) optional
              Small: policy { Qty <= 10 }
              Copy: action require Small { assign Local to Qty }
            }
            """);
        var create = RangeFindings("""
            domain C
            Item: entity { Qty: Number range(0, 10) }
            Order: entity {
              Qty: Number range(0, 100)
              Small: policy { Qty <= 10 }
              Make: action require Small { create Item { Qty: Qty } }
            }
            """);
        await Assert.That($"control=[{control}] create=[{create}]").IsEqualTo("control=[" + control + "] create=[]");
    }

    // ---- Item 23e: unknown action-parameter type --------------------------------------------
    [Test]
    public async Task Item23e_UnknownActionParameterType_NamesTheParameter() {
        var errors = ErrorsOf("domain P\nX: entity {\n  N: Text optional\n  Go: action (x: Missing) { assign N to \"a\" }\n}\n");
        await Assert.That(errors + " <<expect: no \"Property 'x'\">>").DoesNotContain("Property 'x'");
    }

    // ---- Item 23f: {Other}Stage shadowing ---------------------------------------------------
    [Test]
    public async Task Item23f_PropertyNamedLikeOtherEntitysStageEnum_IsRejectedOrCompiles() {
        const string dsl = """
            domain S
            Order: entity {
              Touched: Text optional
              Touch: action { assign Touched to "t" }
              A: stage { Go: action { transition to B } }
              B: stage { }
            }
            Loan: entity {
              OrderStage: Text optional
              orders: many Order
              Sweep: action { for orders as o where o in A invoke o.Touch }
            }
            """;
        string cs;
        try {
            var (domain, analysis) = EvolvedDomain.FromDsl(dsl);
            cs = new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis));
        }
        catch (InvalidOperationException) {
            return; // analysis rejected: expected
        }
        var thrown = Record.Exception(() => ExportedCSharp.CompileAndLoad(cs, "ShadowStage"));
        await Assert.That(thrown?.Message.Split('\n')[0]).IsNull();
    }

    // ---- Item 23b: fallback in ValidateRequiredInitializerCoverage ---------------------------
    [Test]
    public async Task Item23b_EveryEntityHasStructureBag_SoTheFlatFallbackIsUnreachable() {
        var (domain, analysis) = EvolvedDomain.FromDsl("""
            domain B
            Item: entity { Qty: Number optional  A: stage { } B2: stage { } }
            Order: entity { items: many Item  Make: action { create in items { } } }
            """);
        var missing = domain.Types.OfType<Entity>()
            .Where(e => analysis.GetMetadata<EntityStructureMetadata>(e) is null).Select(e => e.Name).ToList();
        await Assert.That(string.Join(",", missing)).IsEqualTo("");
    }

    // ---- F9: uses order through the Load / re-read path -------------------------------------
    [Test]
    public async Task Item17_F9_UsesOrder_SurvivesDslRead() {
        var (domain, _) = EvolvedDomain.FromDsl("domain U\nuses persistence\nuses temporal\nE: entity { N: Text }\n");
        await Assert.That(string.Join(",", domain.Extensions)).StartsWith("persistence,temporal");
    }
}

internal static class Record {
    public static Exception? Exception(System.Action a) {
        try { a(); return null; } catch (Exception e) { return e; }
    }
}