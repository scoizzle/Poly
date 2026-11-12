using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Queries;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// Tests for stage-subscription IR, analyzers, and evolution changes.
/// </summary>
public class SubscriptionAnalysisTests {
    private static Entity MakeEntity(string name, params Stage[] stages) =>
        new(name, [], [], [], stages);

    private static Stage MakeStage(string name, params StageSubscription[] subs) =>
        new(name, [], [], [], []) { Subscriptions = subs };

    /// <summary>
    /// Parses a DSL snippet to a domain (p4-1/p4-2: DSL-authored quantifiers
    /// must flow through the same analysis path as IR fixtures).
    /// </summary>
    private static Domain ParseDomain(string poly) {
        var ctx = ExtensionCatalog.Core.Authoring;
        var parser = new PolyDslParser(poly, ctx);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).ApplyAndAnalyze(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException("Domain evolution failed: " +
                string.Join("; ", result.Analysis.Diagnostics.Where(d =>
                    d.Severity == DiagnosticSeverity.Error).Select(d => d.Message)));
        return result.Root!;
    }

    // ── A′.1 RemoveStageSubscription semantic key matching ─────

    [Test]
    public async Task RemoveStageSubscription_ByReconstructedKey_RemovesSubscription() {
        var rel = new Relationship("Notifies",
            new DomainTypeReference("Order"), new DomainTypeReference("Customer"),
            RelationshipCardinality.OneToOne, []);
        var sub = new StageSubscription("Notifies", ["Active"], StageSubscriptionQuantifier.Each, []);
        var stage = MakeStage("Pending", sub);
        var entity = MakeEntity("Order", stage);
        var customer = MakeEntity("Customer");
        var domain = ValidDomain.Create("Test", [entity, customer], [rel]);

        var result = new DomainEvolution(domain).ApplyAndAnalyze([
            new RemoveStageSubscriptionChange("Order", "Pending",
                new StageSubscription("Notifies", ["Active"], StageSubscriptionQuantifier.Each, []))
        ]);

        await Assert.That(result.Succeeded).IsTrue();
        var updated = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Order");
        var updatedStage = updated.Stages.Single(s => s.Name == "Pending");
        await Assert.That(updatedStage.Subscriptions).IsEmpty();
    }

    [Test]
    public async Task RemoveStageSubscription_WhenNoMatch_FailsLoud() {
        var stage = MakeStage("Pending");
        var entity = MakeEntity("Order", stage);
        var domain = ValidDomain.Create("Test", [entity], []);

        // Remove a subscription that doesn't exist — should fail-loud per fail-loud convention
        var result = new DomainEvolution(domain).ApplyAndAnalyze([
            new RemoveStageSubscriptionChange("Order", "Pending",
                new StageSubscription("NonExistentRel", ["Active"], StageSubscriptionQuantifier.Each, []))
        ]);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.WasRolledBack).IsTrue();
        await Assert.That(result.MutationErrors.Any(e => e.Contains("nothing to remove"))).IsTrue();
    }

    // ── A′.2 SubscriptionAnalyzer real checks ──────────

    [Test]
    public async Task Analyze_UnknownRelationshipName_ReportsDMSS003() {
        var sub = new StageSubscription("NonExistentRel", ["Active"], StageSubscriptionQuantifier.Each, []);
        var stage = MakeStage("Pending", sub);
        var entity = MakeEntity("Order", stage);
        // No relationship at all
        var domain = ValidDomain.Create("Test", [entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionContractMismatch)).IsTrue();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Message.Contains("NonExistentRel"))).IsTrue();
    }

    [Test]
    public async Task Analyze_UnknownTargetStageName_ReportsDMSS003() {
        var rel = new Relationship("Notifies",
            new DomainTypeReference("Order"), new DomainTypeReference("Customer"),
            RelationshipCardinality.OneToOne, []);
        var target = MakeEntity("Customer");
        // Subscription targets stage "Active" on Customer, but Customer has no stages
        var sub = new StageSubscription("Notifies", ["Active"], StageSubscriptionQuantifier.Each, []);
        var stage = MakeStage("Pending", sub);
        var entity = MakeEntity("Order", stage);
        var domain = ValidDomain.Create("Test", [entity, target], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionContractMismatch)).IsTrue();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Message.Contains("Active") && d.Message.Contains("Customer"))).IsTrue();
    }

    [Test]
    public async Task Analyze_ValidSubscription_NoContractErrors() {
        var rel = new Relationship("Notifies",
            new DomainTypeReference("Order"), new DomainTypeReference("Customer"),
            RelationshipCardinality.OneToOne, []);
        var targetStage = new Stage("Active", [], [], [], []);
        var target = MakeEntity("Customer", targetStage);
        var sub = new StageSubscription("Notifies", ["Active"], StageSubscriptionQuantifier.Each, []);
        var stage = MakeStage("Pending", sub);
        var entity = MakeEntity("Order", stage);
        var domain = ValidDomain.Create("Test", [entity, target], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionContractMismatch)).IsFalse();
    }

    [Test]
    public async Task Analyze_MultipleTargetStages_ValidatesAll() {
        var rel = new Relationship("Notifies",
            new DomainTypeReference("Order"), new DomainTypeReference("Customer"),
            RelationshipCardinality.OneToOne, []);
        var targetStage1 = new Stage("Active", [], [], [], []);
        var targetStage2 = new Stage("Inactive", [], [], [], []);
        var target = MakeEntity("Customer", targetStage1, targetStage2);
        // One valid, one invalid
        var sub = new StageSubscription("Notifies", ["Active", "NonExistent"], StageSubscriptionQuantifier.Each, []);
        var stage = MakeStage("Pending", sub);
        var entity = MakeEntity("Order", stage);
        var domain = ValidDomain.Create("Test", [entity, target], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionContractMismatch)).IsTrue();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Message.Contains("NonExistent"))).IsTrue();
    }

    // ── A′.3 stage subscription happy-path ─────────────────────

    [Test]
    public async Task Stage_WithSubscription_WiresSubscriptions() {
        var stage = new Stage("Pending", [], [], [], []) {
            Subscriptions = [new StageSubscription("Notifies", ["Active"], StageSubscriptionQuantifier.Each, [])]
        };

        await Assert.That(stage.Subscriptions.Count).IsEqualTo(1);
        await Assert.That(stage.Subscriptions[0].RelationshipName).IsEqualTo("Notifies");
        await Assert.That(stage.Subscriptions[0].StageNames).IsEquivalentTo(new[] { "Active" });
    }

    [Test]
    public async Task Stage_WithSubscription_WithEffects_WiresEffects() {
        var effect = new AssignEffect(
            DomainExpression.Property("Status"),
            DomainExpression.Literal("Triggered"));
        var stage = new Stage("Pending", [], [], [], []) {
            Subscriptions = [new StageSubscription("Notifies", ["Active"], StageSubscriptionQuantifier.Each, [effect])]
        };

        await Assert.That(stage.Subscriptions.Count).IsEqualTo(1);
        await Assert.That(stage.Subscriptions[0].Effects.Count).IsEqualTo(1);
    }

    [Test]
    public async Task DomainEvolution_AddStageSubscription_ThenAnalyze_HappyPath() {
        var rel = new Relationship("Notifies",
            new DomainTypeReference("Order"), new DomainTypeReference("Customer"),
            RelationshipCardinality.OneToOne, []);
        var targetStage = new Stage("Active", [], [], [], []);
        var target = MakeEntity("Customer", targetStage);
        var stage = MakeStage("Pending");
        var entity = MakeEntity("Order", stage);
        var domain = ValidDomain.Create("Test", [entity, target], [rel]);

        // Add a subscription via evolution
        var sub = new StageSubscription("Notifies", ["Active"], StageSubscriptionQuantifier.Each, []);
        var result = new DomainEvolution(domain).ApplyAndAnalyze([
            new AddStageSubscriptionChange("Order", "Pending", sub)
        ]);

        await Assert.That(result.Succeeded).IsTrue();

        // Analyze the evolved domain
        var analysis = DomainModelAnalyzer.Analyze(result.Root!);
        await Assert.That(analysis.HasErrors).IsFalse();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionContractMismatch)).IsFalse();
    }

    // ── A′.4 CausalityAnalyzer quality ─────────────────────────

    [Test]
    public async Task CausalityAnalyzer_MutualSubscription_ReportsCycle() {
        // Entity A subscribes to Entity B's stage, Entity B subscribes to Entity A's stage.
        // Both entities have actions that transition to the watched stage, creating a real cycle.
        var relAB = new Relationship("Paired",
            new DomainTypeReference("EntityA"), new DomainTypeReference("EntityB"),
            RelationshipCardinality.OneToOne, []);
        var relBA = new Relationship("PairedReverse",
            new DomainTypeReference("EntityB"), new DomainTypeReference("EntityA"),
            RelationshipCardinality.OneToOne, []);

        var activateA = new Poly.DomainModeling.Ontology.Action("Activate", InvocationResult.Void,
            [], [new StageTransitionEffect(new StageReference("Active"))], []);
        var stageA = new Stage("Active", [activateA], [], [], []) {
            Subscriptions = [
                new StageSubscription("Paired", ["Active"], StageSubscriptionQuantifier.Each, [])
            ]
        };
        var entityA = new Entity("EntityA", [], [activateA], [], [stageA]);

        var activateB = new Poly.DomainModeling.Ontology.Action("Activate", InvocationResult.Void,
            [], [new StageTransitionEffect(new StageReference("Active"))], []);
        var stageB = new Stage("Active", [activateB], [], [], []) {
            Subscriptions = [
                new StageSubscription("PairedReverse", ["Active"], StageSubscriptionQuantifier.Each, [])
            ]
        };
        var entityB = new Entity("EntityB", [], [activateB], [], [stageB]);

        var domain = ValidDomain.Create("Test", [entityA, entityB], [relAB, relBA]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionCausalityCycle)).IsTrue();
    }

    // ── A′′.2 Query subscription visibility ────────────────────

    [Test]
    public async Task Query_StageSubscription_AppearsInEntityDetail() {
        var rel = new Relationship("Notifies",
            new DomainTypeReference("Order"), new DomainTypeReference("Customer"),
            RelationshipCardinality.OneToOne, []);
        var targetStage = new Stage("Active", [], [], [], []);
        var target = MakeEntity("Customer", targetStage);
        var sub = new StageSubscription("Notifies", ["Active"], StageSubscriptionQuantifier.Each, []);
        var stage = MakeStage("Pending", sub);
        var entity = MakeEntity("Order", stage);
        var domain = ValidDomain.Create("Test", [entity, target], [rel]);

        var detail = DomainQueries.GetEntity(domain, "Order");

        await Assert.That(detail).IsNotNull();
        await Assert.That(detail!.Stages.Count).IsGreaterThan(0);
        var pending = detail.Stages.First(s => s.Name == "Pending");
        await Assert.That(pending.Subscriptions.Count).IsEqualTo(1);
        await Assert.That(pending.Subscriptions[0].RelationshipName).IsEqualTo("Notifies");
        await Assert.That(pending.Subscriptions[0].StageNames).IsEquivalentTo(new[] { "Active" });
        await Assert.That(pending.Subscriptions[0].Quantifier).IsEqualTo("Each");
        await Assert.That(pending.Subscriptions[0].EffectCount).IsEqualTo(0);
    }

    // ── A′′.3 Duplicate subscription key detection ──────────────

    [Test]
    public async Task Analyze_DuplicateSubscriptionKeys_ReportsWarning() {
        var rel = new Relationship("Notifies",
            new DomainTypeReference("Order"), new DomainTypeReference("Customer"),
            RelationshipCardinality.OneToOne, []);
        var targetStage = new Stage("Active", [], [], [], []);
        var target = MakeEntity("Customer", targetStage);
        var sub1 = new StageSubscription("Notifies", ["Active"], StageSubscriptionQuantifier.Each, []);
        var sub2 = new StageSubscription("Notifies", ["Active"], StageSubscriptionQuantifier.Each, []);
        var stage = MakeStage("Pending", sub1, sub2);
        var entity = MakeEntity("Order", stage);
        var domain = ValidDomain.Create("Test", [entity, target], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionDuplicateHandler)).IsTrue();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Message.Contains("both run", StringComparison.OrdinalIgnoreCase))).IsTrue();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Message.Contains("Duplicate") && d.Message.Contains("Notifies"))).IsTrue();
    }

    // ── A′′.4 Quantifier vs cardinality check ───────────────────

    [Test]
    public async Task Analyze_AnyQuantifierOnOneToOne_ReportsError() {
        var rel = new Relationship("Paired",
            new DomainTypeReference("Order"), new DomainTypeReference("Customer"),
            RelationshipCardinality.OneToOne, []);
        var targetStage = new Stage("Active", [], [], [], []);
        var target = MakeEntity("Customer", targetStage);
        // "Any" quantifier on a singular relationship is meaningless — reject (fail-closed).
        var sub = new StageSubscription("Paired", ["Active"], StageSubscriptionQuantifier.Any, []);
        var stage = MakeStage("Pending", sub);
        var entity = MakeEntity("Order", stage);
        var domain = ValidDomain.Create("Test", [entity, target], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionContractMismatch &&
            d.Severity == DiagnosticSeverity.Error)).IsTrue();
    }

    // ── p4-2: DSL-authored quantifiers vs cardinality (fail closed) ──

    [Test]
    public async Task Evolve_DslWhenAnyOnOneToOne_FailsClosed() {
        // p4-1 + review F5: `when any` on a OneToOne nav is meaningless — evolution
        // must fail closed (DMSS003), not apply with a warning.
        var ex = Assert.Throws<InvalidOperationException>(() => ParseDomain("""
            domain Test

            Tracker: entity {
              Tracks: one Order
              Pending: stage {
                when any Tracks Active {
                }
              }
            }

            Order: entity {
              Draft: stage { }
              Active: stage { }
            }
            """));
        await Assert.That(ex.Message).Contains("one-to-one");
        await Assert.That(ex.Message).Contains("Any");
    }

    [Test]
    public async Task Analyze_DslWhenAllOnOneToMany_NoWarning() {
        // p4-1: `when all` on a OneToMany nav is legal — no quantifier warning.
        var domain = ParseDomain("""
            domain Test

            Patron: entity {
              loans: many Loan
              when all loans Overdue {
              }
            }

            Loan: entity {
              Draft: stage { }
              Overdue: stage { }
            }
            """);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionContractMismatch &&
            d.Message.Contains("one-to-one", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task Analyze_DslWhenAnyOnOneToMany_NoWarning() {
        // p4-1: `when any` on a OneToMany nav is legal — no quantifier warning.
        var domain = ParseDomain("""
            domain Test

            Patron: entity {
              loans: many Loan
              when any loans Overdue {
              }
            }

            Loan: entity {
              Draft: stage { }
              Overdue: stage { }
            }
            """);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionContractMismatch &&
            d.Message.Contains("one-to-one", StringComparison.Ordinal))).IsFalse();
    }

    // ── Slice B′: End-to-end subscription runtime loop ────────

    [Test]
    public async Task InvokeAction_StageTransitionEffect_FiresSubscriptionOnRelatedInstance() {
        // Domain: Tracker ──Tracks──► Order. Subscriber (Tracker) is relationship SOURCE.
        // Tracker has a Pending stage with subscription: when Tracks Active { assign Status to "Triggered" }.
        // Order is just the target entity that transitions.
        var statusProp = new Property("Status", new DomainTypeReference("Text"), [new OptionalConstraint()]);
        var tracker = new Entity("Tracker", [statusProp], [], [], [
            new Stage("Pending", [], [], [], []) {
                Subscriptions = [
                    new StageSubscription("Tracks", ["Active"], StageSubscriptionQuantifier.Each, [
                        new AssignEffect(
                            DomainExpression.Property("Status"),
                            DomainExpression.Literal("Triggered"))
                    ])
                ]
            }
        ]);

        var orderAction = new Poly.DomainModeling.Ontology.Action("Activate", InvocationResult.Void, [], [
            new StageTransitionEffect(new StageReference("Active"))
        ], []);
        var order = new Entity("Order", [], [orderAction], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);

        var rel = new Relationship("Tracks",
            new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToOne, []);

        var domain = ValidDomain.Create("Test", [tracker, order], [rel]);
        var exec = ModuleExec.For(domain);

        // Verify analysis is clean
        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionContractMismatch)).IsFalse();

        // Create instances
        var orderInstance = exec.Create((order).Name, new Dictionary<string, object?>()).Instance!;
        var trackerInstance = exec.Create((tracker).Name, new Dictionary<string, object?>()).Instance!;
        exec.Link("Tracks", trackerInstance, orderInstance);

        // Order starts in Draft, then Activate → StageTransition → Active
        exec.Invoke(orderInstance, "Activate");

        await Assert.That((string)trackerInstance.Bag["Status"]!).IsEqualTo("Triggered");
    }

    [Test]
    public async Task InvokeAction_StageChange_SubscriptionDoesNotFireWhenSubscriberInWrongStage() {
        // Tracker starts in Idle (first stage = default). Subscription on Pending.
        // When Order transitions to Active, tracker subscription should NOT fire
        // because tracker is in Idle, not Pending.
        var tracker = new Entity("Tracker", [
            new Property("Status", new DomainTypeReference("Text"), [])
        ], [], [], [
            new Stage("Idle", [], [], [], []),  // Default — no subscription
            new Stage("Pending", [], [], [], []) {
                Subscriptions = [
                    new StageSubscription("Tracks", ["Active"], StageSubscriptionQuantifier.Each, [
                        new AssignEffect(
                            DomainExpression.Property("Status"),
                            DomainExpression.Literal("Triggered"))
                    ])
                ]
            }
        ]);

        var orderAction = new Poly.DomainModeling.Ontology.Action("Activate", InvocationResult.Void, [], [
            new StageTransitionEffect(new StageReference("Active"))
        ], []);
        var order = new Entity("Order", [], [orderAction], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);

        var rel = new Relationship("Tracks",
            new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToOne, []);

        var domain = ValidDomain.Create("Test", [tracker, order], [rel]);
        var exec = ModuleExec.For(domain);

        var orderInstance = exec.Create((order).Name, new Dictionary<string, object?>()).Instance!;
        // Tracker starts in "Idle" — NOT "Pending" (the stage that has the subscription)
        var trackerInstance = exec.Create((tracker).Name, new Dictionary<string, object?> { ["Status"] = "Untouched" }).Instance!;
        // Link present so the only reason subscription must not fire is wrong stage
        exec.Link("Tracks", trackerInstance, orderInstance);

        // Tracker is in Idle — subscription is on Pending, should NOT fire
        exec.Invoke(orderInstance, "Activate");

        await Assert.That((string)trackerInstance.Bag["Status"]!).IsEqualTo("Untouched");
    }

    [Test]
    public async Task Analyze_WhenAllOnStagelessTarget_ReportsError() {
        // Round-5 F4: `when all Rel Stage` where the target has NO stages must be
        // rejected at analysis (the watched stage cannot exist) — otherwise the export
        // gate would emit a nonexistent CurrentStage/stage-enum reference (CS1061).
        var ctx = ExtensionCatalog.Core.Authoring;
        var parser = new PolyDslParser("""
            domain Test
            Task: entity { Flag: Text }
            Project: entity {
              tasks: many Task
              when all tasks Done { }
            }
            """, ctx);
        var changes = parser.Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Analysis.Diagnostics.Any(d =>
            d.Message.Contains("stage 'Done'") && d.Message.Contains("does not exist"))).IsTrue();
    }

    // ── S-05: DMSS006 duplicate handlers ────────────────────────

    private const string DupStageScoped = """
        domain Test

        Parcel: entity {
          Pending: stage { Move: action { transition to Moved } }
          Moved: stage { }
          Shipped: stage { }
        }

        Hub2: entity {
          Note: Text optional
          parcels: many Parcel
          Watching: stage {
            when parcels Moved { assign Note to "a" }
            when parcels Moved { assign Note to "b" }
          }
        }
        """;

    private const string DupEntityLevel = """
        domain Test

        Order: entity {
          Open: stage { Fulfil: action { transition to Fulfilled } }
          Fulfilled: stage { }
        }

        Customer: entity {
          Note: Text optional
          orders: many Order
          when orders Fulfilled { assign Note to "a" }
          when orders Fulfilled { assign Note to "b" }
        }
        """;

    private static Entity EntityOf(Domain domain, string name) =>
        domain.Types.OfType<Entity>().Single(e => e.Name == name);

    [Test]
    public async Task DuplicateWhen_EntityAndStageLevel_WarnsBothRunAndNeverSaysRemoveAll() {
        var stageDomain = ParseDomain(DupStageScoped);
        var stageDiags = DomainModelAnalyzer.Analyze(stageDomain).Diagnostics
            .Where(d => d.Code == DomainModelDiagnosticCodes.SubscriptionDuplicateHandler).ToList();
        var watching = EntityOf(stageDomain, "Hub2").Stages.Single(s => s.Name == "Watching");

        await Assert.That(stageDiags.Count).IsEqualTo(1);
        await Assert.That(stageDiags[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(ReferenceEquals(stageDiags[0].Node, watching.Subscriptions[1])).IsTrue();
        foreach (var part in new[] { "Duplicate", "parcels", "Moved", "Watching" })
            await Assert.That(stageDiags[0].Message).Contains(part);
        await Assert.That(stageDiags[0].Message).Contains("both run", StringComparison.OrdinalIgnoreCase);
        await Assert.That(stageDiags[0].Message).DoesNotContain("Remove-all");
        await Assert.That(stageDiags[0].Message).DoesNotContain("RemoveAll");
        await Assert.That(DomainModelAnalyzer.Analyze(stageDomain).Diagnostics
            .Count(d => d.Severity == DiagnosticSeverity.Error)).IsEqualTo(0);

        var entityDomain = ParseDomain(DupEntityLevel);
        var entityDiags = DomainModelAnalyzer.Analyze(entityDomain).Diagnostics
            .Where(d => d.Code == DomainModelDiagnosticCodes.SubscriptionDuplicateHandler).ToList();
        var customer = EntityOf(entityDomain, "Customer");

        await Assert.That(entityDiags.Count).IsEqualTo(1);
        await Assert.That(entityDiags[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(ReferenceEquals(entityDiags[0].Node, customer.Subscriptions[1])).IsTrue();
        foreach (var part in new[] { "Duplicate", "orders", "Fulfilled" })
            await Assert.That(entityDiags[0].Message).Contains(part);
        await Assert.That(entityDiags[0].Message).Contains("both run", StringComparison.OrdinalIgnoreCase);
        await Assert.That(entityDiags[0].Message).DoesNotContain("Remove-all");
        await Assert.That(entityDiags[0].Message).DoesNotContain("RemoveAll");
        await Assert.That(DomainModelAnalyzer.Analyze(entityDomain).Diagnostics
            .Count(d => d.Severity == DiagnosticSeverity.Error)).IsEqualTo(0);
    }

    [Test]
    public async Task ThreeDuplicateWhens_WarnOnce() {
        var domain = ParseDomain("""
            domain Test

            Parcel: entity {
              Pending: stage { Move: action { transition to Moved } }
              Moved: stage { }
            }

            Hub2: entity {
              Note: Text optional
              parcels: many Parcel
              Watching: stage {
                when parcels Moved { assign Note to "a" }
                when parcels Moved { assign Note to "b" }
                when parcels Moved { assign Note to "c" }
              }
            }
            """);
        var diags = DomainModelAnalyzer.Analyze(domain).Diagnostics
            .Where(d => d.Code == DomainModelDiagnosticCodes.SubscriptionDuplicateHandler).ToList();

        await Assert.That(diags.Count).IsEqualTo(1);
        await Assert.That(diags[0].Message).Contains("3");
    }

    [Test]
    public async Task DuplicateWhensInDifferentStageOrder_WarnOnce() {
        var domain = ParseDomain("""
            domain Test

            Parcel: entity {
              Pending: stage { Move: action { transition to Moved } Ship: action { transition to Shipped } }
              Moved: stage { }
              Shipped: stage { }
            }

            Hub2: entity {
              Note: Text optional
              parcels: many Parcel
              Watching: stage {
                when parcels Moved, Shipped { assign Note to "a" }
                when parcels Shipped, Moved { assign Note to "b" }
              }
            }
            """);
        var diags = DomainModelAnalyzer.Analyze(domain).Diagnostics
            .Where(d => d.Code == DomainModelDiagnosticCodes.SubscriptionDuplicateHandler).ToList();

        await Assert.That(diags.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SameKey_EntityLevelAndStageScoped_NoDMSS006() {
        // Q11 orders entity-level before stage-scoped, which presupposes the same key at both
        // placements (the NotifyOrder fixture in GuideClaimsTests is this shape).
        var domain = ParseDomain("""
            domain Test

            Parcel: entity {
              Pending: stage { Move: action { transition to Moved } }
              Moved: stage { }
            }

            Hub2: entity {
              Note: Text optional
              parcels: many Parcel
              Watching: stage {
                when parcels Moved { assign Note to "stage" }
              }
              when parcels Moved { assign Note to "entity" }
            }
            """);

        await Assert.That(DomainModelAnalyzer.Analyze(domain).Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionDuplicateHandler)).IsFalse();
    }

    [Test]
    public async Task StageScopedSameKey_InTwoDifferentStages_NoDMSS006() {
        var domain = ParseDomain("""
            domain Test

            Parcel: entity {
              Pending: stage { Move: action { transition to Moved } }
              Moved: stage { }
            }

            Hub2: entity {
              Note: Text optional
              parcels: many Parcel
              Watching: stage {
                when parcels Moved { assign Note to "a" }
              }
              Idle: stage {
                when parcels Moved { assign Note to "b" }
              }
            }
            """);

        await Assert.That(DomainModelAnalyzer.Analyze(domain).Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionDuplicateHandler)).IsFalse();
    }

    private const string TraceParcel = """
        Parcel: entity {
          Code: Text
          Pending: stage { Move: action { transition to Moved } }
          Moved: stage { }
        }
        """;

    private static async Task<string> RunTraceAsync(string hubBody) {
        var domain = ParseDomain("domain Test\n" + TraceParcel + "\n" + hubBody);
        var exec = ModuleExec.For(domain);
        var hub = exec.Create("Hub2", new Dictionary<string, object?>()).Instance!;
        await Assert.That(exec.Invoke(hub, "Add", new Dictionary<string, object?> { ["code"] = "P" }).Succeeded).IsTrue();
        var parcel = exec.LinkedTargets("parcels", hub).Single();
        await Assert.That(exec.Invoke(parcel, "Move").Succeeded).IsTrue();
        return Convert.ToString(hub.Bag["Trace"], System.Globalization.CultureInfo.InvariantCulture)!;
    }

    [Test]
    public async Task DuplicateWhens_SameKey_BothRunInDeclarationOrder() {
        // Runtime pin for the DMSS006 message: both handlers run, in declaration order.
        var trace = await RunTraceAsync("""
            Hub2: entity {
              Trace: Number default(0)
              parcels: many Parcel
              Add: action (code: Text) { create in parcels { Code: code } }
              when parcels Moved { assign Trace to Trace * 10 + 1 }
              when parcels Moved { assign Trace to Trace * 10 + 2 }
            }
            """);

        await Assert.That(trace).IsEqualTo("12");
    }

    [Test]
    public async Task SameKey_StageScopedAndEntityLevel_BothRun_EntityFirst() {
        var trace = await RunTraceAsync("""
            Hub2: entity {
              Trace: Number default(0)
              parcels: many Parcel
              Open: stage {
                Add: action (code: Text) { create in parcels { Code: code } }
                when parcels Moved { assign Trace to Trace * 10 + 2 }
              }
              when parcels Moved { assign Trace to Trace * 10 + 1 }
            }
            """);

        await Assert.That(trace).IsEqualTo("12");
    }

    // ── S-05: DMSS007 never-entered stages ──────────────────────

    private static IReadOnlyList<Diagnostic> NeverEntered(Domain domain) =>
        DomainModelAnalyzer.Analyze(domain).Diagnostics
            .Where(d => d.Code == DomainModelDiagnosticCodes.SubscriptionToNeverEnteredStage).ToList();

    private static string ShipmentWatch(string shipmentBody, string orderBody, string shipmentHeader = "") => $$"""
        domain Test

        Shipment: entity{{shipmentHeader}} {
        {{shipmentBody}}
        }

        Order: entity {
          Note: Text optional
          shipments: many Shipment
        {{orderBody}}
        }
        """;

    [Test]
    public async Task SubscriptionToNeverEnteredStage_Warns() {
        var domain = ParseDomain(ShipmentWatch(
            """
              Packed: stage { Ship: action { transition to Delivered } }
              Delivered: stage { }
              Phantom: stage { }
            """,
            """  when shipments Phantom { assign Note to "x" }"""));
        var diags = NeverEntered(domain);

        await Assert.That(diags.Count).IsEqualTo(1);
        await Assert.That(diags[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(ReferenceEquals(diags[0].Node, EntityOf(domain, "Order").Subscriptions[0])).IsTrue();
        foreach (var part in new[] { "Phantom", "never", "Shipment" })
            await Assert.That(diags[0].Message).Contains(part);
        await Assert.That(diags[0].Message).DoesNotContain("nreachable");
        await Assert.That(DomainModelAnalyzer.Analyze(domain).Diagnostics
            .Count(d => d.Severity == DiagnosticSeverity.Error)).IsEqualTo(0);
    }

    [Test]
    public async Task SubscriptionToNeverEnteredStage_MessageDeniesInitialStage() {
        var domain = ParseDomain(ShipmentWatch(
            """
              Packed: stage { Ship: action { transition to Delivered } }
              Delivered: stage { }
              Phantom: stage { }
            """,
            """  when shipments Phantom { assign Note to "x" }"""));
        var diag = NeverEntered(domain).Single();

        await Assert.That(diag.Message).Contains("is not the initial stage");
        await Assert.That(diag.Message).DoesNotContain("it is the initial stage");
    }

    [Test]
    public async Task SubscriptionToDeadAndLiveStage_WarnsOnlyForDeadStage_WithoutClaimingHandlerDead() {
        var domain = ParseDomain(ShipmentWatch(
            """
              Packed: stage { Ship: action { transition to Delivered } }
              Delivered: stage { }
              Phantom: stage { }
            """,
            """  when shipments Phantom, Delivered { assign Note to "x" }"""));
        var diags = NeverEntered(domain);

        await Assert.That(diags.Count).IsEqualTo(1);
        await Assert.That(diags[0].Message).Contains("can never fire for 'Phantom'");
        await Assert.That(diags[0].Message).DoesNotContain("'Delivered'");
        await Assert.That(diags[0].Message).DoesNotContain("this handler can never fire");
    }

    [Test]
    public async Task SubscriptionToTwoNeverEnteredStages_WarnsOnce() {
        var domain = ParseDomain(ShipmentWatch(
            """
              Packed: stage { Ship: action { transition to Delivered } }
              Delivered: stage { }
              Phantom: stage { }
              Ghost: stage { }
            """,
            """  when shipments Phantom, Ghost { assign Note to "x" }"""));
        var diags = NeverEntered(domain);

        await Assert.That(diags.Count).IsEqualTo(1);
        await Assert.That(diags[0].Message).Contains("Phantom");
        await Assert.That(diags[0].Message).Contains("Ghost");
    }

    [Test]
    public async Task SubscriptionToInitialStage_IsClean() {
        var domain = ParseDomain(ShipmentWatch(
            """
              Packed: stage { Ship: action { transition to Delivered } }
              Other: stage { }
              Delivered: stage { }
            """,
            """  when shipments Other { assign Note to "x" }""",
            " initial(Other)"));

        await Assert.That(NeverEntered(domain)).IsEmpty();
    }

    [Test]
    public async Task SubscriptionToStageEnteredOnlyFromEntryHop_IsClean() {
        var domain = ParseDomain(ShipmentWatch(
            """
              A: stage { entry { transition to B } }
              B: stage { }
            """,
            """  when shipments B { assign Note to "x" }"""));

        await Assert.That(NeverEntered(domain)).IsEmpty();
    }

    [Test]
    public async Task SubscriptionToStageEnteredOnlyFromExitHop_IsClean() {
        var domain = ParseDomain(ShipmentWatch(
            """
              A: stage { exit { transition to B } Go: action { transition to B } }
              B: stage { }
            """,
            """  when shipments B { assign Note to "x" }"""));

        await Assert.That(NeverEntered(domain)).IsEmpty();
    }

    [Test]
    public async Task SubscriptionToStageEnteredOnlyByEntityLevelAction_IsClean() {
        var domain = ParseDomain(ShipmentWatch(
            """
              Packed: stage { }
              Delivered: stage { }
              Deliver: action { transition to Delivered }
            """,
            """  when shipments Delivered { assign Note to "x" }"""));

        await Assert.That(NeverEntered(domain)).IsEmpty();
    }

    [Test]
    public async Task SubscriptionToStageEnteredOnlyBySubscriptionEffect_IsClean() {
        var domain = ParseDomain("""
            domain Test

            Part: entity {
              Open: stage { Close: action { transition to Closed } }
              Closed: stage { }
            }

            Shipment: entity {
              parts: many Part
              Packed: stage { }
              Delivered: stage { }
              when parts Closed { transition to Delivered }
            }

            Order: entity {
              Note: Text optional
              shipments: many Shipment
              when shipments Delivered { assign Note to "x" }
            }
            """);

        await Assert.That(NeverEntered(domain)).IsEmpty();
    }

    [Test]
    public async Task SubscriptionToStageEnteredOnlyInNestedThenHop_IsClean() {
        var domain = ParseDomain(ShipmentWatch(
            """
              Flag: Number default(0)
              Packed: stage { Ship: action { if (Flag > 0) { transition to Delivered } } }
              Delivered: stage { }
            """,
            """  when shipments Delivered { assign Note to "x" }"""));

        await Assert.That(NeverEntered(domain)).IsEmpty();
    }

    [Test]
    public async Task SubscriptionToStageEnteredOnlyInNestedElseHop_IsClean() {
        var domain = ParseDomain(ShipmentWatch(
            """
              Flag: Number default(0)
              Packed: stage { Ship: action { if (Flag > 0) { assign Flag to 0 } else { transition to Delivered } } }
              Delivered: stage { }
            """,
            """  when shipments Delivered { assign Note to "x" }"""));

        await Assert.That(NeverEntered(domain)).IsEmpty();
    }

    [Test]
    public async Task SubscriptionToMissingStage_IsDMSS003Only_NoDMSS007() {
        var domain = ValidDomain.Create("Test", [
            MakeEntity("Order", MakeStage("Open", new StageSubscription("Notifies", ["Nowhere"], StageSubscriptionQuantifier.Each, []))),
            MakeEntity("Customer", new Stage("Active", [], [], [], []))
        ], [new Relationship("Notifies", new DomainTypeReference("Order"), new DomainTypeReference("Customer"),
            RelationshipCardinality.OneToOne, [])]);
        var diags = DomainModelAnalyzer.Analyze(domain).Diagnostics;

        await Assert.That(diags.Any(d => d.Code == DomainModelDiagnosticCodes.SubscriptionContractMismatch)).IsTrue();
        await Assert.That(diags.Any(d => d.Code == DomainModelDiagnosticCodes.SubscriptionToNeverEnteredStage)).IsFalse();
    }

    [Test]
    public async Task SubscriptionToStagelessTarget_IsDMSS003Only_NoDMSS007() {
        var domain = ValidDomain.Create("Test", [
            MakeEntity("Order", MakeStage("Open", new StageSubscription("Notifies", ["Done"], StageSubscriptionQuantifier.Each, []))),
            MakeEntity("Customer")
        ], [new Relationship("Notifies", new DomainTypeReference("Order"), new DomainTypeReference("Customer"),
            RelationshipCardinality.OneToOne, [])]);
        var diags = DomainModelAnalyzer.Analyze(domain).Diagnostics;

        await Assert.That(diags.Any(d => d.Code == DomainModelDiagnosticCodes.SubscriptionContractMismatch)).IsTrue();
        await Assert.That(diags.Any(d => d.Code == DomainModelDiagnosticCodes.SubscriptionToNeverEnteredStage)).IsFalse();
    }
}