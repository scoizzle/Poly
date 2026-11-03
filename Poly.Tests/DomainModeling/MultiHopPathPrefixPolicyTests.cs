using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling;

/// <summary>
/// P2: multi-hop to-one path-prefix (loan book Title) via store-linked EvaluatePolicy.
/// </summary>
public class MultiHopPathPrefixPolicyTests {
    private static (Domain Domain, AnalysisResult Analysis) Evolve(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
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
        var exec = ModuleExec.For(domain);
        var patronE = domain.Types.OfType<Entity>().Single(e => e.Name == "Patron");
        var loanE = domain.Types.OfType<Entity>().Single(e => e.Name == "Loan");
        var bookE = domain.Types.OfType<Entity>().Single(e => e.Name == "Book");

        var patron = exec.Create((patronE).Name, new Dictionary<string, object?> { ["Name"] = "P" }).Instance!;
        var loan = exec.Create((loanE).Name, new Dictionary<string, object?> { ["Code"] = "L1" }).Instance!;
        var book = exec.Create((bookE).Name, new Dictionary<string, object?> { ["Title"] = "Classic" }).Instance!;
        exec.Link("loan", patron, loan);
        exec.Link("book", loan, book);

        var policy = patronE.Policies.Single(p => p.Name == "HasClassic");
        await Assert.That(exec.EvaluatePolicy(patron, (policy).Name)).IsTrue();

        book.Bag["Title"] = "Other";
        await Assert.That(exec.EvaluatePolicy(patron, (policy).Name)).IsFalse();
    }

    [Test]
    public async Task InvokeAction_RequireNot_PathPrefixComparison_UsesBooleanNotInt64() {
        var (domain, _) = Evolve("""
            domain PathNot
            Section: entity {
              SeatsTaken: Number default(0)
              Open: stage {}
            }
            Enrollment: entity {
              section: Section
              SectionFull: policy { section SeatsTaken >= 1 }
              Pending: stage {
                Confirm: action require not SectionFull { transition to Registered }
              }
              Registered: stage {}
            }
            """);
        var exec = ModuleExec.For(domain);
        var sectionE = domain.Types.OfType<Entity>().Single(e => e.Name == "Section");
        var enrollE = domain.Types.OfType<Entity>().Single(e => e.Name == "Enrollment");
        var section = exec.Create((sectionE).Name, new Dictionary<string, object?>()).Instance!;
        var enrollment = exec.Create((enrollE).Name, new Dictionary<string, object?>()).Instance!;
        exec.Link("section", enrollment, section);

        var ok = exec.Invoke(enrollment, "Confirm");
        await Assert.That(ok.Succeeded).IsTrue();

        var full = exec.Create((enrollE).Name, new Dictionary<string, object?>()).Instance!;
        exec.Link("section", full, section);
        section.Bag["SeatsTaken"] = 1L;
        var blocked = exec.Invoke(full, "Confirm");
        await Assert.That(blocked.Succeeded).IsFalse();
        await Assert.That(blocked.FailedGuards).Contains("not_SectionFull");
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
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
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