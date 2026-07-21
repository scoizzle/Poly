using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Parsing;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// Tests for <see cref="InfrastructureAnalyzer"/> — root/child detection, key analysis,
/// property classification, parent resolution, and action metadata.
///
/// Note: Endpoint/routing tests belong in the codegen backend tests, not here.
/// The infrastructure model is transport-agnostic and does not prescribe HTTP verbs,
/// route templates, or any specific protocol.
/// </summary>
public class InfrastructureAnalyzerTests {
    // ── Helpers ───────────────────────────────────────────────

    private static Domain ParseDomain(string poly) {
        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = new Domain("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        if (!result.Succeeded) {
            var errors = string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message));
            throw new InvalidOperationException($"Domain evolution failed: {errors}");
        }
        return result.Root!;
    }

    private static InfrastructureModel AnalyzeFull(string poly) {
        var domain = ParseDomain(poly);
        return new InfrastructureAnalyzer(domain).Analyze();
    }

    // ── Root/child detection ──────────────────────────────────

    [Test]
    public async Task EntityWithoutEntityRefs_IsRoot() {
        var infra = AnalyzeFull("""
            domain Test
            Book: entity {
              Title: Text required
              ISBN: Text unique
            }
            """);
        var model = infra.Storage;

        await Assert.That(model.Entities).Count().IsEqualTo(1);
        await Assert.That(model.Entities[0].IsRoot).IsTrue();
        await Assert.That(model.Entities[0].KeyName).IsEqualTo("isbn");
        await Assert.That(model.Entities[0].KeyClrType).IsEqualTo("string");
    }

    [Test]
    public async Task EntityWithEntityRefs_IsNotRoot() {
        var infra = AnalyzeFull("""
            domain Test
            Book: entity { Title: Text }
            Patron: entity { Name: Text }
            Loan: entity {
              book: Book
              borrower: Patron
            }
            """);
        var model = infra.Storage;

        var loan = model.Entities.First(e => e.Name == "Loan");
        await Assert.That(loan.IsRoot).IsFalse();
        await Assert.That(loan.KeyName).IsEqualTo("id");
        await Assert.That(loan.KeyClrType).IsEqualTo("int");
    }

    [Test]
    public async Task ChildEntity_HasAggregateParent() {
        var infra = AnalyzeFull("""
            domain Test
            Patron: entity {
              Name: Text
              Email: Text unique
              loans: many Loan
            }
            Loan: entity {
              Status: Text
              borrower: Patron
            }
            """);
        var aggregate = infra.Aggregate;

        // Aggregate model has the parent/backref relationship
        var loan = aggregate.Entities.First(e => e.Name == "Loan");
        await Assert.That(loan.IsRoot).IsFalse();
        await Assert.That(loan.AggregateParentName).IsEqualTo("Patron");
        await Assert.That(loan.BackReferencePropertyName).IsEqualTo("borrower");

        // Storage model also has IsRoot and parent name
        var storageLoan = infra.Storage.Entities.First(e => e.Name == "Loan");
        await Assert.That(storageLoan.IsRoot).IsFalse();
        await Assert.That(storageLoan.AggregateParentName).IsEqualTo("Patron");
    }

    // ── Column classification ─────────────────────────────────

    [Test]
    public async Task EntityProperties_BecomeColumns() {
        var infra = AnalyzeFull("""
            domain Test
            Book: entity {
              Title: Text required
              Pages: Number range(1, 10000)
            }
            """);
        var model = infra.Storage;

        var book = model.Entities[0];
        await Assert.That(book.Columns).Count().IsEqualTo(2);

        var title = book.Columns[0];
        await Assert.That(title.Name).IsEqualTo("Title");
        await Assert.That(title.ClrTypeName).IsEqualTo("string");
        await Assert.That(title.IsRequired).IsTrue();

        var pages = book.Columns[1];
        await Assert.That(pages.Name).IsEqualTo("Pages");
        await Assert.That(pages.ClrTypeName).IsEqualTo("long");
    }

    [Test]
    public async Task NavigationProperties_NotInColumns() {
        var infra = AnalyzeFull("""
            domain Test
            Patron: entity {
              Name: Text
              loans: many Loan
            }
            Loan: entity {
              Status: Text
              borrower: Patron
            }
            """);
        var patron = infra.Storage.Entities.First(e => e.Name == "Patron");
        var loan = infra.Storage.Entities.First(e => e.Name == "Loan");

        // Patron.loans is a collection nav, not a column
        await Assert.That(patron.Columns).Count().IsEqualTo(1);
        await Assert.That(patron.CollectionNavigations).Count().IsEqualTo(1);

        // Loan.borrower is a reference nav, not a column
        await Assert.That(loan.Columns).Count().IsEqualTo(1);
        await Assert.That(loan.ReferenceNavigations).Count().IsEqualTo(1);
    }

    // ── Edge cases ────────────────────────────────────────────

    [Test]
    public async Task EntityWithoutUnique_UsesShadowKey() {
        var infra = AnalyzeFull("""
            domain Test
            Loan: entity {
              Status: Text
            }
            """);
        var loan = infra.Storage.Entities[0];
        await Assert.That(loan.KeyName).IsEqualTo("id");
        await Assert.That(loan.KeyClrType).IsEqualTo("int");
        await Assert.That(loan.KeyProperty).IsNull();
    }

    [Test]
    public async Task EntityWithOwnedNavigation_HasCollectionNav() {
        var infra = AnalyzeFull("""
            domain Test
            Order: entity {
              Title: Text
              items: many LineItem
            }
            LineItem: entity {
              Product: Text
              Quantity: Number
            }
            """);
        var order = infra.Storage.Entities.First(e => e.Name == "Order");
        await Assert.That(order.CollectionNavigations).Count().IsEqualTo(1);
        var nav = order.CollectionNavigations[0];
        await Assert.That(nav.TargetEntityName).IsEqualTo("LineItem");
        await Assert.That(nav.IsCollection).IsTrue();
    }
}