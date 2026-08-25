using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
// PolyDslParser

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
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var applyResult = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(applyResult.Succeeded).IsTrue();

        var printer = new DomainDslPrinter();
        var printedPoly = printer.Print(applyResult.Root!);

        var parser2 = new PolyDslParser(printedPoly);
        var changes2 = parser2.Parse();
        var emptyDomain2 = DomainTestFactory.Create("_", [], []);
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
    public async Task Parse_EntryExitEffects_RoundTrips() {
        // P2.4: entry/exit effects on stages should parse and print round-trip
        var poly = """
            domain Test

            Item: entity {
              Status: Text

              Active: stage {
                entry {
                  assign Status to "Entered"
                }
                exit {
                  assign Status to "Exited"
                }
                DoStuff: action {
                  transition to Done
                }
              }
              Done: stage {}
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var item = result.Root.Types.OfType<Entity>().Single();
        var active = item.Stages.Single(s => s.Name == "Active");
        await Assert.That(active.OnEntryEffects.Count).IsEqualTo(1);
        await Assert.That(active.OnExitEffects.Count).IsEqualTo(1);

        // Print → re-parse → structural identity
        var printer = new DomainDslPrinter();
        var printed = printer.Print(result.Root);

        var parser2 = new PolyDslParser(printed);
        var changes2 = parser2.Parse();
        var emptyDomain2 = DomainTestFactory.Create("_", [], []);
        var result2 = new DomainEvolution(emptyDomain2).Apply(changes2);
        await Assert.That(result2.Succeeded).IsTrue();

        var item2 = result2.Root.Types.OfType<Entity>().Single();
        var active2 = item2.Stages.Single(s => s.Name == "Active");
        await Assert.That(active2.OnEntryEffects.Count).IsEqualTo(1);
        await Assert.That(active2.OnExitEffects.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Parse_When_WithPeerBinding_RoundTrips() {
        var poly = """
            domain Test

            Tracker: entity {
              Status: Text
              Tracks: Order

              Pending: stage {
                when Tracks Active as order {
                  assign Status to order Code
                }
              }
            }

            Order: entity {
              Code: Text
              Draft: stage { }
              Active: stage { }
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var tracker = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Tracker");
        var pending = tracker.Stages.Single(s => s.Name == "Pending");
        await Assert.That(pending.Subscriptions.Count).IsEqualTo(1);
        await Assert.That(pending.Subscriptions[0].PeerBinding).IsEqualTo("order");
        var assign = pending.Subscriptions[0].Effects.OfType<AssignEffect>().Single();
        await Assert.That(assign.Value).IsTypeOf<RelationshipNavigation>();
        var nav = (RelationshipNavigation)assign.Value;
        await Assert.That(nav.RelationshipName).IsEqualTo("order");

        var printed = new DomainDslPrinter().Print(result.Root);
        await Assert.That(printed.Contains("as order", StringComparison.Ordinal)).IsTrue();

        var reparsed = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(new PolyDslParser(printed).Parse());
        await Assert.That(reparsed.Succeeded).IsTrue();
        var sub2 = reparsed.Root.Types.OfType<Entity>().Single(e => e.Name == "Tracker")
            .Stages.Single(s => s.Name == "Pending").Subscriptions[0];
        await Assert.That(sub2.PeerBinding).IsEqualTo("order");
    }

    [Test]
    public async Task Parse_MultiStageWhen_RoundTrips() {
        // P2.5: when Rel Stage1, Stage2 should parse and print round-trip
        var poly = """
            domain Test

            Tracker: entity {
              Status: Text
              Tracks: Order

              Pending: stage {
                when Tracks Active, Done {
                  assign Status to "Triggered"
                }
              }
            }

            Order: entity {
              Draft: stage { }
              Active: stage { }
              Done: stage {}
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var tracker = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Tracker");
        var pending = tracker.Stages.Single(s => s.Name == "Pending");
        await Assert.That(pending.Subscriptions.Count).IsEqualTo(1);
        await Assert.That(pending.Subscriptions[0].StageNames.Count).IsEqualTo(2);
        await Assert.That(pending.Subscriptions[0].StageNames).Contains("Active");
        await Assert.That(pending.Subscriptions[0].StageNames).Contains("Done");

        // Print → re-parse → structural identity
        var printer = new DomainDslPrinter();
        var printed = printer.Print(result.Root);
        await Assert.That(printed.Contains("Tracks: Order")).IsTrue();

        var parser2 = new PolyDslParser(printed);
        var changes2 = parser2.Parse();
        var emptyDomain2 = DomainTestFactory.Create("_", [], []);
        var result2 = new DomainEvolution(emptyDomain2).Apply(changes2);
        await Assert.That(result2.Succeeded).IsTrue();

        var tracker2 = result2.Root.Types.OfType<Entity>().Single(e => e.Name == "Tracker");
        var pending2 = tracker2.Stages.Single(s => s.Name == "Pending");
        await Assert.That(pending2.Subscriptions.Count).IsEqualTo(1);
        await Assert.That(pending2.Subscriptions[0].StageNames.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Parse_WhenAny_RoundTrips() {
        // p4-1: when any Rel Stage [as name] parses to Any and prints back with 'any'.
        var poly = """
            domain Test

            Tracker: entity {
              Status: Text
              Tracks: many Order

              Pending: stage {
                when any Tracks Active as order {
                  assign Status to order Code
                }
              }
            }

            Order: entity {
              Code: Text
              Draft: stage { }
              Active: stage { }
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var tracker = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Tracker");
        var pending = tracker.Stages.Single(s => s.Name == "Pending");
        await Assert.That(pending.Subscriptions.Count).IsEqualTo(1);
        await Assert.That(pending.Subscriptions[0].Quantifier).IsEqualTo(StageSubscriptionQuantifier.Any);
        await Assert.That(pending.Subscriptions[0].PeerBinding).IsEqualTo("order");

        var printed = new DomainDslPrinter().Print(result.Root);
        await Assert.That(printed.Contains("when any Tracks Active as order", StringComparison.Ordinal)).IsTrue();

        var reparsed = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(new PolyDslParser(printed).Parse());
        await Assert.That(reparsed.Succeeded).IsTrue();
        var sub2 = reparsed.Root.Types.OfType<Entity>().Single(e => e.Name == "Tracker")
            .Stages.Single(s => s.Name == "Pending").Subscriptions[0];
        await Assert.That(sub2.Quantifier).IsEqualTo(StageSubscriptionQuantifier.Any);
        await Assert.That(sub2.PeerBinding).IsEqualTo("order");
    }

    [Test]
    public async Task Parse_WhenAll_MultiStage_RoundTrips() {
        // p4-1: when all Rel Stage1, Stage2 parses to All and prints back with 'all'.
        var poly = """
            domain Test

            Tracker: entity {
              Status: Text
              Tracks: many Order

              Pending: stage {
                when all Tracks Active, Done {
                  assign Status to "Triggered"
                }
              }
            }

            Order: entity {
              Draft: stage { }
              Active: stage { }
              Done: stage {}
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var tracker = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Tracker");
        var pending = tracker.Stages.Single(s => s.Name == "Pending");
        await Assert.That(pending.Subscriptions.Count).IsEqualTo(1);
        await Assert.That(pending.Subscriptions[0].Quantifier).IsEqualTo(StageSubscriptionQuantifier.All);
        await Assert.That(pending.Subscriptions[0].StageNames.Count).IsEqualTo(2);

        var printed = new DomainDslPrinter().Print(result.Root);
        await Assert.That(printed.Contains("when all Tracks Active, Done", StringComparison.Ordinal)).IsTrue();

        var reparsed = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(new PolyDslParser(printed).Parse());
        await Assert.That(reparsed.Succeeded).IsTrue();
        var sub2 = reparsed.Root.Types.OfType<Entity>().Single(e => e.Name == "Tracker")
            .Stages.Single(s => s.Name == "Pending").Subscriptions[0];
        await Assert.That(sub2.Quantifier).IsEqualTo(StageSubscriptionQuantifier.All);
        await Assert.That(sub2.StageNames.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Parse_When_OmittedQuantifier_IsEach_AndPrintOmitsKeyword() {
        // p4-1: omitted quantifier stays Each (product default); printer must NOT emit any/all.
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
              Draft: stage { }
              Active: stage { }
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var tracker = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Tracker");
        var pending = tracker.Stages.Single(s => s.Name == "Pending");
        await Assert.That(pending.Subscriptions[0].Quantifier).IsEqualTo(StageSubscriptionQuantifier.Each);

        var printed = new DomainDslPrinter().Print(result.Root);
        await Assert.That(printed.Contains("when Tracks Active", StringComparison.Ordinal)).IsTrue();
        await Assert.That(printed.Contains("when any ", StringComparison.Ordinal)).IsFalse();
        await Assert.That(printed.Contains("when all ", StringComparison.Ordinal)).IsFalse();

        var reparsed = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(new PolyDslParser(printed).Parse());
        await Assert.That(reparsed.Succeeded).IsTrue();
        var sub2 = reparsed.Root.Types.OfType<Entity>().Single(e => e.Name == "Tracker")
            .Stages.Single(s => s.Name == "Pending").Subscriptions[0];
        await Assert.That(sub2.Quantifier).IsEqualTo(StageSubscriptionQuantifier.Each);
    }

    [Test]
    public async Task Parse_EntityLevelWhenAny_RoundTrips() {
        // p4-1: entity-level subscription also accepts the any/all quantifier.
        var poly = """
            domain Test

            Patron: entity {
              Name: Text
              loans: many Loan

              when any loans Overdue {
                assign Name to "Flagged"
              }
            }

            Loan: entity {
              Draft: stage { }
              Overdue: stage { }
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var patron = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Patron");
        await Assert.That(patron.Subscriptions.Count).IsEqualTo(1);
        await Assert.That(patron.Subscriptions[0].Quantifier).IsEqualTo(StageSubscriptionQuantifier.Any);

        var printed = new DomainDslPrinter().Print(result.Root);
        await Assert.That(printed.Contains("when any loans Overdue", StringComparison.Ordinal)).IsTrue();

        var reparsed = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(new PolyDslParser(printed).Parse());
        await Assert.That(reparsed.Succeeded).IsTrue();
        var sub2 = reparsed.Root.Types.OfType<Entity>().Single(e => e.Name == "Patron").Subscriptions[0];
        await Assert.That(sub2.Quantifier).IsEqualTo(StageSubscriptionQuantifier.Any);
    }

    [Test]
    public async Task Parse_StagePrev_Rejected() {
        // P2′′′′′.3: Using the removed 'prev' keyword should produce a clear error.
        var poly = """
            domain Test

            Item: entity {
              Active: stage prev Draft {
              }
            }
            """;

        var parser = new PolyDslParser(poly);
        var ex = Assert.Throws<FormatException>(() => parser.Parse());
        await Assert.That(ex.Message).Contains("prev");
        await Assert.That(ex.Message).Contains("no longer supported");
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
        var emptyDomain = DomainTestFactory.Create("_", [], []);
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
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var printer = new DomainDslPrinter();
        var printed = printer.Print(result.Root!);

        var parser2 = new PolyDslParser(printed);
        var changes2 = parser2.Parse();
        var emptyDomain2 = DomainTestFactory.Create("_", [], []);
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
        var emptyDomain = DomainTestFactory.Create("_", [], []);
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

        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var primitives = result.Root!.Types.OfType<Poly.DomainModeling.Ontology.PrimitiveType>().ToList();
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
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        await Assert.That(result.Relationships().Count).IsEqualTo(1);
        await Assert.That(result.Relationships()[0].Name).IsEqualTo("Tracks");

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
        var emptyDomain2 = DomainTestFactory.Create("_", [], []);
        var result2 = new DomainEvolution(emptyDomain2).Apply(changes2);
        await Assert.That(result2.Succeeded).IsTrue();
        await Assert.That(result2.Relationships().Count).IsEqualTo(1);
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
        var emptyDomain = DomainTestFactory.Create("_", [], []);
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
        var emptyDomain2 = DomainTestFactory.Create("_", [], []);
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
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var entity = result.Root!.Types.OfType<Entity>().Single();
        var activate = entity.Stages.SelectMany(s => s.Actions)
            .Single(a => a.Name == "Activate");
        await Assert.That(activate.Policies.Count).IsEqualTo(1);
        var policy = activate.Policies[0];
        await Assert.That(policy.Name).IsEqualTo("HasName");
        // Should be the real expression (Comparison for "is not null"), not Literal(true)
        await Assert.That(policy.Expression).IsTypeOf<Comparison>();
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
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var entity2 = result.Root!.Types.OfType<Entity>().Single();
        var activate2 = entity2.Stages.SelectMany(s => s.Actions)
            .Single(a => a.Name == "Activate");
        await Assert.That(activate2.Policies.Count).IsEqualTo(1);
        var policy2 = activate2.Policies[0];
        await Assert.That(policy2.Name).IsEqualTo("not_IsBlocked");
        await Assert.That(policy2.Expression).IsTypeOf<Poly.DomainModeling.Ontology.Not>();
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
        var emptyDomain = DomainTestFactory.Create("_", [], []);
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
    public async Task Parse_ValueType_RoundTrips() {
        var poly = """
            domain Test

            Money: value {
              Amount: Number
              Currency: Text
            }

            Order: entity {
              Price: Money
            }
            """;
        var first = Apply(poly);
        var money = first.Types.OfType<Poly.DomainModeling.Ontology.ValueType>().Single();
        await Assert.That(money.Name).IsEqualTo("Money");
        await Assert.That(money.Properties.Count).IsEqualTo(2);
        var printed = new DomainDslPrinter().Print(first);
        await Assert.That(printed.Contains("Money: value")).IsTrue();
        var second = Apply(printed);
        await Assert.That(second.Types.OfType<Poly.DomainModeling.Ontology.ValueType>().Single().Properties.Count).IsEqualTo(2);
        await Assert.That(second.Types.OfType<Entity>().Single().Properties.Single().Type.TypeName)
            .IsEqualTo("Money");
    }

    [Test]
    public async Task Parse_ContractAndBind_RoundTrips() {
        var poly = """
            domain Test

            Order: entity {
              Total: Number
              Pay: action (request: ChargeRequest) {
                assign Total to Total
              }
            }

            Stripe: contract external stripe v1 {
              ChargeRequest: value {
                Amount: Number
                Currency: Text
              }
              Charge: outbound operation ChargeRequest
            }

            ChargeOrder: bind Stripe Charge to Pay request
            """;
        var first = Apply(poly);
        await Assert.That(first.ImportedContracts.Count).IsEqualTo(1);
        await Assert.That(first.ImportedContracts[0].Types.Count).IsEqualTo(1);
        await Assert.That(first.ImportedContracts[0].Types[0].Name).IsEqualTo("ChargeRequest");
        await Assert.That(first.ImportedContracts[0].Endpoints.Count).IsEqualTo(1);
        await Assert.That(first.ContractBindings.Count).IsEqualTo(1);
        var printed = new DomainDslPrinter().Print(first);
        await Assert.That(printed.Contains("ChargeRequest: value")).IsTrue();
        var second = Apply(printed);
        await Assert.That(second.ImportedContracts[0].Name).IsEqualTo("Stripe");
        await Assert.That(second.ImportedContracts[0].Types[0].Properties.Count).IsEqualTo(2);
        await Assert.That(second.ContractBindings[0].ActionName).IsEqualTo("Pay");
        await Assert.That(second.ContractBindings[0].LocalParameterName).IsEqualTo("request");
    }

    [Test]
    public async Task Parse_CreateEntityEffect_RoundTrips() {
        var poly = """
            domain Test

            Item: entity {
              Name: Text
              Draft: stage {
                Go: action {
                  create Item { Name: "NewItem" }
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

        var printer = new DomainDslPrinter();
        var printed = printer.Print(result.Root);

        // Re-parse and verify structural identity
        var parser2 = new PolyDslParser(printed);
        var changes2 = parser2.Parse();
        var emptyDomain2 = DomainTestFactory.Create("_", [], []);
        var result2 = new DomainEvolution(emptyDomain2).Apply(changes2);
        await Assert.That(result2.Succeeded).IsTrue();

        var item1 = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Item");
        var item2 = result2.Root.Types.OfType<Entity>().Single(e => e.Name == "Item");
        await Assert.That(item2.Stages.Count).IsEqualTo(item1.Stages.Count);
        await Assert.That(item2.Actions.Count).IsEqualTo(item1.Actions.Count);
    }

    [Test]
    public async Task Parse_CreateInEffect_RoundTrips() {
        var poly = """
            domain Test

            Customer: entity {
              Name: Text
              Pending: stage {
                Go: action {
                  create in orders { }
                }
              }
              orders: many Order
            }

            Order: entity {
              Title: Text
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        // Verify the create-in effect parsed as CreateEntityInRelationshipEffect
        var customer = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Customer");
        var go = customer.Stages.SelectMany(s => s.Actions).FirstOrDefault(a => a.Name == "Go");
        await Assert.That(go).IsNotNull();
        await Assert.That(go!.Effects.Count).IsEqualTo(1);
        await Assert.That(go.Effects[0]).IsTypeOf<CreateEntityInRelationshipEffect>();
        var createIn = (CreateEntityInRelationshipEffect)go.Effects[0];
        await Assert.That(createIn.RelationshipName).IsEqualTo("orders");

        var printer = new DomainDslPrinter();
        var printed = printer.Print(result.Root);

        // Re-parse and verify structural identity
        var parser2 = new PolyDslParser(printed);
        var changes2 = parser2.Parse();
        var emptyDomain2 = DomainTestFactory.Create("_", [], []);
        var result2 = new DomainEvolution(emptyDomain2).Apply(changes2);
        await Assert.That(result2.Succeeded).IsTrue();

        var customer2 = result2.Root.Types.OfType<Entity>().Single(e => e.Name == "Customer");
        var go2 = customer2.Stages.SelectMany(s => s.Actions).FirstOrDefault(a => a.Name == "Go");
        await Assert.That(go2).IsNotNull();
        await Assert.That(go2!.Effects.Count).IsEqualTo(1);
        await Assert.That(go2.Effects[0]).IsTypeOf<CreateEntityInRelationshipEffect>();
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
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var entity = result.Root!.Types.OfType<Entity>().Single();
        var validate = entity.Actions.Single(a => a.Name == "Validate");
        await Assert.That(validate.Policies.Count).IsEqualTo(1);
        var policy = validate.Policies[0];
        await Assert.That(policy.Name).IsEqualTo("not_IsBlocked");
        await Assert.That(policy.Expression).IsTypeOf<Poly.DomainModeling.Ontology.Not>();
    }

    [Test]
    public async Task EqualsConstraint_ParseAndRoundTrip() {
        var poly = """
            domain Test

            Item: entity {
              Status: Text default("Active")
            }
            """;
        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();
        var item = result.Root!.Types.OfType<Entity>().Single();
        await Assert.That(item.Properties.Any(p =>
            p.Name == "Status" && p.Constraints.Any(c => c is DefaultValueConstraint))).IsTrue();
    }

    [Test]
    public async Task EnumType_ParseAndRoundTrip() {
        var poly = """
            domain Test

            Color: enum {
              Red,
              Green,
              Blue,
            }

            Item: entity {
              Color: Color
            }
            """;
        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();
        var enumType = result.Root!.Types.OfType<EnumType>().Single();
        await Assert.That(enumType.MemberNames.Count).IsEqualTo(3);
        await Assert.That(enumType.MemberNames).Contains("Red");
        await Assert.That(enumType.MemberNames).Contains("Green");
        await Assert.That(enumType.MemberNames).Contains("Blue");

        var printer = new DomainDslPrinter();
        var printed = printer.Print(result.Root);
        await Assert.That(printed.Contains("Color: enum {")).IsTrue();
    }

    [Test]
    public async Task Arithmetic_Expression_ParseAndRoundTrip() {
        var poly = """
            domain Test

            Item: entity {
              Total: Number
              Computed: policy { Total + 5 > 10 }
            }
            """;
        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();
        // Verify printer includes arithmetic
        var printer = new DomainDslPrinter();
        var printed = printer.Print(result.Root!);
        await Assert.That(printed.Contains("+")).IsTrue();
    }

    [Test]
    public async Task InvokeEffect_ParseAndRoundTrip() {
        var poly = """
            domain Test

            Order: entity {
              Status: Text
              Draft: stage {
                Submit: action {
                  invoke Validate
                  transition to Active
                }
              }
              Active: stage {}
              Validate: action { assign Status to "validated" }
            }
            """;
        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();
        var printer = new DomainDslPrinter();
        var printed = printer.Print(result.Root!);
        await Assert.That(printed.Contains("invoke Validate")).IsTrue();
    }

    [Test]
    public async Task ConditionalEffect_ParseAndRoundTrip() {
        var poly = """
            domain Test

            Item: entity {
              Status: Text
              Count: Number
              Draft: stage {
                Process: action {
                  if (Count > 0) {
                    assign Status to "ok"
                  } else {
                    assign Status to "empty"
                  }
                  transition to Done
                }
              }
              Done: stage {}
            }
            """;
        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();
        var printer = new DomainDslPrinter();
        var printed = printer.Print(result.Root!);
        await Assert.That(printed.Contains("if (")).IsTrue();
        await Assert.That(printed.Contains("else {")).IsTrue();
    }

    [Test]
    public async Task InvokeKeyword_NoLongerRejected() {
        // Verify that 'invoke' is accepted (removed from unsupported keywords)
        var poly = """
            domain Test

            Item: entity {
              DoIt: action {
                invoke Validate
              }
              Validate: action { }
            }
            """;
        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        await Assert.That(changes.Count > 0).IsTrue();
    }

    [Test]
    public async Task OwnedKeyword_IsTokenized() {
        var poly = """
            domain Test

            Customer: entity {
              passport: owned Passport
            }
            Passport: entity {
              Number: Text
            }
            """;
        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();
        // Verify the relationship exists and has SourceOwnsTarget=true
        var rel = result.Relationships().FirstOrDefault(r => r.Name == "passport");
        await Assert.That(rel).IsNotNull();
        await Assert.That(rel!.SourceOwnsTarget).IsTrue();
    }

    [Test]
    public async Task CombinedSurface_ParamsOwnedInheritanceIfInvoke_RoundTrips() {
        // E6.3: single domain exercising params + owned + inheritance (removed) + if + invoke
        var poly = """
            domain Combined

            Worker: entity {
              Name: Text
              Role: Text
              Score: Number
              Badge: Text
              passport: owned Passport

              Draft: stage {
                Promote: action (level: Text) {
                  if (Score > 0) {
                    invoke Stamp(mark: level)
                  } else {
                    assign Badge to "skip"
                  }
                  transition to Active
                }
              }
              Active: stage {}

              Stamp: action (mark: Text) {
                assign Badge to mark
              }
            }

            Passport: entity {
              Number: Text
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var worker = result.Root!.Types.OfType<Entity>().Single(e => e.Name == "Worker");
        await Assert.That(worker.Properties.Any(p => p.Name == "Name")).IsTrue();
        await Assert.That(worker.Properties.Any(p => p.Name == "Role")).IsTrue();
        await Assert.That(worker.Properties.Any(p => p.Name == "Badge")).IsTrue();

        var promote = worker.Stages.Single(s => s.Name == "Draft").Actions
            .Single(a => a.Name == "Promote");
        await Assert.That(promote.Parameters.Count).IsEqualTo(1);
        await Assert.That(promote.Effects.Any(e => e is ConditionalEffect)).IsTrue();

        var owned = result.Relationships().Single(r => r.Name == "passport");
        await Assert.That(owned.SourceOwnsTarget).IsTrue();

        var printer = new DomainDslPrinter();
        var printed = printer.Print(result.Root);

        await Assert.That(printed.Contains("Worker: entity")).IsTrue();
        await Assert.That(printed.Contains("owned Passport")).IsTrue();
        await Assert.That(printed.Contains("Promote: action (level: Text)")).IsTrue();
        await Assert.That(printed.Contains("if (")).IsTrue();
        await Assert.That(printed.Contains("invoke Stamp")).IsTrue();

        var parser2 = new PolyDslParser(printed);
        var changes2 = parser2.Parse();
        var empty2 = DomainTestFactory.Create("_", [], []);
        var result2 = new DomainEvolution(empty2).Apply(changes2);
        await Assert.That(result2.Succeeded).IsTrue();

        var worker2 = result2.Root!.Types.OfType<Entity>().Single(e => e.Name == "Worker");
        await Assert.That(worker2.Properties.Any(p => p.Name == "Name")).IsTrue();
        await Assert.That(worker2.Properties.Any(p => p.Name == "Role")).IsTrue();
        await Assert.That(worker2.Stages.Single(s => s.Name == "Draft").Actions
            .Single(a => a.Name == "Promote").Parameters.Count).IsEqualTo(1);
        await Assert.That(result2.Relationships().Single(r => r.Name == "passport").SourceOwnsTarget).IsTrue();

        var analysis = DomainModelAnalyzer.Analyze(result2.Root);
        await Assert.That(analysis.HasStructuralFailure).IsFalse();
    }

    [Test]
    public async Task ElseIf_ParsePrint_RoundTripsAsElseIf() {
        var poly = """
            domain Test

            Item: entity {
              Status: Text
              Score: Number
              Grade: action {
                if (Score >= 90) {
                  assign Status to "A"
                } else if (Score >= 70) {
                  assign Status to "B"
                } else {
                  assign Status to "C"
                }
              }
            }
            """;
        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var grade = result.Root!.Types.OfType<Entity>().Single().Actions.Single();
        await Assert.That(grade.Effects[0]).IsTypeOf<ConditionalEffect>();
        var outer = (ConditionalEffect)grade.Effects[0];
        await Assert.That(outer.ElseEffects).IsNotNull();
        await Assert.That(outer.ElseEffects![0]).IsTypeOf<ConditionalEffect>();

        var printed = new DomainDslPrinter().Print(result.Root);
        await Assert.That(printed.Contains("else if")).IsTrue();

        var result2 = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .Apply(new PolyDslParser(printed).Parse());
        await Assert.That(result2.Succeeded).IsTrue();
        var printed2 = new DomainDslPrinter().Print(result2.Root!);
        await Assert.That(printed2.Contains("else if")).IsTrue();
    }

    [Test]
    public async Task EqualsConstraint_EscapedQuote_RoundTrips() {
        var poly = """
            domain Test

            Item: entity {
              Note: Text default("say \"hi\"")
            }
            """;
        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var prop = result.Root!.Types.OfType<Entity>().Single().Properties.Single();
        var dv = prop.Constraints.OfType<DefaultValueConstraint>().Single();
        var lit = dv.Expression as Literal;
        await Assert.That(lit?.Value).IsEqualTo("say \"hi\"");

        var printed = new DomainDslPrinter().Print(result.Root);
        await Assert.That(printed.Contains("\\\"")).IsTrue();

        var result2 = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .Apply(new PolyDslParser(printed).Parse());
        await Assert.That(result2.Succeeded).IsTrue();
        var dv2 = result2.Root!.Types.OfType<Entity>().Single().Properties.Single()
            .Constraints.OfType<DefaultValueConstraint>().Single();
        var lit2 = dv2.Expression as Literal;
        await Assert.That(lit2?.Value).IsEqualTo("say \"hi\"");
    }

    [Test]
    public async Task Print_NotComparison_RoundTripsWithParens() {
        var poly = """
            domain Test

            Item: entity {
              Total: Number
              Positive: policy { not (Total > 0) }
            }
            """;
        var first = Apply(poly);
        var printed = new DomainDslPrinter().Print(first);
        await Assert.That(printed.Contains("not (Total > 0)") || printed.Contains("not ((Total > 0))")).IsTrue();
        var second = Apply(printed);
        var expr = second.Types.OfType<Entity>().Single().Policies.Single().Expression;
        await Assert.That(expr).IsTypeOf<Poly.DomainModeling.Ontology.Not>();
        await Assert.That(((Poly.DomainModeling.Ontology.Not)expr).Operand).IsTypeOf<Comparison>();
    }

    [Test]
    public async Task Print_MixedRequireGates_RoundTrips() {
        var poly = """
            domain Test

            Item: entity {
              Ok: policy { Status is "Ok" }
              Blocked: policy { Status is "Blocked" }
              Status: Text
              Go: action
                require Ok
                require not Blocked
              {
                assign Status to "Went"
              }
            }
            """;
        var first = Apply(poly);
        var printed = new DomainDslPrinter().Print(first);
        await Assert.That(printed.Contains("require Ok, not Blocked")).IsFalse();
        var second = Apply(printed);
        var go = second.Types.OfType<Entity>().Single().Actions.Single(a => a.Name == "Go");
        await Assert.That(go.Policies.Count).IsEqualTo(2);
        await Assert.That(go.Policies.Any(p => p.Name == "Ok")).IsTrue();
        await Assert.That(go.Policies.Any(p => p.Name == "not_Blocked")).IsTrue();
    }

    [Test]
    public async Task Print_CreateInMultiInitializer_WhitespaceSeparators() {
        var poly = """
            domain Test

            Customer: entity {
              Name: Text
              Place: action {
                create in orders { Title: "A" Total: 1 }
              }
              orders: many Order
            }

            Order: entity {
              Title: Text
              Total: Number
            }
            """;
        var first = Apply(poly);
        var printed = new DomainDslPrinter().Print(first);
        await Assert.That(printed.Contains("Title:")).IsTrue();
        await Assert.That(printed.Contains(",")).IsFalse();
        var second = Apply(printed);
        var place = second.Types.OfType<Entity>().Single(e => e.Name == "Customer")
            .Actions.Single(a => a.Name == "Place");
        var createIn = (CreateEntityInRelationshipEffect)place.Effects[0];
        await Assert.That(createIn.Initializers.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Print_EqualityConstraint_IsOmitted() {
        var domain = Apply("""
            domain Test
            Item: entity { Status: Text }
            """);
        var item = domain.Types.OfType<Entity>().Single();
        var pinned = item with {
            Properties = [
                item.Properties[0] with {
                    Constraints = [new EqualityConstraint("Active")]
                }
            ]
        };
        var withEq = new Domain(domain.Name, [pinned]);
        var printed = new DomainDslPrinter().Print(withEq);
        await Assert.That(printed.Contains("equals")).IsFalse();
        await Assert.That(printed.Contains("/*")).IsFalse();
        var second = Apply(printed);
        await Assert.That(second.Types.OfType<Entity>().Single().Properties.Single()
            .Constraints.OfType<EqualityConstraint>().Any()).IsFalse();
    }

    private static Domain Apply(string poly) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .Apply(new PolyDslParser(poly).Parse());
        if (!result.Succeeded)
            throw new InvalidOperationException(result.FailureSummary ?? "evolution failed");
        return result.Root!;
    }

    [Test]
    public async Task ProduceFillPrintParse_FilledInternalContract_RoundTripsValueTypesAndEndpoints() {
        // pack-3b-3: a producer-filled `contract internal` body must print back as
        // hand-authored `contract internal` DSL that re-parses to the same types/endpoints.
        var source = DomainFactory.Create("billing", b => b
            .AddValueType("ChargeRequest",
                new Property("Amount", new DomainTypeReference("Number"), []),
                new Property("Currency", new DomainTypeReference("Text"), []))
            .AddEntity("Ledger")
            .AddActionWithParameters("Ledger", "Charge",
                new Property("request", new DomainTypeReference("ChargeRequest"), [])));

        var parent = Apply("""
            domain Parent
            Billing: contract internal billing v1 {}
            """);
        var filled = new DomainSuite([source, parent]).FillInternalContracts(parent);

        var printed = new DomainDslPrinter().Print(filled);
        await Assert.That(printed.Contains("Billing: contract internal billing v1", StringComparison.Ordinal)).IsTrue();

        var reparsed = Apply(printed);
        var contract = reparsed.ImportedContracts.Single(c => c.Name == "Billing");

        await Assert.That(contract.SourceKind).IsEqualTo(ContractSourceKind.InternalDomain);
        await Assert.That(contract.SourceIdentifier).IsEqualTo("billing");
        await Assert.That(contract.Version).IsEqualTo("v1");

        await Assert.That(contract.Types.Select(t => t.Name)).Contains("ChargeRequest");
        var request = contract.Types.Single(t => t.Name == "ChargeRequest");
        await Assert.That(request.Properties.Count).IsEqualTo(2);
        await Assert.That(request.Properties.Select(p => p.Name)).Contains("Amount");
        await Assert.That(request.Properties.Select(p => p.Name)).Contains("Currency");

        await Assert.That(contract.Endpoints.Select(e => e.Name)).Contains("Charge");
        var charge = contract.Endpoints.Single(e => e.Name == "Charge");
        await Assert.That(charge.Kind).IsEqualTo(ContractEndpointKind.Operation);
        await Assert.That(charge.Direction).IsEqualTo(ContractEndpointDirection.Outbound);
        await Assert.That(charge.PayloadType.TypeName).IsEqualTo("ChargeRequest");
    }
}