using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Parsing;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// Structural tests for <see cref="DomainToCSharpExporter"/>.
/// Asserts the shape of produced TypeDefinitionNode trees, not rendered string output.
/// Survives formatting changes and C# idiom refactoring.
/// </summary>
public class DomainToCSharpExporterTests {
    private const string LibraryCheckoutDsl = """
        domain Library

        Genre: enum { Fiction, NonFiction, Reference }
        PatronStatus: enum { Active, Suspended, Closed }
        FineStatus: enum { Unpaid, Resolved }
        PremiumTier: enum { Silver, Gold, Platinum }

        Book: entity {
          Title: Text required
          Author: Text required
          ISBN: Text unique length(10, 17)
          Pages: Number range(1, 10000)
          Genre: Genre
        }

        Patron: entity {
          Name: Text required
          Email: Text unique pattern("^[^@]+@[^@]+$")
          MemberSince: Date default(today)
          Status: PatronStatus default(Active)
          MaxItems: Number range(0, 20) required
          CurrentBorrowCount: Number
          OutstandingFines: Number
          loans: many Loan
          fines: many Fine
          GoodStanding: policy { Status is "Active" }
          AtLimit: policy { CurrentBorrowCount >= MaxItems }
          HasFines: policy { OutstandingFines > 0 }
          HasOverdueLoans: policy { any loans where Status is "Overdue" }
          AccountInGoodStanding: policy { Status is "Active" and OutstandingFines == 0 }
          Active: stage {
            CheckOut: action (book: Book) -> Loan
              require GoodStanding
              require not AtLimit
              require not HasFines
            {
              assign CurrentBorrowCount to CurrentBorrowCount + 1
              create in loans { book: book }
            }
            Suspend: action {
              assign Status to "Suspended"
              assign CurrentBorrowCount to 0
              transition to Suspended
            }
            CloseAccount: action { transition to Closed }
          }
          when loans Overdue {
            create Fine { Amount: 5 Reason: "Overdue item" }
            assign OutstandingFines to OutstandingFines + 5
          }
          when loans Returned {
            assign CurrentBorrowCount to CurrentBorrowCount - 1
          }
          when fines Paid {
            assign OutstandingFines to OutstandingFines - 5
          }
          Suspended: stage {
            entry { assign MaxItems to 0 }
            exit  { assign MaxItems to 5 }
            Reinstate: action require not HasOverdueLoans {
              assign Status to "Active"
              transition to Active
            }
          }
          Closed: stage { }
        }

        Loan: entity {
          Status: Text
          CheckedOutAt: DateTime
          DueDate: DateTime
          ReturnedAt: DateTime
          TimesRenewed: Number
          book: Book
          borrower: Patron
          Active: stage {
            entry { assign CheckedOutAt to now }
            Renew: action {
              assign DueDate to DueDate + 14
              assign TimesRenewed to TimesRenewed + 1
            }
            Return: action {
              assign ReturnedAt to now
              transition to Returned
            }
          }
          Overdue: stage {
            entry { assign CheckedOutAt to now }
          }
          Returned: stage { }
        }

        Fine: entity {
          Amount: Number required
          Reason: Text
          DateIssued: DateTime default(now)
          Paid: Boolean
          patron: Patron
          Unpaid: stage {
            Pay: action {
              if (Amount <= 0) {
                assign Paid to true
                delete
              }
              else { assign Paid to true }
              transition to Resolved
            }
            Waive: action {
              assign Amount to 0
              assign Paid to true
              transition to Resolved
            }
          }
          Resolved: stage {
            entry { assign Paid to true }
          }
        }

        PremiumPatron: entity {
          Name: Text required
          Email: Text unique pattern("^[^@]+@[^@]+$")
          RewardPoints: Number
          Tier: PremiumTier default(Silver)
          PriorityAccess: Boolean
          IsLoyal: policy { RewardPoints >= 100 }
          HasPriority: policy { PriorityAccess is true }
          UnlimitedItems: policy { Tier is "Platinum" or PriorityAccess is true }
        }
        """;

    private static (Domain Domain, AnalysisResult Analysis) ParseAndAnalyze(string poly) {
        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var result = new DomainEvolution(new Domain("_", [], [])).Apply(changes);
        if (!result.Succeeded) {
            var errors = string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message));
            throw new InvalidOperationException($"Evolution failed: {errors}");
        }
        var analysis = DomainModelAnalyzer.Analyze(result.Root!);
        return (result.Root!, analysis);
    }

    [Test]
    public async Task Export_Produces_CoreInfrastructureTypes() {
        var (domain, analysis) = ParseAndAnalyze(LibraryCheckoutDsl);
        var exporter = new DomainToCSharpExporter();

        var types = exporter.Export(domain, analysis);

        await Assert.That(types.Any(t => t.Name == "DomainResult")).IsTrue();
        await Assert.That(types.Any(t => t.Name is "DomainResult" && t.GenericParameters?.Count > 0)).IsTrue();
    }

    [Test]
    public async Task Export_Produces_EnumTypes() {
        var (domain, analysis) = ParseAndAnalyze(LibraryCheckoutDsl);
        var exporter = new DomainToCSharpExporter();

        var types = exporter.Export(domain, analysis);

        await Assert.That(types.Any(t => t.Name == "Genre")).IsTrue();
        await Assert.That(types.Any(t => t.Name == "PatronStatus")).IsTrue();
        await Assert.That(types.Any(t => t.Name == "FineStatus")).IsTrue();
        await Assert.That(types.Any(t => t.Name == "PremiumTier")).IsTrue();
    }

    [Test]
    public async Task Export_Produces_EntityTypes() {
        var (domain, analysis) = ParseAndAnalyze(LibraryCheckoutDsl);
        var exporter = new DomainToCSharpExporter();

        var types = exporter.Export(domain, analysis);

        var book = types.FirstOrDefault(t => t.Name == "Book");
        await Assert.That(book).IsNotNull();
        await Assert.That(book!.EffectiveSemantics).IsEqualTo(TypeDefinitionSemantics.MutableReference);

        await Assert.That(types.Any(t => t.Name == "Patron")).IsTrue();
        await Assert.That(types.Any(t => t.Name == "Loan")).IsTrue();
        await Assert.That(types.Any(t => t.Name == "Fine")).IsTrue();
        await Assert.That(types.Any(t => t.Name == "PremiumPatron")).IsTrue();
    }

    [Test]
    public async Task Export_BookEntity_HasExpectedProperties() {
        var (domain, analysis) = ParseAndAnalyze(LibraryCheckoutDsl);
        var exporter = new DomainToCSharpExporter();

        var types = exporter.Export(domain, analysis);
        var book = types.First(t => t.Name == "Book");
        var propNames = book.Properties?.Select(p => p.Name).ToArray() ?? [];

        await Assert.That(propNames.Contains("IsDeleted")).IsTrue();
        await Assert.That(propNames.Contains("Title")).IsTrue();
        await Assert.That(propNames.Contains("Author")).IsTrue();
        await Assert.That(propNames.Contains("ISBN")).IsTrue();
        await Assert.That(propNames.Contains("Pages")).IsTrue();
        await Assert.That(propNames.Contains("Genre")).IsTrue();
    }

    [Test]
    public async Task Export_PatronEntity_HasPoliciesAsMethods() {
        var (domain, analysis) = ParseAndAnalyze(LibraryCheckoutDsl);
        var exporter = new DomainToCSharpExporter();

        var types = exporter.Export(domain, analysis);
        var patron = types.First(t => t.Name == "Patron");
        var methodNames = patron.Methods?.Select(m => m.Name).ToArray() ?? [];

        await Assert.That(methodNames.Contains("GoodStanding")).IsTrue();
        await Assert.That(methodNames.Contains("AtLimit")).IsTrue();
        await Assert.That(methodNames.Contains("HasFines")).IsTrue();
        await Assert.That(methodNames.Contains("AccountInGoodStanding")).IsTrue();
    }

    [Test]
    public async Task Export_PatronEntity_HasNavigationBackingFields() {
        var (domain, analysis) = ParseAndAnalyze(LibraryCheckoutDsl);
        var exporter = new DomainToCSharpExporter();

        var types = exporter.Export(domain, analysis);
        var patron = types.First(t => t.Name == "Patron");
        var fieldNames = patron.Fields?.Select(f => f.Name).ToArray() ?? [];

        await Assert.That(fieldNames.Contains("_loans")).IsTrue();
        await Assert.That(fieldNames.Contains("_fines")).IsTrue();
    }

    [Test]
    public async Task Export_PatronEntity_HasStageEnum() {
        var (domain, analysis) = ParseAndAnalyze(LibraryCheckoutDsl);
        var exporter = new DomainToCSharpExporter();

        var types = exporter.Export(domain, analysis);

        await Assert.That(types.Any(t => t.Name == "PatronStage")).IsTrue();
    }
}