using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Parsing;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// Tests for <see cref="InfrastructureAnalyzer"/> and subsystem models —
/// root/child detection, key analysis, property classification, parent
/// resolution, behavior metadata, topology, and AnalysisResult-backed path.
/// </summary>
public class InfrastructureAnalyzerTests {
    // ── Helpers ───────────────────────────────────────────────

    private static (Domain Domain, AnalysisResult Analysis) ParseDomainWithAnalysis(string poly) {
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
        return (result.Root!, result.Analysis);
    }

    private static Domain ParseDomain(string poly) => ParseDomainWithAnalysis(poly).Domain;

    private static InfrastructureModel AnalyzeFull(string poly) {
        var domain = ParseDomain(poly);
        return new InfrastructureAnalyzer(domain).Analyze();
    }

    private static InfrastructureModel AnalyzeWithAnalysis(string poly) {
        var (domain, analysis) = ParseDomainWithAnalysis(poly);
        // Prefer full DomainModelAnalyzer so EntityStructure + capability metadata are present.
        var full = DomainModelAnalyzer.Analyze(domain);
        return new InfrastructureAnalyzer(domain, full).Analyze();
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
        await Assert.That(infra.Aggregate.Entities[0].IsRoot).IsTrue();
        await Assert.That(infra.Transport.Entities[0].IsExposable).IsTrue();
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

        var loan = aggregate.Entities.First(e => e.Name == "Loan");
        await Assert.That(loan.IsRoot).IsFalse();
        await Assert.That(loan.AggregateParentName).IsEqualTo("Patron");
        await Assert.That(loan.BackReferencePropertyName).IsEqualTo("borrower");
        await Assert.That(loan.ParentRelationshipName).IsEqualTo("loans");

        var storageLoan = infra.Storage.Entities.First(e => e.Name == "Loan");
        await Assert.That(storageLoan.IsRoot).IsFalse();
        await Assert.That(storageLoan.AggregateParentName).IsEqualTo("Patron");
        await Assert.That(storageLoan.ForeignKeys).Count().IsEqualTo(1);
        await Assert.That(storageLoan.ForeignKeys[0].ParentEntityName).IsEqualTo("Patron");
        await Assert.That(storageLoan.ForeignKeys[0].ParentKeyProperty).IsEqualTo("Email");
        await Assert.That(storageLoan.ForeignKeys[0].ChildPropertyName).IsEqualTo("BorrowerId");

        var transportLoan = infra.Transport.Entities.First(e => e.Name == "Loan");
        await Assert.That(transportLoan.ParentName).IsEqualTo("Patron");
        await Assert.That(transportLoan.IsExposable).IsFalse();
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

        await Assert.That(patron.Columns).Count().IsEqualTo(1);
        await Assert.That(patron.CollectionNavigations).Count().IsEqualTo(1);

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

    [Test]
    public async Task UniqueNumberKey_UsesLongClrType() {
        var infra = AnalyzeWithAnalysis("""
            domain Test
            Ticket: entity {
              Title: Text
              TicketNumber: Number unique
            }
            """);
        var ticket = infra.Storage.Entities[0];
        await Assert.That(ticket.KeyName).IsEqualTo("ticketNumber");
        await Assert.That(ticket.KeyClrType).IsEqualTo("long");
        await Assert.That(ticket.HasShadowKey).IsFalse();
    }

    // ── Behavior ──────────────────────────────────────────────

    [Test]
    public async Task Behavior_CapturesActionParametersAndVoid() {
        var infra = AnalyzeWithAnalysis("""
            domain Test
            Book: entity {
              Title: Text
              ISBN: Text unique

              Checkout(days: Number): action {
              }
            }
            """);
        var book = infra.Behavior.Entities.First(e => e.Name == "Book");
        await Assert.That(book.Actions).Count().IsEqualTo(1);
        var action = book.Actions[0];
        await Assert.That(action.Name).IsEqualTo("Checkout");
        await Assert.That(action.IsVoid).IsTrue();
        await Assert.That(action.Parameters).Count().IsEqualTo(1);
        await Assert.That(action.Parameters[0].Name).IsEqualTo("days");
        await Assert.That(action.Parameters[0].DomainType).IsEqualTo("Number");
        await Assert.That(action.Parameters[0].IsEntityRef).IsFalse();
    }

    [Test]
    public async Task Behavior_CapturesStageTransitions_FromEffects() {
        var infra = AnalyzeFull("""
            domain Test
            Patron: entity {
              Name: Text

              Active: stage {
                Suspend: action {
                  transition to Suspended
                }
              }
              Suspended: stage {
              }
            }
            """);
        var patron = infra.Behavior.Entities.First(e => e.Name == "Patron");
        var suspend = patron.Actions.First(a => a.Name == "Suspend");
        await Assert.That(suspend.StageName).IsEqualTo("Active");
        await Assert.That(suspend.StageTransitions).Count().IsEqualTo(1);
        await Assert.That(suspend.StageTransitions[0].TargetStageName).IsEqualTo("Suspended");
    }

    // ── Topology ──────────────────────────────────────────────

    [Test]
    public async Task Topology_DetectsCreateInAndSubscriptions() {
        var infra = AnalyzeFull("""
            domain Test
            Patron: entity {
              Name: Text
              Email: Text unique
              loans: many Loan

              Checkout(book: Book): action {
                create in loans {
                  DueDate: 0
                }
              }

              when loans Returned {
              }
            }
            Book: entity {
              Title: Text
              ISBN: Text unique
            }
            Loan: entity {
              DueDate: Number
              borrower: Patron

              Active: stage {
                Return: action {
                  transition to Returned
                }
              }
              Returned: stage {
              }
            }
            """);

        await Assert.That(infra.Topology.CreateInRelations).Count().IsGreaterThanOrEqualTo(1);
        var createIn = infra.Topology.CreateInRelations.First();
        await Assert.That(createIn.CreatorEntity).IsEqualTo("Patron");
        await Assert.That(createIn.CreatedEntity).IsEqualTo("Loan");
        await Assert.That(createIn.RelationshipName).IsEqualTo("loans");

        await Assert.That(infra.Topology.Subscriptions).Count().IsGreaterThanOrEqualTo(1);
        var sub = infra.Topology.Subscriptions.First();
        await Assert.That(sub.SubscriberEntity).IsEqualTo("Patron");
        await Assert.That(sub.RelationshipName).IsEqualTo("loans");
        await Assert.That(sub.TargetStage).IsEqualTo("Returned");

        // Topology is also available via Transport for consumers of that view only.
        await Assert.That(infra.Transport.Effects.CreateInRelations.Count)
            .IsEqualTo(infra.Topology.CreateInRelations.Count);

        var loan = infra.Aggregate.Entities.First(e => e.Name == "Loan");
        await Assert.That(loan.AggregateParentName).IsEqualTo("Patron");
        await Assert.That(loan.ParentRelationshipName).IsEqualTo("loans");

        var patronStore = infra.Storage.Entities.First(e => e.Name == "Patron");
        await Assert.That(patronStore.SubscriptionLists).Count().IsGreaterThanOrEqualTo(1);
    }

    // ── Analysis-backed path ──────────────────────────────────

    [Test]
    public async Task AnalysisPath_MatchesFallback_ForRootAndKeys() {
        const string poly = """
            domain Test
            Book: entity {
              Title: Text required
              ISBN: Text unique
            }
            Patron: entity {
              Name: Text
              Email: Text unique
              loans: many Loan
            }
            Loan: entity {
              Status: Text
              borrower: Patron
            }
            """;

        var fallback = AnalyzeFull(poly);
        var withAnalysis = AnalyzeWithAnalysis(poly);

        foreach (var name in new[] { "Book", "Patron", "Loan" }) {
            var a = fallback.Aggregate.Entities.First(e => e.Name == name);
            var b = withAnalysis.Aggregate.Entities.First(e => e.Name == name);
            await Assert.That(b.IsRoot).IsEqualTo(a.IsRoot);
            await Assert.That(b.AggregateParentName).IsEqualTo(a.AggregateParentName);

            var sa = fallback.Storage.Entities.First(e => e.Name == name);
            var sb = withAnalysis.Storage.Entities.First(e => e.Name == name);
            await Assert.That(sb.IsRoot).IsEqualTo(sa.IsRoot);
            await Assert.That(sb.KeyName).IsEqualTo(sa.KeyName);
            await Assert.That(sb.KeyClrType).IsEqualTo(sa.KeyClrType);
        }
    }

    [Test]
    public async Task Topology_ScannedOnce_SameInstanceOnModelAndTransport() {
        var infra = AnalyzeFull("""
            domain Test
            Book: entity { Title: Text }
            """);
        // Same scan result is shared — Transport reuses coordinator topology.
        await Assert.That(ReferenceEquals(infra.Topology, infra.Transport.Effects)).IsTrue();
    }
}