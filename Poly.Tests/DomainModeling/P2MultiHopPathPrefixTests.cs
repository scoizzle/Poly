using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Parsing;

namespace Poly.Tests.DomainModeling;

/// <summary>
/// P2: multi-hop to-one path-prefix (loan book Title) via store-linked EvaluatePolicy.
/// </summary>
public class P2MultiHopPathPrefixTests {
    private static (Domain Domain, AnalysisResult Analysis) Evolve(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(new Domain("_", [], [])).Apply(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        var analysis = DomainModelAnalyzer.Analyze(result.Root!);
        if (analysis.HasErrors)
            throw new InvalidOperationException(string.Join("; ",
                analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        return (result.Root!, analysis);
    }

    private const string MultiHopDomain = """
        domain MultiHop
        Patron: entity {
          Name: Text
          loan: Loan
          HasClassic: policy { loan book Title is "Classic" }
          Active: stage {}
        }
        Loan: entity {
          Code: Text
          book: Book
          Open: stage {}
        }
        Book: entity {
          Title: Text
          Catalog: stage {}
        }
        """;

    [Test]
    public async Task EvaluatePolicy_TwoHop_ToOne_MatchesTitle() {
        var (domain, _) = Evolve(MultiHopDomain);
        var store = new DomainInstanceStore();
        var patronE = domain.Types.OfType<Entity>().Single(e => e.Name == "Patron");
        var loanE = domain.Types.OfType<Entity>().Single(e => e.Name == "Loan");
        var bookE = domain.Types.OfType<Entity>().Single(e => e.Name == "Book");

        var patron = DomainEntityInstance.Create(patronE,
            new Dictionary<string, object?> { ["Name"] = "P" }, domain);
        var loan = DomainEntityInstance.Create(loanE,
            new Dictionary<string, object?> { ["Code"] = "L1" }, domain);
        var book = DomainEntityInstance.Create(bookE,
            new Dictionary<string, object?> { ["Title"] = "Classic" }, domain);
        store.Add(patron);
        store.Add(loan);
        store.Add(book);
        store.Link("loan", patron, loan);
        store.Link("book", loan, book);

        var policy = patronE.Policies.Single(p => p.Name == "HasClassic");
        await Assert.That(patron.EvaluatePolicy(policy)).IsTrue();

        book.SetProperty("Title", "Other");
        await Assert.That(patron.EvaluatePolicy(policy)).IsFalse();
    }

    [Test]
    public async Task Analyze_ManyInMiddle_BarePathPrefix_ReportsError() {
        var changes = new PolyDslParser("""
            domain BadHop
            Patron: entity {
              loans: many Loan
              Bad: policy { loans book Title is "X" }
            }
            Loan: entity {
              book: Book
            }
            Book: entity {
              Title: Text
            }
            """).Parse();
        var result = new DomainEvolution(new Domain("_", [], [])).Apply(changes);
        // Evolution may reject; also re-analyze if we can build via factory.
        AnalysisResult analysis;
        if (result.Succeeded)
            analysis = DomainModelAnalyzer.Analyze(result.Root!);
        else
            analysis = result.Analysis;

        var errors = analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        await Assert.That(errors.Any(d =>
            d.Message.Contains("many", StringComparison.OrdinalIgnoreCase)
            || d.Message.Contains("OneToMany", StringComparison.Ordinal)
            || d.Message.Contains("quantifier", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }
}