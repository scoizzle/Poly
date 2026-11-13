using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology.Contract;
using Poly.Grammar;
using Poly.Interpretation.CSharp;
using Poly.Introspection;
using Poly.Packs.Product;
using Poly.Tests.Interpretation;

namespace Poly.Tests.Grammar;

/// <summary>One input the drive's writer or parser refused: the input's identity and the refusal message.</summary>
internal sealed record PatternRowRefusal(string Input, string Message);

/// <summary>An expected refusal: the input's identity, the message's first line (node ids shown as <c>…</c>) and its owner.</summary>
internal sealed record PatternRowExpectedRefusal(string Input, string FirstLine, string Owner);

/// <summary>
/// The change-protocol drive (G-23): prints both corpora inside one <see cref="PrintTrace"/> scope and
/// keeps every observation. The DSL drive is <see cref="GoldenDomains.Compilable"/> (parse, analyse, print)
/// plus <see cref="DslDocuments"/> and a code-built pinned <c>uses</c>; the C# drive is
/// <see cref="NodeFactCorpus.Units"/>, the <see cref="AstParityTests.Corpus"/> programs, and
/// <see cref="CSharpSources"/> / <see cref="CSharpTypes"/> for the shapes neither carries.
/// No negative literal is in either drive (#501 is F-04's).
/// </summary>
internal static class PatternRowCorpus {
    /// <summary>The DSL surface over <paramref name="grammar"/>: the reader's own tokenizer.</summary>
    public static PatternRowSurface<DslToken, DslTokenKind> DslSurface(Grammar<DslToken, DslTokenKind> grammar) =>
        new(PatternRowAllowList.Dsl, grammar, text => new DslTokenReader(text), ReportsSuppliedContent: false);

    /// <summary>The C# surface over <paramref name="grammar"/>: the reader's own tokenizer.</summary>
    public static PatternRowSurface<CSharpToken, CSharpTokenKind> CSharpSurface(Grammar<CSharpToken, CSharpTokenKind> grammar) =>
        new(PatternRowAllowList.CSharp, grammar, text => new CSharpTokenizer(text), ReportsSuppliedContent: true);

    /// <summary>DSL documents that carry table-printed rows the golden corpus may not.</summary>
    public static IReadOnlyList<(string Name, string Poly)> DslDocuments { get; } = [
        ("literals-and-dates", """
            domain Ledger
            uses temporal

            Invoice: entity {
              Title: Text
              Note: Text optional
              Amount: Number
              Paid: Boolean
              DueDate: Date
              HasNote: policy { Note != null }
              IsLarge: policy { Amount > 100 }
              IsNamed: policy { Title != "none" }
              IsSettled: policy { Paid == true }
              IsOpen: policy { Paid == false }
              IsOverdue: policy { DueDate + 14 Days < Today }
              IsRecent: policy { DueDate > Today - 3 Months }
            }
            """),
    ];

    /// <summary>C# texts the reader reads and the writer prints, for rows the other corpora may not carry.</summary>
    public static IReadOnlyList<(string Name, string Text)> CSharpSources { get; } = [
        ("interface", "interface IShape { int Sides { get; } long Area(long scale); }"),
        ("generic-class-with-base", "class Box<T> : Base { }"),
        ("attributes", "[Obsolete] class Old { } [Category(\"slow\")] class Slow { }"),
        ("binary-operators", """
            var a = 1L + 2L - 3L * 4L / 5L % 6L;
            var b = 1L == 2L != false;
            var c = 1L < 2L && 2L <= 3L || 3L > 4L && 4L >= 5L;
            var d = 6L & 3L | 1L ^ 2L;
            var e = 1L << 3 >> 1;
            """),
        ("coalesce", "string s = null; var t = s ?? \"x\";"),
        ("accessors", """
            class Accessors {
                long _a;
                long A { get; set => _a = value; }
                long B { get => 1; set; }
                long C { get => 1; private set => _a = value; }
                long D { get => 1; private set; }
            }
            """),
        ("generic-method", "class G { T Id<T>(T x) { return x; } }"),
        ("records", "record R<T>(T Value) : Base { long X() { return 1; } } record S<T>(T V) : Base;"),
        ("generic-interface-with-base", "interface IBox<T> : IShape { }"),
        ("async-ident-lambda", "Func<Task, Task> f = async t => await t;"),
    ];

    /// <summary>Code-built types the reader cannot read: an empty enum (#849) and a body-less method.</summary>
    public static IReadOnlyList<(string Name, TypeDefinitionNode Type)> CSharpTypes { get; } = [
        ("empty-enum", new TypeDefinitionNode("Empty", Kind: TypeKind.Enum)),
        ("body-less-method", new TypeDefinitionNode("Shell", Methods: [
            new MethodDefinitionNode("Declared", TypeReference.To<long>()),
        ])),
    ];

    /// <summary>Every observation of the drive, in print order. Computed once.</summary>
    public static IReadOnlyList<PrintObservation> Observations => Driven.Value.Observations;

    /// <summary>Inputs the writer or parser refused, in drive order. <see cref="ExpectedRefusals"/> pins them.</summary>
    public static IReadOnlyList<PatternRowRefusal> Refusals => Driven.Value.Refusals;

    /// <summary>
    /// The refusals the drive expects, each with its owner. The protocol test fails on a refusal that is not
    /// listed (a writer that starts refusing more inputs would shrink coverage silently) and on a listed one
    /// that no longer occurs (stale).
    /// </summary>
    public static IReadOnlyList<PatternRowExpectedRefusal> ExpectedRefusals { get; } = [
        new("unit Poly.Tests/Interpretation/ShadowedVariableVmTests.cs: var s = \"hi\"; var n = ((long s) => s + 1)(4); n;",
            "CSharpWriter rejected Lambda (NodeId …): a lambda used as a callee needs a delegate type to cast to, and its result type did not resolve.",
            "G-23b"),
        new("unit Poly.Tests/Interpretation/ShadowedVariableVmTests.cs: var s = 1000; var n = ((long s) => ((long s) => s + 1)(5) + …",
            "CSharpWriter rejected Lambda (NodeId …): a lambda used as a callee needs a delegate type to cast to, and its result type did not resolve.",
            "G-23b"),
    ];

    private static readonly Lazy<(IReadOnlyList<PrintObservation> Observations, IReadOnlyList<PatternRowRefusal> Refusals)> Driven =
        new(Drive, LazyThreadSafetyMode.ExecutionAndPublication);

    private static (IReadOnlyList<PrintObservation>, IReadOnlyList<PatternRowRefusal>) Drive() {
        // Load the static corpora before the scope begins, so the observation set does not depend on
        // which test touched them first.
        var units = NodeFactCorpus.Units;
        var programs = AstParityTests.Corpus().ToList();
        var goldens = GoldenDomains.Compilable.Select(domain => (domain.Name, domain.Poly)).ToList();

        var refusals = new List<PatternRowRefusal>();
        using var scope = PrintTrace.Begin();

        foreach (var (name, poly) in goldens.Concat(DslDocuments))
            PrintDsl(name, poly);
        PrintPinnedUses();

        foreach (var (source, unit) in units)
            Refusing(refusals, $"unit {source}", () => CSharpLanguage.Write(unit));
        foreach (var program in programs)
            Refusing(refusals, $"parity {program.Label}", () => AstParity.Print(program.Program));
        foreach (var (name, text) in CSharpSources)
            Refusing(refusals, $"source {name}", () => CSharpLanguage.Write(CSharpLanguage.Read(text)));
        foreach (var (name, type) in CSharpTypes)
            Refusing(refusals, $"type {name}", () => CSharpLanguage.Write([type]));

        return (scope.Observations, refusals);
    }

    private static void Refusing(List<PatternRowRefusal> refusals, string label, Func<string> print) {
        try {
            print();
        }
        catch (InvalidOperationException ex) {
            refusals.Add(new(label, ex.Message));
        }
        catch (FormatException ex) {
            refusals.Add(new(label, ex.Message));
        }
    }

    /// <summary>Parses, analyses and prints one document, as the round-trip corpus does.</summary>
    private static void PrintDsl(string name, string poly) {
        var session = DomainSession.Create(DslSyntaxTree.Parse(poly).Uses, new SessionOptions(ExtensionCatalog.Core, ProductCatalog.ImplicitIds));
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(poly, session).Parse(), session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Drive document '{name}' does not analyse: {result.FailureSummary}");
        new DomainDslPrinter(session).Print(result.Root);
    }

    /// <summary>
    /// A pinned <c>uses</c> with a dotted numeric pin, so <c>uses/pinned</c> is observed and its
    /// read-back asymmetry fires deterministically. The pin needs a seeded contract, so the domain is
    /// built in code.
    /// </summary>
    private static void PrintPinnedUses() {
        const string poly = """
            domain Shop

            Order: entity {
              Total: Number
            }
            """;
        var session = DomainSession.Create([], new SessionOptions(ExtensionCatalog.Core, ProductCatalog.ImplicitIds));
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(poly, session).Parse(), session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Pinned-uses document does not analyse: {result.FailureSummary}");
        var contract = new ImportedContract("Acme", ContractSourceKind.InternalDomain, "acme", "1.2.3", []) {
            UsesId = "acme",
            Pin = "1.2.3",
        };
        var pinned = result.Root with {
            Extensions = [.. result.Root.Extensions, "acme"],
            ImportedContracts = [contract],
        };
        new DomainDslPrinter(session).Print(pinned);
    }
}