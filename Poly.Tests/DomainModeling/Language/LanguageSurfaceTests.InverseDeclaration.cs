using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.Grammar;

namespace Poly.Tests.DomainModeling.Language;

/// <summary>
/// F-15 (Q10) — <c>via</c> names the inverse navigation, e.g.
/// <c>reports: many Employee via manager</c>. The clause is syntax and IR only:
/// pairing validity is S-11's analysis and dispatch is L's lowering.
/// </summary>
public sealed partial class LanguageSurfaceTests {
    private static GrammarException ParseFails(string dsl) =>
        Assert.Throws<GrammarException>(() => new PolyDslParser(dsl).Parse())!;

    private static Domain Apply(string dsl) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(dsl).Parse());
        if (!result.Succeeded || result.Root is null)
            throw new InvalidOperationException("the document did not apply");
        return result.Root;
    }

    private static Entity EntityNamed(Domain root, string name) =>
        root.Types.OfType<Entity>().Single(e => e.Name == name);

    private static Relationship Nav(Entity entity, string name) =>
        entity.Navigations.Single(n => n.Name == name);

    private const string DepartmentAndEmployee = """
        domain Inverse
        Employee: entity {
          name: Text
        }
        Department: entity {
          reports: many Employee via manager
        }
        """;

    [Test]
    public async Task Via_Clause_ParsesPrintsRoundTrips() {
        var printed = new DomainDslPrinter().Print(Apply(DepartmentAndEmployee));

        await Assert.That(printed).Contains("reports: many Employee via manager");
        await Assert.That(new DomainDslPrinter().Print(Apply(printed))).IsEqualTo(printed);
    }

    [Test]
    public async Task Relationship_Inverse_ExposedOnOntology() {
        var root = Apply(DepartmentAndEmployee);

        await Assert.That(Nav(EntityNamed(root, "Department"), "reports").Inverse).IsEqualTo("manager");
    }

    [Test]
    public async Task Via_UnknownInverseName_IsRecorded_NotRejected() {
        // Parse layer only: naming a navigation that does not exist is S-11's
        // DMREL003, so the parser records the string and reports nothing.
        var changes = new PolyDslParser(DepartmentAndEmployee).Parse();
        var added = changes.OfType<AddRelationshipChange>().Single(c => c.Name == "reports");

        await Assert.That(added.Inverse).IsEqualTo("manager");
    }

    [Test]
    public async Task UndeclaredNavigations_RemainIndependent() {
        var root = Apply("""
            domain NoInverse
            Employee: entity {
              name: Text
            }
            Department: entity {
              name: Text
              manager: Employee
              director: Employee
            }
            """);

        var department = EntityNamed(root, "Department");

        await Assert.That(Nav(department, "manager").Inverse).IsNull();
        await Assert.That(Nav(department, "director").Inverse).IsNull();
    }

    [Test]
    public async Task Via_WithoutName_IsPositionedError() {
        var ex = ParseFails("""
            domain Inverse
            Employee: entity {
              name: Text
            }
            Department: entity {
              reports: many Employee via }
            }
            """);

        await Assert.That(ex.Detail).IsEqualTo("'via' requires the inverse navigation name");
        await Assert.That(ex.Line).IsEqualTo(6);
        await Assert.That(ex.Column).IsEqualTo(26);
    }

    [Test]
    public async Task Via_WithoutName_BeforeMember_IsPositionedError() {
        var ex = ParseFails("""
            domain Inverse
            Employee: entity {
              name: Text
            }
            Department: entity {
              reports: many Employee via
              name: Text
            }
            """);

        await Assert.That(ex.Detail).IsEqualTo("'via' requires the inverse navigation name");
        await Assert.That(ex.Line).IsEqualTo(6);
        await Assert.That(ex.Column).IsEqualTo(26);
    }

    [Test]
    public async Task Via_DeclaredTwice_IsPositionedError() {
        var ex = ParseFails("""
            domain Inverse
            Employee: entity {
              name: Text
            }
            Department: entity {
              reports: many Employee via manager via deputy
            }
            """);

        await Assert.That(ex.Detail).IsEqualTo("'via' is declared more than once on navigation property 'reports'");
        await Assert.That(ex.Line).IsEqualTo(6);
        await Assert.That(ex.Column).IsEqualTo(38);
    }

    [Test]
    public async Task MemberNamedVia_AfterNavLine_AsField_Parses() {
        var root = Apply("""
            domain ContextualVia
            Employee: entity {
              name: Text
            }
            Department: entity {
              reports: many Employee
              via: Text
            }
            """);

        var department = EntityNamed(root, "Department");

        await Assert.That(department.Properties.Any(p => p.Name == "via")).IsTrue();
        await Assert.That(Nav(department, "reports").Inverse).IsNull();
    }

    [Test]
    public async Task MemberNamedVia_AfterNavLine_AsAction_Parses() {
        var root = Apply("""
            domain ContextualVia
            Employee: entity {
              name: Text
            }
            Department: entity {
              reports: many Employee
              via(x: Text): action { }
            }
            """);

        var department = EntityNamed(root, "Department");

        await Assert.That(department.Actions.Any(a => a.Name == "via")).IsTrue();
        await Assert.That(Nav(department, "reports").Inverse).IsNull();
    }

    [Test]
    public async Task MemberNamedVia_AfterViaClause_Parses() {
        var root = Apply("""
            domain ContextualVia
            Employee: entity {
              name: Text
            }
            Department: entity {
              reports: many Employee via manager
              via: Text
            }
            """);

        var department = EntityNamed(root, "Department");

        await Assert.That(Nav(department, "reports").Inverse).IsEqualTo("manager");
        await Assert.That(department.Properties.Any(p => p.Name == "via")).IsTrue();
    }

    [Test]
    public async Task Via_AsTargetType_Parses() {
        var root = Apply("""
            domain ContextualVia
            via: entity {
              name: Text
            }
            Department: entity {
              reports: many via
            }
            """);

        var reports = Nav(EntityNamed(root, "Department"), "reports");

        await Assert.That(reports.Target.TypeName).IsEqualTo("via");
        await Assert.That(reports.Inverse).IsNull();
    }

    [Test]
    public async Task Via_AsInverseName_RoundTrips() {
        var dsl = """
            domain ContextualVia
            Employee: entity {
              name: Text
            }
            Department: entity {
              reports: many Employee via via
            }
            """;

        var root = Apply(dsl);
        var printed = new DomainDslPrinter().Print(root);

        await Assert.That(Nav(EntityNamed(root, "Department"), "reports").Inverse).IsEqualTo("via");
        await Assert.That(printed).Contains("reports: many Employee via via");
        await Assert.That(new DomainDslPrinter().Print(Apply(printed))).IsEqualTo(printed);
    }

    [Test]
    public async Task Via_OnOwnedNavigation_IsAcceptedAndRecorded() {
        var dsl = """
            domain OwnedInverse
            Line: entity {
              sku: Text
            }
            Order: entity {
              lines: many owned Line via order
              line: owned Line via owner
            }
            """;

        var root = Apply(dsl);
        var order = EntityNamed(root, "Order");
        var printed = new DomainDslPrinter().Print(root);

        await Assert.That(Nav(order, "lines").SourceOwnsTarget).IsTrue();
        await Assert.That(Nav(order, "lines").Inverse).IsEqualTo("order");
        await Assert.That(Nav(order, "line").SourceOwnsTarget).IsTrue();
        await Assert.That(Nav(order, "line").Inverse).IsEqualTo("owner");
        await Assert.That(printed).Contains("lines: many owned Line via order");
        await Assert.That(printed).Contains("line: owned Line via owner");
        await Assert.That(new DomainDslPrinter().Print(Apply(printed))).IsEqualTo(printed);
    }
}