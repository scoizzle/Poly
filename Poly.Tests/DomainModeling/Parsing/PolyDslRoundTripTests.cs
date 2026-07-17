using System.Linq;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Parsing;
using Poly.Introspection;

namespace Poly.Tests.DomainModeling.Parsing;

public class PolyDslRoundTripTests {
    [Test]
    public async Task ParseThenPrint_RoundTrips_StructurallyIdentical() {
        var poly = """
            domain TestDomain

            Product: entity {
              SKU: Text required unique
              Name: Text required
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
        var emptyDomain = new Domain("_", [], []);
        var applyResult = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(applyResult.Succeeded).IsTrue();

        var printer = new DomainDslPrinter();
        var printedPoly = printer.Print(applyResult.Root!);

        var parser2 = new PolyDslParser(printedPoly);
        var changes2 = parser2.Parse();
        var emptyDomain2 = new Domain("_", [], []);
        var applyResult2 = new DomainEvolution(emptyDomain2).Apply(changes2);
        await Assert.That(applyResult2.Succeeded).IsTrue();

        var e1 = applyResult.Root!.Types.OfType<Entity>().OrderBy(e => e.Name).First();
        var e2 = applyResult2.Root!.Types.OfType<Entity>().OrderBy(e => e.Name).First();
        await Assert.That(e2.Name).IsEqualTo(e1.Name);
        await Assert.That(e2.Properties.Count).IsEqualTo(e1.Properties.Count);
        await Assert.That(e2.Stages.Count).IsEqualTo(e1.Stages.Count);

        var analysis = DomainModelAnalyzer.Analyze(applyResult2.Root);
        await Assert.That(analysis.HasStructuralFailure).IsFalse();
    }

    [Test]
    public async Task Parse_MinimalEntity_ProducesChangeList() {
        var poly = """
            domain Minimal

            Person: entity {
              Name: Text required
              Age: Number range(0, 150)
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = new Domain("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var person = result.Root!.Types.OfType<Entity>().Single(e => e.Name == "Person");
        await Assert.That(person.Properties.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ParsePrintParse_Minimal_RoundTrips() {
        var poly = """
            domain Simple

            Task: entity {
              Title: Text required
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = new Domain("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var printer = new DomainDslPrinter();
        var printed = printer.Print(result.Root!);

        var parser2 = new PolyDslParser(printed);
        var changes2 = parser2.Parse();
        var emptyDomain2 = new Domain("_", [], []);
        var result2 = new DomainEvolution(emptyDomain2).Apply(changes2);
        await Assert.That(result2.Succeeded).IsTrue();
    }

    [Test]
    public async Task C2_IsNotNull_ProducesNotEqual() {
        var poly = """
            domain Test

            Item: entity {
              Name: Text
              HasName: policy { Name is not null }
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        await Assert.That(changes.Any(c => c is AddPolicyToEntityChange)).IsTrue();

        var policyChange = changes.OfType<AddPolicyToEntityChange>().First();
        await Assert.That(policyChange.Policy.Expression).IsTypeOf<Comparison>();
        var comp = (Comparison)policyChange.Policy.Expression;
        await Assert.That(comp.Kind).IsEqualTo(ComparisonKind.NotEqual);
    }

    [Test]
    public async Task C3_PatternConstraint_StoresRegex() {
        var poly = """
            domain Test

            User: entity {
              Email: Text pattern("[^@]+@[^@]+")
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = new Domain("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var prop = result.Root!.Types.OfType<Entity>().Single().Properties[0];
        await Assert.That(prop.Constraints.Any(c => c is PatternConstraint)).IsTrue();
    }

    [Test]
    public async Task C4_MultiEntity_PrimitivesOnce() {
        var poly = """
            domain Test

            Order: entity {
              Total: Number
            }

            Customer: entity {
              Name: Text
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();

        var emptyDomain = new Domain("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var primitives = result.Root!.Types.OfType<Poly.DomainModeling.PrimitiveType>().ToList();
        await Assert.That(primitives.Count).IsEqualTo(5);
        var entities = result.Root.Types.OfType<Entity>().ToList();
        await Assert.That(entities.Count).IsEqualTo(2);
    }

    [Test]
    public async Task C5_Relationship_Subscription_RoundTrips() {
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
        var emptyDomain = new Domain("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        await Assert.That(result.Root!.Relationships.Count).IsEqualTo(1);
        await Assert.That(result.Root.Relationships[0].Name).IsEqualTo("Tracks");

        var tracker = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Tracker");
        var pending = tracker.Stages.Single(s => s.Name == "Pending");
        await Assert.That(pending.Subscriptions.Count).IsEqualTo(1);

        var analysis = DomainModelAnalyzer.Analyze(result.Root);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionContractMismatch)).IsFalse();

        var printer = new DomainDslPrinter();
        var printed = printer.Print(result.Root);
        var parser2 = new PolyDslParser(printed);
        var changes2 = parser2.Parse();
        var emptyDomain2 = new Domain("_", [], []);
        var result2 = new DomainEvolution(emptyDomain2).Apply(changes2);
        await Assert.That(result2.Succeeded).IsTrue();
        await Assert.That(result2.Root!.Relationships.Count).IsEqualTo(1);
    }

    [Test]
    public async Task C9_AllConstraints_RoundTrip() {
        var poly = """
            domain Test

            Item: entity {
              Code: Text required unique
              Name: Text required
              Qty: Number range(0, 999)
              Label: Text length(1, 10)
              Email: Text pattern("[^@]+@[^@]+")
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = new Domain("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var item = result.Root!.Types.OfType<Entity>().Single();
        var code = item.Properties.Single(p => p.Name == "Code");
        await Assert.That(code.Constraints.Any(c => c is RequiredConstraint)).IsTrue();
        await Assert.That(code.Constraints.Any(c => c is UniqueConstraint)).IsTrue();

        var printer = new DomainDslPrinter();
        var printed = printer.Print(result.Root);
        var parser2 = new PolyDslParser(printed);
        var changes2 = parser2.Parse();
        var emptyDomain2 = new Domain("_", [], []);
        var result2 = new DomainEvolution(emptyDomain2).Apply(changes2);
        await Assert.That(result2.Succeeded).IsTrue();
    }

    [Test]
    public async Task C1_Require_AfterPolicy_BindsRealExpression() {
        var poly = """
            domain Test

            Item: entity {
              HasName: policy { Name is not null }

              Draft: stage {
                Activate: action
                  require HasName
                {
                  transition to Active
                }
              }
              Active: stage {}
              Name: Text
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = new Domain("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var entity = result.Root!.Types.OfType<Entity>().Single();
        var activate = entity.Stages.SelectMany(s => s.Actions)
            .Single(a => a.Name == "Activate");
        await Assert.That(activate.Policies.Count).IsEqualTo(1);
        var policy = activate.Policies[0];
        await Assert.That(policy.Name).IsEqualTo("HasName");
        // Should be the real expression (Comparison for "is not null"), not Literal(true)
        await Assert.That(policy.Expression).IsTypeOf<Poly.DomainModeling.Comparison>();
    }

    [Test]
    public async Task C1_MissingRequire_ThrowsParseError() {
        var poly = """
            domain Test

            Item: entity {
              Draft: stage {
                Activate: action
                  require NonExistent
                {
                  transition to Active
                }
              }
              Active: stage {}
            }
            """;

        var parser = new PolyDslParser(poly);
        var threw = false;
        try {
            parser.Parse();
        }
        catch (FormatException ex) {
            await Assert.That(ex.Message.Contains("NonExistent")).IsTrue();
            threw = true;
        }
        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task C1_RequireNot_BindsRealExpression() {
        var poly = """
            domain Test

            Item: entity {
              IsBlocked: policy { Status is "Blocked" }

              Draft: stage {
                Activate: action
                  require not IsBlocked
                {
                  transition to Active
                }
              }
              Active: stage {}
              Status: Text
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = new Domain("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var entity2 = result.Root!.Types.OfType<Entity>().Single();
        var activate2 = entity2.Stages.SelectMany(s => s.Actions)
            .Single(a => a.Name == "Activate");
        await Assert.That(activate2.Policies.Count).IsEqualTo(1);
        var policy2 = activate2.Policies[0];
        await Assert.That(policy2.Name).IsEqualTo("not_IsBlocked");
        await Assert.That(policy2.Expression).IsTypeOf<Poly.DomainModeling.Not>();
    }

    [Test]
    public async Task C2_NoWhenPolicies_Emitted() {
        // Verify that "when Draft" on an action does NOT produce any policies
        var poly = """
            domain Test

            Item: entity {
              Draft: stage {
                Activate: action
                  when Draft
                {
                  transition to Active
                }
              }
              Active: stage {}
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = new Domain("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var entity3 = result.Root!.Types.OfType<Entity>().Single();
        var activate3 = entity3.Stages.SelectMany(s => s.Actions)
            .Single(a => a.Name == "Activate");
        // No policies should be attached (when gates are not runtime-enforced in Phase 1a)
        await Assert.That(activate3.Policies.Count).IsEqualTo(0);

        // Round-trip: printer should not output when either
        var printer = new DomainDslPrinter();
        var printed = printer.Print(result.Root);
        await Assert.That(printed.Contains("when")).IsFalse();
    }

    [Test]
    public async Task Unsupported_Actor_ThrowsPhase1Error() {
        var poly = """
            domain Test
            Person: actor { }
            """;

        var parser = new PolyDslParser(poly);
        var threw = false;
        try { parser.Parse(); }
        catch (FormatException ex) {
            await Assert.That(ex.Message.Contains("not supported in Phase 1a")).IsTrue();
            threw = true;
        }
        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task Unsupported_ValueType_ThrowsPhase1Error() {
        var poly = """
            domain Test
            Money: value { }
            """;

        var parser = new PolyDslParser(poly);
        var threw = false;
        try { parser.Parse(); }
        catch (FormatException ex) {
            await Assert.That(ex.Message.Contains("not supported in Phase 1a")).IsTrue();
            threw = true;
        }
        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task Unsupported_CreateEffect_ThrowsPhase1Error() {
        var poly = """
            domain Test
            Item: entity {
              Draft: stage {
                Go: action {
                  create Item in Draft
                }
              }
            }
            """;

        var parser = new PolyDslParser(poly);
        var threw = false;
        try { parser.Parse(); }
        catch (FormatException ex) {
            await Assert.That(ex.Message.Contains("not supported in Phase 1a")).IsTrue();
            threw = true;
        }
        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task Malformed_UnclosedEntity_ThrowsError() {
        var poly = """
            domain Test
            Item: entity {
              Name: Text
            """;

        var parser = new PolyDslParser(poly);
        var threw = false;
        try { parser.Parse(); }
        catch (FormatException) {
            threw = true;
        }
        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task C3_RequireNot_EntityLevel_BindsRealExpression() {
        // Verify entity-level "require not PolicyName" works (not only stage-level)
        var poly = """
            domain Test

            Item: entity {
              IsBlocked: policy { Status is "Blocked" }

              Validate: action
                require not IsBlocked
              {
                assign Status to "Validated"
              }
              Status: Text
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = new Domain("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var entity = result.Root!.Types.OfType<Entity>().Single();
        var validate = entity.Actions.Single(a => a.Name == "Validate");
        await Assert.That(validate.Policies.Count).IsEqualTo(1);
        var policy = validate.Policies[0];
        await Assert.That(policy.Name).IsEqualTo("not_IsBlocked");
        await Assert.That(policy.Expression).IsTypeOf<Poly.DomainModeling.Not>();
    }
}