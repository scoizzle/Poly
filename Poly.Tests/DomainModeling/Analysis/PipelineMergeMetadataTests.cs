using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Parsing;
using Poly.DslCompiler;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// Tests that <see cref="DomainModelAnalyzer.Analyze"/> (the domain analysis pipeline)
/// now produces topology, aggregate, and behavior metadata — without running the DslCompiler.
/// These passes were moved from the infra pipeline to the domain pipeline (APM.A2).
/// </summary>
public class PipelineMergeMetadataTests {
    private static Domain ParseDomain(string poly) {
        var ctx = DomainInputBuilder.CreateWithSqlPack().Build();
        var parser = new PolyDslParser(poly, ctx.Parser);
        var changes = parser.Parse();
        var emptyDomain = new Domain("_", [], []);
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
        var behavior = analysis.GetMetadata<BehaviorMetadata>(domain);
        await Assert.That(behavior).IsNotNull();

        var itemBeh = behavior!.Behavior.Entities.First(e => e.Name == "Item");
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
        await Assert.That(analysis.GetMetadata<BehaviorMetadata>(domain)).IsNotNull();
    }

    // ── Phase B diagnostic tests (B′.1) ─────────────────────────

    [Test]
    public async Task DomainAnalysis_OrphanEntity_EmitsDMAGG001() {
        // DMAGG001: non-root entity with no aggregate parent
        // Loan has borrower→Patron but Patron has no loans: many → only incoming rel means Loan is non-root
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
            d.Code == DomainModelDiagnosticCodes.AggregateOrphan)).IsTrue();
    }

    [Test]
    public async Task DomainAnalysis_NoOrphan_OnNormalHierarchy() {
        // Should not fire DMAGG001 when Loan has proper parent relationship
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
            d.Code == DomainModelDiagnosticCodes.AggregateOrphan)).IsFalse();
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
        // error — it should short-circuit via HasStructuralFailure guard.
        var analysis = DomainModelAnalyzer.Analyze(new Domain("Test", [
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
            Profile: entity { City: Text }
            Customer: entity {
              Name: Text required
              Email: Text unique
              Status: Text default(Active)
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

        // SemanticDomainAnalyzer — DomainTypeLookupMetadata, ResolvedTypeReferenceMetadata
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

        // BehaviorPass — BehaviorMetadata
        await Assert.That(analysis.GetMetadata<BehaviorMetadata>(domain)).IsNotNull();

        // CrossReferencePass — cycle diagnostic only (no published metadata)

        // StoragePass — StorageMappingMetadata
        await Assert.That(analysis.GetMetadata<StorageMappingMetadata>(domain)).IsNotNull();

        // TransportPass — TransportMetadata
        await Assert.That(analysis.GetMetadata<TransportMetadata>(domain)).IsNotNull();

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