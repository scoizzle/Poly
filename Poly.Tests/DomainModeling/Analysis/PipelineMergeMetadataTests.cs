using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Queries;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// Tests that <see cref="DomainModelAnalyzer.Analyze"/> (the domain analysis pipeline)
/// now produces topology, aggregate, and behavior metadata — without running the DslCompiler.
/// These passes were moved from the infra pipeline to the domain pipeline (APM.A2).
/// </summary>
public class PipelineMergeMetadataTests {
    private static Domain ParseDomain(string poly) {
        var ctx = ExtensionCatalog.Core.Authoring;
        var parser = new PolyDslParser(poly, ctx);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException("Domain evolution failed: " +
                string.Join("; ", result.Analysis.Diagnostics.Where(d =>
                    d.Severity == DiagnosticSeverity.Error).Select(d => d.Message)));
        return result.Root!;
    }

    [Test]
    public async Task DomainAnalysis_ProducesEffectTopologyMetadata() {
        // EffectTopologyMetadata requires at least one cross-entity effect
        var domain = ParseDomain("""
            domain Test
            Patron: entity {
              Name: Text
              loans: many Loan
            }
            Loan: entity {
              Amount: Number
              borrower: Patron
            }
            """);
        var analysis = DomainModelAnalyzer.Analyze(domain);
        var topology = analysis.GetMetadata<EffectTopologyMetadata>(domain);
        await Assert.That(topology).IsNotNull();
    }

    [Test]
    public async Task DomainAnalysis_ProducesOwnershipAggregateMetadata_WithRootAndChild() {
        // Aggregate metadata should identify Patron as root and Loan as child
        var domain = ParseDomain("""
            domain Test
            Patron: entity {
              Name: Text
              loans: many Loan
            }
            Loan: entity {
              Amount: Number
              borrower: Patron
            }
            """);
        var analysis = DomainModelAnalyzer.Analyze(domain);
        var aggregate = analysis.GetMetadata<OwnershipAggregateMetadata>(domain);
        await Assert.That(aggregate).IsNotNull();

        var patronAgg = aggregate!.Aggregate.Entities.First(e => e.Name == "Patron");
        await Assert.That(patronAgg.IsRoot).IsTrue();

        var loanAgg = aggregate.Aggregate.Entities.First(e => e.Name == "Loan");
        await Assert.That(loanAgg.IsRoot).IsFalse();
        await Assert.That(loanAgg.AggregateParentName).IsEqualTo("Patron");
    }

    [Test]
    public async Task DomainAnalysis_ProducesBehaviorMetadata_WithActionDetails() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              Name: Text
              Activate: action { }
            }
            """);
        var analysis = DomainModelAnalyzer.Analyze(domain);
        var behavior = BehaviorMetadata.From(domain, analysis);
        await Assert.That(behavior.Entities).IsNotEmpty();

        var itemBeh = behavior.Entities.First(e => e.Name == "Item");
        await Assert.That(itemBeh.Actions.Count).IsGreaterThanOrEqualTo(1);
        await Assert.That(itemBeh.Actions.Any(a => a.Name == "Activate")).IsTrue();
    }

    [Test]
    public async Task DomainAnalysis_ProducesAllThreeMetadata_OnSingleAnalysis() {
        // Proves all three metadata types come from a single DomainModelAnalyzer.Analyze call
        var domain = ParseDomain("""
            domain Test
            Customer: entity {
              Name: Text
              orders: many Order
            }
            Order: entity {
              Total: Number
              customer: Customer
              Submit: action { }
            }
            """);
        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.GetMetadata<EffectTopologyMetadata>(domain)).IsNotNull();
        await Assert.That(analysis.GetMetadata<OwnershipAggregateMetadata>(domain)).IsNotNull();
        await Assert.That(BehaviorMetadata.From(domain, analysis).Entities).IsNotEmpty();
    }

    // ── Phase B diagnostic tests (B′.1) ─────────────────────────

    [Test]
    public async Task DomainAnalysis_EntityWithNoResolvableOwner_ResolvesToRoot() {
        // A singular outbound plus an outbound collection, with no inbound collection:
        // nothing owns Loan, so Loan is a root. It used to stay a non-root with no
        // parent (DMAGG001, since removed) — a state with no key and no HTTP endpoints.
        var domain = ParseDomain("""
            domain Test
            Patron: entity { Name: Text }
            Note: entity { Body: Text }
            Loan: entity {
              Amount: Number
              borrower: Patron
              notes: many Note
            }
            """);
        var analysis = DomainModelAnalyzer.Analyze(domain);
        var aggregate = analysis.GetMetadata<OwnershipAggregateMetadata>(domain)!.Aggregate;
        var loan = aggregate.Entities.First(e => e.Name == "Loan");
        await Assert.That(loan.IsRoot).IsTrue();
        await Assert.That(loan.AggregateParentName).IsNull();
        await Assert.That(aggregate.Entities.Where(e => !e.IsRoot)
            .All(e => e.AggregateParentName is not null)).IsTrue();
    }

    [Test]
    public async Task DomainAnalysis_SingularOutboundOnly_IsRoot() {
        var domain = ParseDomain("""
            domain Test
            Patron: entity { Name: Text }
            Loan: entity {
              Amount: Number
              borrower: Patron
            }
            """);
        var analysis = DomainModelAnalyzer.Analyze(domain);
        var aggregate = analysis.GetMetadata<OwnershipAggregateMetadata>(domain)!.Aggregate;
        await Assert.That(aggregate.Entities.First(e => e.Name == "Loan").IsRoot).IsTrue();
    }

    [Test]
    public async Task DomainAnalysis_NormalHierarchy_ChildHasItsParent() {
        var domain = ParseDomain("""
            domain Test
            Patron: entity {
              Name: Text
              loans: many Loan
            }
            Loan: entity {
              Amount: Number
              borrower: Patron
            }
            """);
        var analysis = DomainModelAnalyzer.Analyze(domain);
        var aggregate = analysis.GetMetadata<OwnershipAggregateMetadata>(domain)!.Aggregate;
        await Assert.That(aggregate.Entities.First(e => e.Name == "Patron").IsRoot).IsTrue();
        var loan = aggregate.Entities.First(e => e.Name == "Loan");
        await Assert.That(loan.IsRoot).IsFalse();
        await Assert.That(loan.AggregateParentName).IsEqualTo("Patron");
    }

    [Test]
    public async Task AnalysisSummary_KeepsEveryMessage_AnchoredToItsElement() {
        // T1: the summary used to stop at ten errors+warnings and every message lost its
        // code and element path, so the 11th consequence of a change vanished and the
        // rest were unlinkable to the element they came from.
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              Score: Number range(0, 10) default(10)
              Bump01: action { assign Score to Score + 1 }
              Bump02: action { assign Score to Score + 1 }
              Bump03: action { assign Score to Score + 1 }
              Bump04: action { assign Score to Score + 1 }
              Bump05: action { assign Score to Score + 1 }
              Bump06: action { assign Score to Score + 1 }
              Bump07: action { assign Score to Score + 1 }
              Bump08: action { assign Score to Score + 1 }
              Bump09: action { assign Score to Score + 1 }
              Bump10: action { assign Score to Score + 1 }
              Bump11: action { assign Score to Score + 1 }
              Bump12: action { assign Score to Score + 1 }
            }
            """);
        var analysis = DomainModelAnalyzer.Analyze(domain);

        var summary = DomainQueries.GetAnalysisSummary(analysis, domain);

        var warnings = summary.Findings!.Where(f => f.Severity == "Warning").ToList();
        await Assert.That(warnings.Count).IsGreaterThanOrEqualTo(12);
        await Assert.That(summary.Messages.Count).IsGreaterThanOrEqualTo(12);
        await Assert.That(warnings.All(f => f.Code is not null)).IsTrue();
        await Assert.That(warnings.All(f => f.ElementPath.StartsWith("Test/Item", StringComparison.Ordinal))).IsTrue();
        await Assert.That(summary.Messages.All(m => m.Contains("Test/Item", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task DomainAnalysis_ThreeEntityCycle_EmitsDMDEP001() {
        // DMDEP001: real smell cycle A→B→C→A (not a pure inverse navigation pair)
        var domain = ParseDomain("""
            domain Test
            A: entity {
              name: Text
              b: B
            }
            B: entity {
              name: Text
              c: C
            }
            C: entity {
              name: Text
              a: A
            }
            """);
        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.DependencyCycle)).IsTrue();
    }

    [Test]
    public async Task DomainAnalysis_NoCycle_OnBidirectionalNavigations() {
        // Pure inverse pair (root collection + child back-ref) is intentional — not DMDEP001
        var domain = ParseDomain("""
            domain Test
            Patron: entity {
              Name: Text
              loans: many Loan
            }
            Loan: entity {
              Amount: Number
              borrower: Patron
            }
            """);
        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.DependencyCycle)).IsFalse();
    }

    [Test]
    public async Task DomainAnalysis_NoCycle_OnOneWayRelationship() {
        var domain = ParseDomain("""
            domain Test
            Patron: entity { Name: Text }
            Loan: entity {
              Amount: Number
              borrower: Patron
            }
            """);
        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.DependencyCycle)).IsFalse();
    }

    [Test]
    public async Task DomainAnalysis_UnguardedActionWithPolicies_EmitsDMBEH001() {
        // DMBEH001: action with no require gates and no parameters, when entity has policies elsewhere
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              Name: Text
              IsActive: Boolean
              ActivePolicy: policy { IsActive is true }
              Submit: action { }
            }
            """);
        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.UnconditionalAction)).IsTrue();
    }

    [Test]
    public async Task DomainAnalysis_NoDMBEH001_WhenActionHasGuard() {
        // Guarded action should not emit DMBEH001
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              Name: Text
              IsActive: Boolean
              ActivePolicy: policy { IsActive is true }
              Submit: action require ActivePolicy { }
            }
            """);
        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.UnconditionalAction)).IsFalse();
    }

    [Test]
    public async Task DomainAnalysis_NoDMBEH001_WhenEntityHasNoPolicies() {
        // When entity has zero policies, unconditional action is likely intentional — no hint
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              Name: Text
              Submit: action { }
            }
            """);
        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.UnconditionalAction)).IsFalse();
    }

    [Test]
    public async Task DomainAnalysis_StageUnguardedActionWithPolicies_EmitsDMBEH001() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              Name: Text
              IsActive: Boolean
              ActivePolicy: policy { IsActive is true }
              Active: stage {
                Submit: action { }
              }
            }
            """);
        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.UnconditionalAction)).IsTrue();
    }

    // ── G3: StoragePass noise on structural failure ─────────────

    [Test]
    public async Task DomainAnalysis_StructuralFailure_StoragePassNoiseAbsent() {
        // G3: When a structural failure exists (e.g. duplicate entity name),
        // StoragePass must NOT emit its "requires EffectTopology and OwnershipAggregate"
        // error — it should short-circuit via the HasErrors guard.
        var analysis = DomainModelAnalyzer.Analyze(DomainTestFactory.Create("Test", [
            new Entity("Item", [], [], [], []),
            new Entity("Item", [], [], [], []),
        ], []));

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Message.Contains("StoragePass requires"))).IsFalse();

        // The real error should be present
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Message.Contains("Duplicate"))).IsTrue();
    }

    // ── Pipeline integrity: every pass produced expected output ──

    [Test]
    public async Task DomainAnalysis_AllPasses_ProduceExpectedMetadata() {
        // Every pass in UseDomainModelAnalysisPipeline must produce its expected
        // metadata on a representative domain. This catches passes that silently
        // skip, exception early, or misorder dependencies.
        var domain = ParseDomain("""
            domain Test
            uses persistence
            Profile: entity { City: Text }
            Customer: entity {
              Name: Text required
              Email: Text unique
              Status: Text default("Active")
              profile: owned Profile
              GoodStanding: policy { Status is "Active" }
              Active: stage {
                Submit: action { transition to Done }
              }
              Done: stage { }
            }
            Loan: entity {
              Amount: Number
              customer: Customer
              Active: stage { }
            }
            """);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        // Telemetry — all passes must have run (count matches UseDomainModelAnalysisPipeline)
        // 20 passes registered (includes RequiredPropertiesPass + EffectFactsPass)
        await Assert.That(analysis.Telemetry.Passes.Count).IsGreaterThanOrEqualTo(20);
        await Assert.That(analysis.Telemetry.Passes.All(p => p.Elapsed > TimeSpan.Zero)).IsTrue();

        // ── Metadata produced by each pass ──

        // StructuralDomainAnalyzer — diagnostics only (no metadata)
        var structuralError = analysis.Diagnostics.Any(d => d.Code is "DMSTR001" or "DMSTR002" or "DMSTR003");
        await Assert.That(structuralError).IsFalse();

        // DomainCatalogPass — DomainTypeLookupMetadata alias + resolved type refs
        await Assert.That(analysis.GetMetadata<DomainTypeLookupMetadata>(default)).IsNotNull();

        // RequiredPropertiesPass — RequiredPropertiesMetadata (per entity with policies)
        var customerEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Customer");
        await Assert.That(analysis.GetMetadata<RequiredPropertiesMetadata>(customerEntity)).IsNotNull();

        // PolicyConstraintAnalyzer — diagnostics only (policy reference integrity)
        // EffectFactsPass — ResolvedRelationshipTargetMetadata on create-in (when present)
        // EffectAnalyzer — diagnostics only (effect binding/validation)

        // ConstraintQualityAnalyzer — diagnostics only

        // CapabilityAnalyzer — internal metadata (ActionCapabilityMetadata, etc.)
        // tested via downstream pass consumption

        // ConstraintPropagationAnalyzer — DownstreamConstraintsMetadata (internal)

        // RuleCoverageAnalyzer — diagnostics only

        // ContractIntegrationAnalyzer — diagnostics only

        // EntityStructureAnalyzer — EntityStructureMetadata per entity
        var customer = domain.Types.OfType<Entity>().First(e => e.Name == "Customer");
        var profile = domain.Types.OfType<Entity>().First(e => e.Name == "Profile");
        var loan = domain.Types.OfType<Entity>().First(e => e.Name == "Loan");
        var customerStructure = analysis.GetMetadata<EntityStructureMetadata>(customer);
        await Assert.That(customerStructure).IsNotNull();
        await Assert.That(analysis.GetMetadata<EntityStructureMetadata>(profile)).IsNotNull();
        await Assert.That(analysis.GetMetadata<EntityStructureMetadata>(loan)).IsNotNull();

        // Stage lookup metadata is available for stage-bearing entities
        await Assert.That(customerStructure!.HasStages).IsTrue();
        await Assert.That(customerStructure.StageByName).IsNotNull();
        await Assert.That(customerStructure.StageByName!.ContainsKey("Active")).IsTrue();

        // SubscriptionAnalyzer — diagnostics only

        // EffectTopologyPass — EffectTopologyMetadata
        await Assert.That(analysis.GetMetadata<EffectTopologyMetadata>(domain)).IsNotNull();

        // OwnershipAggregatePass — OwnershipAggregateMetadata
        await Assert.That(analysis.GetMetadata<OwnershipAggregateMetadata>(domain)).IsNotNull();

        // Behavior — projected from capability (not a pipeline pass)
        await Assert.That(BehaviorMetadata.From(domain, analysis).Entities).IsNotEmpty();

        // CrossReferencePass — cycle diagnostic only (no published metadata)

        // StoragePass — StorageMappingMetadata
        await Assert.That(analysis.GetMetadata<StorageMappingMetadata>(domain)).IsNotNull();

        // AuthoringSuggestionAnalyzer — diagnostics only (DMAS001 hints)

        // DomainCatalogPass — sole name→member catalog
        var catalog = analysis.GetMetadata<DomainCatalogMetadata>(domain);
        await Assert.That(catalog).IsNotNull();
        await Assert.That(catalog!.Index).IsNotNull();
        await Assert.That(catalog.Types).IsNotNull();
        await Assert.That(catalog.Relationships).IsNotNull();
        await Assert.That(catalog.ActionsByEntityName.ContainsKey("Customer")).IsTrue();
        await Assert.That(analysis.GetMetadata<MutationTargetIndexMetadata>(domain)).IsNull();
        await Assert.That(analysis.GetMetadata<ActionResolutionMetadata>(customer)).IsNull();

        // Entity/program Syntax projection is export-time only (DomainProgramProjection /
        // DomainToCSharpExporter) — analyze alone must not require mid-pipeline IR bags.
    }
}