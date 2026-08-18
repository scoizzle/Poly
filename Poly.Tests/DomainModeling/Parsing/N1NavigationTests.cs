using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
// PolyDslParser

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// Tests for N1 navigation property parsing: "orders: many Order" inside entity blocks.
/// </summary>
public class N1NavigationTests {
    [Test]
    public async Task Parse_ManyNav_CreatesOneToManyRelationship() {
        var poly = """
            domain Test

            Customer: entity {
              Name: Text
            }

            Order: entity {
              Title: Text
              customer: many Customer
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        await Assert.That(result.Relationships().Count).IsEqualTo(1);
        var rel = result.Relationships()[0];
        await Assert.That(rel.Name).IsEqualTo("customer");
        await Assert.That(rel.Source.TypeName).IsEqualTo("Order");
        await Assert.That(rel.Target.TypeName).IsEqualTo("Customer");
        await Assert.That(rel.Cardinality).IsEqualTo(RelationshipCardinality.OneToMany);
        await Assert.That(rel.SourceOwnsTarget).IsFalse();
    }

    [Test]
    public async Task Parse_OneNav_CreatesOneToOneRelationship() {
        var poly = """
            domain Test

            Employee: entity {
              Name: Text
            }

            Company: entity {
              Name: Text
              ceo: Employee
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        await Assert.That(result.Relationships().Count).IsEqualTo(1);
        var rel = result.Relationships()[0];
        await Assert.That(rel.Name).IsEqualTo("ceo");
        await Assert.That(rel.Source.TypeName).IsEqualTo("Company");
        await Assert.That(rel.Target.TypeName).IsEqualTo("Employee");
        await Assert.That(rel.Cardinality).IsEqualTo(RelationshipCardinality.OneToOne);
        await Assert.That(rel.SourceOwnsTarget).IsFalse();
    }

    [Test]
    public async Task Parse_ManyOwnedNav_CreatesOwnedOneToMany() {
        var poly = """
            domain Test

            Customer: entity {
              Name: Text
            }

            Order: entity {
              Title: Text
              lineItems: many owned LineItem
            }

            LineItem: entity {
              Product: Text
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        await Assert.That(result.Relationships().Count).IsEqualTo(1);
        var rel = result.Relationships()[0];
        await Assert.That(rel.Name).IsEqualTo("lineItems");
        await Assert.That(rel.Source.TypeName).IsEqualTo("Order");
        await Assert.That(rel.Target.TypeName).IsEqualTo("LineItem");
        await Assert.That(rel.Cardinality).IsEqualTo(RelationshipCardinality.OneToMany);
        await Assert.That(rel.SourceOwnsTarget).IsTrue();
    }

    [Test]
    public async Task Parse_OwnedNav_CreatesOwnedOneToOne() {
        var poly = """
            domain Test

            Passport: entity {
              PassNum: Text required unique
            }

            Person: entity {
              Name: Text
              passport: owned Passport
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        await Assert.That(result.Relationships().Count).IsEqualTo(1);
        var rel = result.Relationships()[0];
        await Assert.That(rel.Name).IsEqualTo("passport");
        await Assert.That(rel.Source.TypeName).IsEqualTo("Person");
        await Assert.That(rel.Cardinality).IsEqualTo(RelationshipCardinality.OneToOne);
        await Assert.That(rel.SourceOwnsTarget).IsTrue();
    }

    [Test]
    public async Task Parse_OneOwnedNav_CreatesOneToOneOwned() {
        var poly = """
            domain Test

            Profile: entity {
              Bio: Text
            }

            User: entity {
              Name: Text
              profile: one owned Profile
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        await Assert.That(result.Relationships().Count).IsEqualTo(1);
        var rel = result.Relationships()[0];
        await Assert.That(rel.Name).IsEqualTo("profile");
        await Assert.That(rel.Cardinality).IsEqualTo(RelationshipCardinality.OneToOne);
        await Assert.That(rel.SourceOwnsTarget).IsTrue();
    }

    [Test]
    public async Task Parse_NavThenProperty_DisambiguatesCorrectly() {
        var poly = """
            domain Test

            Customer: entity {
              Name: Text
            }

            Order: entity {
              Title: Text
              customer: many Customer
              count: Number
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var order = result.Root!.Types.OfType<Entity>().Single(e => e.Name == "Order");
        await Assert.That(order.Properties.Count).IsEqualTo(2);
        await Assert.That(order.Properties.Any(p => p.Name == "count")).IsTrue();
        await Assert.That(order.Properties.Any(p => p.Name == "Title")).IsTrue();
        await Assert.That(result.Relationships().Count).IsEqualTo(1);
    }

    [Test]
    public async Task Parse_UnknownTargetEntity_ThrowsError() {
        var poly = """
            domain Test

            Order: entity {
              customer: many UnknownEntity
            }
            """;

        var parser = new PolyDslParser(poly);
        var threw = false;
        try {
            parser.Parse();
        }
        catch (FormatException ex) {
            await Assert.That(ex.Message.Contains("references unknown entity")).IsTrue();
            threw = true;
        }
        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task Parse_PrimitiveTarget_ThrowsError() {
        var poly = """
            domain Test

            Order: entity {
              name: many Text
            }
            """;

        var parser = new PolyDslParser(poly);
        var threw = false;
        try {
            parser.Parse();
        }
        catch (FormatException ex) {
            await Assert.That(ex.Message.Contains("primitive type")).IsTrue();
            threw = true;
        }
        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task Parse_N2TopLevel_Rejected() {
        var poly = """
            domain Test

            Tracker: entity {
              Status: Text
            }

            Order: entity {
              Name: Text
            }

            relationship Tracks from Tracker to Order one
            """;

        var parser = new PolyDslParser(poly);
        var threw = false;
        try {
            parser.Parse();
        }
        catch (FormatException ex) {
            await Assert.That(ex.Message.Contains("navigation property")).IsTrue();
            threw = true;
        }
        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task Parse_N2InsideEntity_Rejected() {
        var poly = """
            domain Test

            Tracker: entity {
              Status: Text
              relationship Tracks from Tracker to Order one
            }

            Order: entity {
              Draft: stage {}
            }
            """;

        var parser = new PolyDslParser(poly);
        var threw = false;
        try {
            parser.Parse();
        }
        catch (FormatException ex) {
            await Assert.That(ex.Message.Contains("navigation property")).IsTrue();
            threw = true;
        }
        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task N1Nav_RoundTrips_StructurallyIdentical() {
        var poly = """
            domain Test

            Customer: entity {
              Name: Text
            }

            Order: entity {
              Title: Text
              customer: many Customer
              items: many owned LineItem
            }

            LineItem: entity {
              Product: Text
              price: Number
            }
            """;

        // First parse: N1 input
        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Relationships().Count).IsEqualTo(2);

        // First print: N1 output
        var printer = new DomainDslPrinter();
        var printed = printer.Print(result.Root);

        // Second parse: re-parse N1 output
        var parser2 = new PolyDslParser(printed);
        var changes2 = parser2.Parse();
        var emptyDomain2 = DomainTestFactory.Create("_", [], []);
        var result2 = new DomainEvolution(emptyDomain2).Apply(changes2);
        await Assert.That(result2.Succeeded).IsTrue();

        // Structural comparison
        await Assert.That(result2.Relationships().Count).IsEqualTo(result.Relationships().Count);
        await Assert.That(result2.Root.Types.OfType<Entity>().Count())
            .IsEqualTo(result.Root.Types.OfType<Entity>().Count());

        foreach (var rel in result2.Relationships()) {
            var orig = result.Relationships().FirstOrDefault(r => r.Name == rel.Name);
            await Assert.That(orig).IsNotNull();
            await Assert.That(rel.Source.TypeName).IsEqualTo(orig!.Source.TypeName);
            await Assert.That(rel.Target.TypeName).IsEqualTo(orig.Target.TypeName);
            await Assert.That(rel.Cardinality).IsEqualTo(orig.Cardinality);
            await Assert.That(rel.SourceOwnsTarget).IsEqualTo(orig.SourceOwnsTarget);
        }

        // Analysis should be clean
        var analysis = DomainModelAnalyzer.Analyze(result2.Root);
        await Assert.That(analysis.HasStructuralFailure).IsFalse();
    }

    [Test]
    public async Task MultipleNavs_MultipleEntities_AllResolve() {
        var poly = """
            domain HR

            Company: entity {
              Name: Text
              departments: many Department
              employees: many Employee
            }

            Department: entity {
              Name: Text
              manager: Employee
            }

            Employee: entity {
              Name: Text
              reviews: many PerformanceReview
            }

            PerformanceReview: entity {
              Score: Number
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        await Assert.That(result.Relationships().Count).IsEqualTo(4);

        // Company → Department (OneToMany)
        await Assert.That(result.Relationships().Any(r =>
            r.Name == "departments" &&
            r.Source.TypeName == "Company" &&
            r.Target.TypeName == "Department" &&
            r.Cardinality == RelationshipCardinality.OneToMany)).IsTrue();

        // Company → Employee (OneToMany)
        await Assert.That(result.Relationships().Any(r =>
            r.Name == "employees" &&
            r.Source.TypeName == "Company" &&
            r.Target.TypeName == "Employee" &&
            r.Cardinality == RelationshipCardinality.OneToMany)).IsTrue();

        // Department → Employee (OneToOne)
        await Assert.That(result.Relationships().Any(r =>
            r.Name == "manager" &&
            r.Source.TypeName == "Department" &&
            r.Target.TypeName == "Employee" &&
            r.Cardinality == RelationshipCardinality.OneToOne)).IsTrue();

        // Employee → PerformanceReview (OneToMany)
        await Assert.That(result.Relationships().Any(r =>
            r.Name == "reviews" &&
            r.Source.TypeName == "Employee" &&
            r.Target.TypeName == "PerformanceReview" &&
            r.Cardinality == RelationshipCardinality.OneToMany)).IsTrue();

        var analysis = DomainModelAnalyzer.Analyze(result.Root);
        await Assert.That(analysis.HasStructuralFailure).IsFalse();
    }

    [Test]
    public async Task Parse_PropertyNavCollision_ThrowsError() {
        // Nav name conflicts with an existing property on the same entity
        var poly = """
            domain Test

            Order: entity {
              Name: Text
              Name: many Customer
            }

            Customer: entity {}
            """;

        var parser = new PolyDslParser(poly);
        var threw = false;
        try {
            parser.Parse();
        }
        catch (FormatException ex) {
            await Assert.That(ex.Message.Contains("conflicts with an existing property")).IsTrue();
            threw = true;
        }
        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task Parse_SameNavNameOnDifferentEntities_Succeeds() {
        // Relationship identity is (source entity, name): the same nav name may be
        // declared on different source entities (e.g. back-references both named 'customer').
        var poly = """
            domain Test

            Order: entity {
              customer: many Customer
              primary: many Customer
            }

            Customer: entity {
              customer: many Order
            }
            """;

        var parser = new PolyDslParser(poly);
        var result = parser.Parse();
        await Assert.That(result.OfType<AddRelationshipChange>().Count()).IsEqualTo(3);
    }

    [Test]
    public async Task Parse_DuplicateNavNameOnSameEntity_ThrowsError() {
        var poly = """
            domain Test

            Order: entity {
              customer: many Customer
              customer: many Customer
            }

            Customer: entity { }
            """;

        var parser = new PolyDslParser(poly);
        var threw = false;
        try {
            parser.Parse();
        }
        catch (FormatException ex) {
            await Assert.That(ex.Message.Contains("defined more than once on entity 'Order'")).IsTrue();
            threw = true;
        }
        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task Parse_TwoN1Navs_Succeeds() {
        var poly = """
            domain Test

            Tracker: entity {
              Status: Text
              tracks: many Order
            }

            Order: entity {
              Name: Text
            }

            Auditor: entity {
              Name: Text
              Audits: Order
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        await Assert.That(result.Relationships().Count).IsEqualTo(2);
        await Assert.That(result.Relationships().Any(r => r.Name == "tracks")).IsTrue();
        await Assert.That(result.Relationships().Any(r => r.Name == "Audits")).IsTrue();

        var analysis = DomainModelAnalyzer.Analyze(result.Root);
        await Assert.That(analysis.HasStructuralFailure).IsFalse();
    }

    [Test]
    public async Task Parse_SelfReferentialNav_Allowed() {
        // Self-referential navigation (Friends-style) should be allowed
        var poly = """
            domain Test

            Person: entity {
              Name: Text
              friends: many Person
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        await Assert.That(result.Relationships().Count).IsEqualTo(1);
        var rel = result.Relationships()[0];
        await Assert.That(rel.Name).IsEqualTo("friends");
        await Assert.That(rel.Source.TypeName).IsEqualTo("Person");
        await Assert.That(rel.Target.TypeName).IsEqualTo("Person");
        await Assert.That(rel.Cardinality).IsEqualTo(RelationshipCardinality.OneToMany);
        await Assert.That(rel.SourceOwnsTarget).IsFalse();

        var analysis = DomainModelAnalyzer.Analyze(result.Root);
        await Assert.That(analysis.HasStructuralFailure).IsFalse();
    }

    [Test]
    public async Task N1NavWithSubscription_RoundTrips() {
        var poly = """
            domain Test

            Tracker: entity {
              Status: Text
              Tracks: Order

              Pending: stage {
                when Tracks Active {
                  assign Status to "Triggered"
                }
              }
            }

            Order: entity {
              Draft: stage {
                Activate: action {
                  transition to Active
                }
              }
              Active: stage {}
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Relationships().Count).IsEqualTo(1);

        var printer = new DomainDslPrinter();
        var printed = printer.Print(result.Root);

        await Assert.That(printed.Contains("relationship ")).IsFalse();
        await Assert.That(printed.Contains("Tracks: Order")).IsTrue();

        var parser2 = new PolyDslParser(printed);
        var changes2 = parser2.Parse();
        var emptyDomain2 = DomainTestFactory.Create("_", [], []);
        var result2 = new DomainEvolution(emptyDomain2).Apply(changes2);
        await Assert.That(result2.Succeeded).IsTrue();
        await Assert.That(result2.Relationships().Count).IsEqualTo(1);

        var tracker = result2.Root.Types.OfType<Entity>().Single(e => e.Name == "Tracker");
        var pending = tracker.Stages.Single(s => s.Name == "Pending");
        await Assert.That(pending.Subscriptions.Count).IsEqualTo(1);
        await Assert.That(pending.Subscriptions[0].RelationshipName).IsEqualTo("Tracks");

        var analysis = DomainModelAnalyzer.Analyze(result2.Root);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionContractMismatch)).IsFalse();
    }

    [Test]
    public async Task N1NavAuthored_RoundTrips_WithSubscription() {
        var poly = """
            domain Test

            Tracker: entity {
              Status: Text
              Pending: stage {
                when Tracks Active {
                  assign Status to "Triggered"
                }
              }
              Tracks: Order
            }

            Order: entity {
              Draft: stage {
                Activate: action {
                  transition to Active
                }
              }
              Active: stage {}
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Relationships().Count).IsEqualTo(1);
        var rel = result.Relationships()[0];
        await Assert.That(rel.Name).IsEqualTo("Tracks");
        await Assert.That(rel.Source.TypeName).IsEqualTo("Tracker");
        await Assert.That(rel.Target.TypeName).IsEqualTo("Order");

        var printer = new DomainDslPrinter();
        var printed = printer.Print(result.Root);

        var parser2 = new PolyDslParser(printed);
        var changes2 = parser2.Parse();
        var emptyDomain2 = DomainTestFactory.Create("_", [], []);
        var result2 = new DomainEvolution(emptyDomain2).Apply(changes2);
        await Assert.That(result2.Succeeded).IsTrue();
        await Assert.That(result2.Relationships().Count).IsEqualTo(1);
        await Assert.That(result2.Relationships()[0].Name).IsEqualTo("Tracks");

        var tracker = result2.Root.Types.OfType<Entity>().Single(e => e.Name == "Tracker");
        var pending = tracker.Stages.Single(s => s.Name == "Pending");
        await Assert.That(pending.Subscriptions.Count).IsEqualTo(1);
        await Assert.That(pending.Subscriptions[0].RelationshipName).IsEqualTo("Tracks");

        var analysis = DomainModelAnalyzer.Analyze(result2.Root);
        await Assert.That(analysis.HasStructuralFailure).IsFalse();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionContractMismatch)).IsFalse();
    }
}