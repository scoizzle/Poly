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
              Amount: Number
              Paid: Boolean
              DueDate: Date
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
    ];

    /// <summary>Code-built types the reader cannot read: an empty enum (#849).</summary>
    public static IReadOnlyList<(string Name, TypeDefinitionNode Type)> CSharpTypes { get; } = [
        ("empty-enum", new TypeDefinitionNode("Empty", Kind: TypeKind.Enum)),
    ];

    /// <summary>Every observation of the drive, in print order. Computed once.</summary>
    public static IReadOnlyList<PrintObservation> Observations => Driven.Value.Observations;

    /// <summary>Inputs the writer or parser refused (label and message), so a shrinking drive is visible.</summary>
    public static IReadOnlyList<string> Refusals => Driven.Value.Refusals;

    private static readonly Lazy<(IReadOnlyList<PrintObservation> Observations, IReadOnlyList<string> Refusals)> Driven =
        new(Drive, LazyThreadSafetyMode.ExecutionAndPublication);

    private static (IReadOnlyList<PrintObservation>, IReadOnlyList<string>) Drive() {
        // Load the static corpora before the scope begins, so the observation set does not depend on
        // which test touched them first.
        var units = NodeFactCorpus.Units;
        var programs = AstParityTests.Corpus().ToList();
        var goldens = GoldenDomains.Compilable.Select(domain => (domain.Name, domain.Poly)).ToList();

        var refusals = new List<string>();
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

    private static void Refusing(List<string> refusals, string label, Func<string> print) {
        try {
            print();
        }
        catch (InvalidOperationException ex) {
            refusals.Add($"{label}: {ex.Message}");
        }
        catch (FormatException ex) {
            refusals.Add($"{label}: {ex.Message}");
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