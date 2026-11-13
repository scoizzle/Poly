using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// Ownership aggregate truth: every resolved non-root has exactly one parent, a
/// parentless entity is a root, and the canonical back-reference pair
/// (<c>Owner.children: many Child</c> + <c>Child.owner: Owner</c>) does not build a
/// two-node parent cycle.
/// </summary>
public class OwnershipAggregateAnalysisTests {
    private static (Domain Domain, AnalysisResult Analysis) Analyze(string dsl) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(dsl).Parse());
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        var domain = result.Root!;
        return (domain, DomainModelAnalyzer.Analyze(domain));
    }

    private static AggregateModel Aggregate(AnalysisResult analysis, Domain domain) =>
        analysis.GetMetadata<OwnershipAggregateMetadata>(domain)!.Aggregate;

    private static AggregateEntity Entity(AggregateModel aggregate, string name) =>
        aggregate.Entities.First(e => e.Name == name);

    /// <summary>
    /// The invariant every consumer relies on: a parentless entity is a root, so no
    /// entity can be a non-root without a parent (that state had no key and no HTTP
    /// endpoints), and following parents always terminates at a root.
    /// </summary>
    private static async Task AssertParentInvariant(AggregateModel aggregate) {
        foreach (var entity in aggregate.Entities) {
            if (!entity.IsRoot)
                await Assert.That(entity.AggregateParentName).IsNotNull();
            var cursor = entity;
            var seen = new HashSet<string>(StringComparer.Ordinal) { entity.Name };
            while (cursor?.AggregateParentName is not null) {
                await Assert.That(seen.Add(cursor.AggregateParentName)).IsTrue();
                cursor = Entity(aggregate, cursor.AggregateParentName);
            }
            await Assert.That(cursor!.IsRoot).IsTrue();
        }
    }

    [Test]
    public async Task Grandchild_ResolvesToItsParent() {
        var (domain, analysis) = Analyze("""
            domain Test
            Customer: entity {
              Name: Text
              orders: many Order
            }
            Order: entity {
              Total: Number
              customer: Customer
              items: many OrderItem
            }
            OrderItem: entity {
              Sku: Text
              order: Order
            }
            """);

        var aggregate = Aggregate(analysis, domain);
        var item = Entity(aggregate, "OrderItem");
        await Assert.That(item.IsRoot).IsFalse();
        await Assert.That(item.AggregateParentName).IsEqualTo("Order");
        await Assert.That(item.ParentRelationshipName).IsEqualTo("items");
        await Assert.That(Entity(aggregate, "Customer").IsRoot).IsTrue();
        await Assert.That(Entity(aggregate, "Order").AggregateParentName).IsEqualTo("Customer");
        await AssertParentInvariant(aggregate);
    }

    [Test]
    public async Task Grandchild_DeclaredBeforeParent_Resolves() {
        var (domain, analysis) = Analyze("""
            domain Test
            OrderItem: entity {
              Sku: Text
              order: Order
            }
            Order: entity {
              Total: Number
              customer: Customer
              items: many OrderItem
            }
            Customer: entity {
              Name: Text
              orders: many Order
            }
            """);

        var aggregate = Aggregate(analysis, domain);
        var item = Entity(aggregate, "OrderItem");
        await Assert.That(item.AggregateParentName).IsEqualTo("Order");
        await Assert.That(item.ParentRelationshipName).IsEqualTo("items");
        await AssertParentInvariant(aggregate);
    }

    [Test]
    public async Task BackReferencePair_NoParentCycle() {
        // S1b: Member.bookings + Booking.member is one edge declared twice. Taking the
        // inbound singular as a parent made Member.parent = Booking and
        // Booking.parent = Member — a cycle that corrupted every consumer of the
        // aggregate (facts, route nesting, keys).
        var (domain, analysis) = Analyze("""
            domain Test
            Plan: entity {
              Code: Text
            }
            Member: entity {
              Name: Text
              plan: Plan
              bookings: many Booking
            }
            Booking: entity {
              Amount: Number
              member: Member
            }
            """);

        var aggregate = Aggregate(analysis, domain);
        await Assert.That(Entity(aggregate, "Plan").IsRoot).IsTrue();
        await Assert.That(Entity(aggregate, "Plan").AggregateParentName).IsNull();
        var booking = Entity(aggregate, "Booking");
        await Assert.That(booking.IsRoot).IsFalse();
        await Assert.That(booking.AggregateParentName).IsEqualTo("Member");
        await Assert.That(booking.ParentRelationshipName).IsEqualTo("bookings");
        await Assert.That(booking.BackReferencePropertyName).IsEqualTo("member");
        // Nothing owns Member: the plain reference to Plan is not ownership (that would
        // make Member a child of the entity it references).
        await Assert.That(Entity(aggregate, "Member").AggregateParentName).IsNull();
        await AssertParentInvariant(aggregate);
    }

    [Test]
    public async Task ReferenceOnlyShape_IsARoot() {
        // A plain singular reference whose target owns nothing (and is owned by nobody)
        // leaves the referrer parentless. It is a root — not a non-root with no key and
        // no HTTP endpoints, which is what the old pass produced (silently).
        var (domain, analysis) = Analyze("""
            domain Test
            Account: entity {
              Name: Text
            }
            Lead: entity {
              Name: Text
              account: Account
            }
            """);

        var aggregate = Aggregate(analysis, domain);
        await Assert.That(Entity(aggregate, "Lead").IsRoot).IsTrue();
        await Assert.That(Entity(aggregate, "Lead").AggregateParentName).IsNull();
        await Assert.That(Entity(aggregate, "Account").IsRoot).IsTrue();
        await AssertParentInvariant(aggregate);
    }

    [Test]
    public async Task OwnedTarget_IsNotRoot_OwnerStaysRoot() {
        // S17/S18: `address: owned Address` is composition — Address is a child of
        // Customer, and Customer stays a root (it was mis-parented off the reverse edge
        // and lost its HTTP endpoints).
        var (domain, analysis) = Analyze("""
            domain Test
            Customer: entity {
              Name: Text
              address: owned Address
              orders: many Order
            }
            Address: entity {
              Line1: Text
            }
            Order: entity {
              Total: Number
              customer: Customer
            }
            """);

        var aggregate = Aggregate(analysis, domain);
        await Assert.That(Entity(aggregate, "Customer").IsRoot).IsTrue();
        var address = Entity(aggregate, "Address");
        await Assert.That(address.IsRoot).IsFalse();
        await Assert.That(address.AggregateParentName).IsEqualTo("Customer");
        await Assert.That(Entity(aggregate, "Order").AggregateParentName).IsEqualTo("Customer");
        await AssertParentInvariant(aggregate);
    }

    [Test]
    public async Task SingularChain_IsAcyclic() {
        // Plain singulars are references. Ownership is inferred from an inbound singular
        // (the 1:1 pair convention), so *which* of the two chain links is the child is a
        // resolution detail — what is guaranteed is that the chain cannot close a loop and
        // that a parentless entity is a root.
        var (domain, analysis) = Analyze("""
            domain Test
            Plan: entity { Name: Text }
            Account: entity {
              Name: Text
              subscription: Subscription
            }
            Subscription: entity {
              Name: Text
              plan: Plan
            }
            """);

        var aggregate = Aggregate(analysis, domain);
        var account = Entity(aggregate, "Account");
        var subscription = Entity(aggregate, "Subscription");
        await Assert.That(Entity(aggregate, "Plan").IsRoot).IsTrue();
        await Assert.That(
            account.AggregateParentName == "Subscription" && subscription.AggregateParentName == "Account")
            .IsFalse();
        await AssertParentInvariant(aggregate);
    }

    [Test]
    public async Task TwoNodeMutualReference_ResolvesWithoutACycle() {
        // The pathological shape: each entity's only ownership signal points at the
        // other. One may take the other as its parent; they must not both.
        var (domain, analysis) = Analyze("""
            domain Test
            A: entity {
              Name: Text
              b: B
            }
            B: entity {
              Name: Text
              a: A
            }
            """);

        var aggregate = Aggregate(analysis, domain);
        var a = Entity(aggregate, "A");
        var b = Entity(aggregate, "B");
        await Assert.That(a.AggregateParentName == "B" && b.AggregateParentName == "A").IsFalse();
        await AssertParentInvariant(aggregate);
    }

    private static (Domain? Root, AnalysisResult Analysis) Run(string dsl) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(dsl).Parse());
        return result.Root is { } root && result.Succeeded
            ? (root, DomainModelAnalyzer.Analyze(root))
            : (result.Root, result.Analysis);
    }

    private static IReadOnlyList<Diagnostic> AllErrors(AnalysisResult analysis) =>
        analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

    private static IReadOnlyList<Diagnostic> Errors(AnalysisResult analysis, string code) =>
        analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && d.Code == code).ToList();

    private static IReadOnlyList<OwnedEdge> Edges(AnalysisResult analysis, Domain domain) =>
        analysis.GetMetadata<OwnershipAggregateMetadata>(domain)!.OwnedEdges;

    [Test]
    public async Task OwnedEdges_ListEveryOwnedNavigationInDeclarationOrder() {
        var (domain, analysis) = Analyze("""
            domain Test
            Customer: entity {
              Name: Text
              profile: owned Profile
              orders: many owned Order
            }
            Profile: entity { Bio: Text }
            Order: entity {
              Total: Number
              lines: many owned Line
            }
            Line: entity { Qty: Number }
            """);

        var edges = Edges(analysis, domain);
        await Assert.That(edges.Count).IsEqualTo(3);
        await Assert.That(edges[0]).IsEqualTo(new OwnedEdge("Customer", "profile", "Profile", RelationshipCardinality.OneToOne));
        await Assert.That(edges[1]).IsEqualTo(new OwnedEdge("Customer", "orders", "Order", RelationshipCardinality.OneToMany));
        await Assert.That(edges[2]).IsEqualTo(new OwnedEdge("Order", "lines", "Line", RelationshipCardinality.OneToMany));
    }

    [Test]
    public async Task OwnerEdgesOf_ReturnsTheOneOwnerEdge() {
        var (domain, analysis) = Analyze("""
            domain Test
            Customer: entity {
              Name: Text
              orders: many owned Order
            }
            Order: entity { Total: Number }
            """);

        var meta = analysis.GetMetadata<OwnershipAggregateMetadata>(domain)!;
        var owners = meta.OwnerEdgesOf("Order");
        await Assert.That(owners.Count).IsEqualTo(1);
        await Assert.That(owners[0].SourceEntity).IsEqualTo("Customer");
        await Assert.That(owners[0].NavName).IsEqualTo("orders");
        await Assert.That(meta.OwnerEdgesOf("Customer")).IsEmpty();
    }

    [Test]
    public async Task OwnedEdges_SelfOwnership_IsOneEdge() {
        var (domain, analysis) = Analyze("""
            domain Test
            Node: entity {
              Name: Text
              children: many owned Node
            }
            """);

        var edges = Edges(analysis, domain);
        await Assert.That(edges.Count).IsEqualTo(1);
        await Assert.That(edges[0]).IsEqualTo(new OwnedEdge("Node", "children", "Node", RelationshipCardinality.OneToMany));
    }

    /// <summary>Pin: green against the empty stub — a plain reference is not an owner edge.</summary>
    [Test]
    public async Task OwnedEdges_PlainReferencesAreNotEdges_Pin() {
        var (domain, analysis) = Analyze("""
            domain Test
            Order: entity {
              Total: Number
              customer: Customer
              lines: many Line
            }
            Customer: entity { Name: Text }
            Line: entity { Qty: Number }
            """);

        await Assert.That(Edges(analysis, domain)).IsEmpty();
    }

    /// <summary>Pin: green against the empty stub — a duplicated target is DMSTR001's, no edge, no cascade.</summary>
    [Test]
    public async Task OwnedEdges_DuplicatedTargetType_IsSkipped_Pin() {
        var (root, analysis) = Run("""
            domain DuplicatedTarget
            A: entity {
              x: owned B
            }
            B: entity {
              Name: Text
            }
            B: entity {
              y: A
            }
            """);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.StructuralDuplicate).Count).IsGreaterThan(0);
        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.OwnedTargetMultipleOwners)).IsEmpty();
        if (root is not null && analysis.GetMetadata<OwnershipAggregateMetadata>(root) is { } meta)
            await Assert.That(meta.OwnedEdges).IsEmpty();
    }

    private const string Dmstr010 = DomainModelDiagnosticCodes.OwnedTargetMultipleOwners;

    [Test]
    public async Task TwoOwnerNavigationsToOneTarget_IsDMSTR010() {
        var (_, analysis) = Run("""
            domain Test
            Customer: entity {
              Name: Text
              profile: owned Profile
            }
            Supplier: entity {
              Name: Text
              profile: owned Profile
            }
            Profile: entity { Bio: Text }
            """);

        var errors = Errors(analysis, Dmstr010);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(1);
        var nav = (Relationship)errors[0].Node;
        await Assert.That(nav.Name).IsEqualTo("profile");
        await Assert.That(nav.Source.TypeName).IsEqualTo("Supplier");
        await Assert.That(errors[0].Message).Contains("Profile");
        await Assert.That(errors[0].Message).Contains("Customer.profile");
        await Assert.That(errors[0].Message).Contains("Supplier.profile");
        await Assert.That(errors[0].Message).Contains("plain reference");
    }

    [Test]
    public async Task TwoOwnerNavigationsOnOneSource_IsDMSTR010() {
        var (_, analysis) = Run("""
            domain Test
            Peer: entity {
              Name: Text
              primary: owned Address
              secondary: owned Address
            }
            Address: entity { Line: Text }
            """);

        var errors = Errors(analysis, Dmstr010);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(1);
        await Assert.That(((Relationship)errors[0].Node).Name).IsEqualTo("secondary");
    }

    [Test]
    public async Task ThreeOwners_ReportOncePerTargetNamingAll() {
        var (_, analysis) = Run("""
            domain Test
            A: entity { x: owned T }
            B: entity { x: owned T }
            C: entity { x: owned T }
            T: entity { Name: Text }
            """);

        var errors = Errors(analysis, Dmstr010);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("A.x");
        await Assert.That(errors[0].Message).Contains("B.x");
        await Assert.That(errors[0].Message).Contains("C.x");
    }

    /// <summary>Pin: green on the stub — one owner plus a plain reference is clean.</summary>
    [Test]
    public async Task OneOwnerPlusPlainReference_IsClean_Pin() {
        var (_, analysis) = Run("""
            domain Test
            Customer: entity {
              Name: Text
              profile: owned Profile
            }
            Supplier: entity {
              Name: Text
              profile: Profile
            }
            Profile: entity { Bio: Text }
            """);

        await Assert.That(AllErrors(analysis)).IsEmpty();
    }

    /// <summary>Pin: green on the stub — two owned navs to different targets are clean.</summary>
    [Test]
    public async Task TwoOwnedNavsToDifferentTargets_IsClean_Pin() {
        var (_, analysis) = Run("""
            domain Test
            Order: entity {
              Total: Number
              header: owned Header
              line: owned Line
            }
            Header: entity { Name: Text }
            Line: entity { Qty: Number }
            """);

        await Assert.That(AllErrors(analysis)).IsEmpty();
    }

    /// <summary>Pin: green on the stub — a self-owning navigation is one edge (OQ-14).</summary>
    [Test]
    public async Task SelfOwning_IsClean_Pin() {
        var (_, analysis) = Run("""
            domain Test
            Node: entity {
              Name: Text
              children: many owned Node
            }
            """);

        await Assert.That(AllErrors(analysis)).IsEmpty();
    }

    /// <summary>Pin: green on the stub — a duplicated target is DMSTR001's alone.</summary>
    [Test]
    public async Task DuplicatedTarget_ReportsOnlyStructural_Pin() {
        var (_, analysis) = Run("""
            domain Test
            A: entity { x: owned B }
            C: entity { y: owned B }
            B: entity { Name: Text }
            B: entity { Other: Text }
            """);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.StructuralDuplicate).Count).IsGreaterThan(0);
        await Assert.That(Errors(analysis, Dmstr010)).IsEmpty();
    }

    // ── V-14 (#586): characterisation of master, before the ownership-cycle error ──────────────

    private static IReadOnlyList<Diagnostic> Warnings(AnalysisResult analysis) =>
        analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning).ToList();

    private const string MutuallyOwnedRepro = """
        domain OwnCycle
        A: entity {
          Name: Text
          b: owned B
        }
        B: entity {
          Name: Text
          a: owned A
        }
        """;

    [Test]
    public async Task MutuallyOwnedEntities_OnlyTheCycleError_NoWarning() {
        // Master (characterised in 3f9d12c4): no error and no warning, because a pure
        // inverse pair is dropped from CrossReferencePass's DMDEP001 graph. Now the one
        // error is the ownership cycle, and DMDEP001 still stays silent.
        var (_, analysis) = Run(MutuallyOwnedRepro);
        await Assert.That(string.Join(",", AllErrors(analysis).Select(static d => d.Code))).IsEqualTo("DMDEP002");
        await Assert.That(Warnings(analysis)).IsEmpty();
    }

    [Test]
    public async Task MutuallyOwnedEntities_AnalysisTerminates_AndReportsTheCycle() {
        // Master pinned the aggregate built on the repro (it terminates: the ResolvesTo guard
        // stops the two-node loop). A failed evolve keeps no aggregate to read back, so the
        // proof now is that the whole pipeline, which runs OwnershipAggregatePass, returns
        // and carries the cycle error: no pass loops on the cycle.
        var (_, analysis) = Run(MutuallyOwnedRepro);
        await Assert.That(Errors(analysis, Cycle).Count).IsEqualTo(1);
    }

    [Test]
    public async Task MutuallyOwnedEntities_FailAnalysis_BeforeLower() {
        // Master lowered and printed the repro (the issue: "the C# export ignores
        // ownership entirely for the pair"). Analysis now fails closed, so Lower is never
        // reached; the same shape with the cycle broken still lowers and prints.
        var session = ParitySessions.Create(ExtensionCatalog.ProductAuthoring);
        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(MutuallyOwnedRepro, session).Parse(), session: session);
        await Assert.That(evolved.Succeeded).IsFalse();

        var brokenSession = ParitySessions.Create(ExtensionCatalog.ProductAuthoring);
        var broken = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(MutuallyOwnedRepro.Replace("a: owned A", "a: A", StringComparison.Ordinal), brokenSession).Parse(),
                session: brokenSession);
        await Assert.That(broken.Succeeded).IsTrue();
        var cs = new CSharpGenerator().Generate(brokenSession.Lower(broken.Root!, broken.Analysis));
        await Assert.That(cs).Contains("class A");
        await Assert.That(cs).Contains("class B");
    }

    [Test]
    public async Task Master_NeighbourShapes_AnalyseClean_Pin() {
        // Shapes the V-14 rule must NOT move: an owned chain, and an owned navigation
        // with a plain inverse. (Self-owning is pinned above by SelfOwning_IsClean_Pin.)
        string[] clean = [
            """
            domain Chain
            A: entity {
              Name: Text
              b: owned B
            }
            B: entity {
              Name: Text
              c: owned C
            }
            C: entity {
              Name: Text
            }
            """,
            """
            domain OwnedWithPlainInverse
            A: entity {
              Name: Text
              b: owned B
            }
            B: entity {
              Name: Text
              a: A
            }
            """,
        ];
        foreach (var dsl in clean) {
            var (_, analysis) = Run(dsl);
            await Assert.That(AllErrors(analysis)).IsEmpty();
        }
    }

    [Test]
    public async Task ThreeEntityOwnedRing_OnlyTheCycleError() {
        // Master (characterised in 3f9d12c4) accepted the three-entity ring with no error.
        var (_, analysis) = Run("""
            domain Ring
            A: entity { b: owned B }
            B: entity { c: owned C }
            C: entity { a: owned A }
            """);
        await Assert.That(string.Join(",", AllErrors(analysis).Select(static d => d.Code))).IsEqualTo("DMDEP002");
    }

    // ── V-14 (#586, OQ-14): a multi-entity ownership cycle is an analysis error ───────────────

    private const string Cycle = DomainModelDiagnosticCodes.OwnershipCycle;

    private static Relationship CycleNav(Diagnostic diagnostic) => (Relationship)diagnostic.Node;

    [Test]
    public async Task MutuallyOwnedEntities_ReportOwnershipCycle() {
        var (_, analysis) = Run(MutuallyOwnedRepro);

        var errors = Errors(analysis, Cycle);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(1);
        // Positioned on the navigation that closes the cycle: the walk starts at the
        // earliest-declared member (A) and B.a is the owned edge that returns to it.
        var nav = CycleNav(errors[0]);
        await Assert.That(nav.Source.TypeName).IsEqualTo("B");
        await Assert.That(nav.Name).IsEqualTo("a");
        await Assert.That(errors[0].Message).Contains("A -> B -> A");
        await Assert.That(errors[0].Code).IsEqualTo("DMDEP002");
    }

    [Test]
    public async Task ThreeEntityOwnershipCycle_Reported() {
        var (_, analysis) = Run("""
            domain Ring
            A: entity { Name: Text
              b: owned B }
            B: entity { Name: Text
              c: owned C }
            C: entity { Name: Text
              a: owned A }
            """);

        var errors = Errors(analysis, Cycle);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("A -> B -> C -> A");
        await Assert.That(CycleNav(errors[0]).Source.TypeName).IsEqualTo("C");
        await Assert.That(CycleNav(errors[0]).Name).IsEqualTo("a");
    }

    [Test]
    public async Task MultiEntityCycle_AOwnsBOwnsA_StillError() {
        // Collections close the cycle just the same, and A owning itself (legal on its
        // own, OQ-14) does not excuse the A -> B -> A ring.
        var (_, analysis) = Run("""
            domain TreeInACycle
            A: entity {
              Name: Text
              children: many owned A
              bs: many owned B
            }
            B: entity {
              Name: Text
              owners: many owned A
            }
            """);

        var errors = Errors(analysis, Cycle);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("A -> B -> A");
        await Assert.That(errors[0].Message).DoesNotContain("A -> A");
    }

    [Test]
    public async Task OwnedChain_IsAccepted() {
        var (_, analysis) = Run("""
            domain Chain
            A: entity { b: owned B }
            B: entity { c: owned C }
            C: entity { Name: Text }
            """);
        await Assert.That(Errors(analysis, Cycle)).IsEmpty();
        await Assert.That(AllErrors(analysis)).IsEmpty();

        // Presence control: the same chain with C owning A closes the ring and reports.
        var (_, closed) = Run("""
            domain Chain
            A: entity { b: owned B }
            B: entity { c: owned C }
            C: entity { a: owned A }
            """);
        await Assert.That(Errors(closed, Cycle).Count).IsEqualTo(1);
    }

    [Test]
    public async Task SelfOwnedEntity_SingleTypeTree_IsAccepted() {
        var (_, analysis) = Run("""
            domain Tree
            A: entity {
              Name: Text
              children: many owned A
            }
            """);
        await Assert.That(Errors(analysis, Cycle)).IsEmpty();
        await Assert.That(AllErrors(analysis)).IsEmpty();

        // Presence control: the same tree whose node also owns B, which owns A back, reports.
        var (_, closed) = Run("""
            domain Tree
            A: entity {
              Name: Text
              children: many owned A
              b: owned B
            }
            B: entity { a: owned A }
            """);
        await Assert.That(Errors(closed, Cycle).Count).IsEqualTo(1);
    }

    [Test]
    public async Task OwnedNavigationWithPlainInverse_IsAccepted() {
        var (_, analysis) = Run("""
            domain PlainInverse
            A: entity {
              Name: Text
              b: owned B
            }
            B: entity {
              Name: Text
              a: A
            }
            """);
        await Assert.That(Errors(analysis, Cycle)).IsEmpty();
        await Assert.That(AllErrors(analysis)).IsEmpty();

        // Presence control: making the inverse owned too reports the cycle.
        var (_, closed) = Run(MutuallyOwnedRepro);
        await Assert.That(Errors(closed, Cycle).Count).IsEqualTo(1);
    }

    [Test]
    public async Task SelfEdgeInsideARealCycle_GivesOneError() {
        var (_, analysis) = Run("""
            domain SelfEdgeInCycle
            A: entity {
              Name: Text
              children: many owned A
              b: owned B
            }
            B: entity { a: owned A }
            """);

        var errors = Errors(analysis, Cycle);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("A -> B -> A");
    }

    [Test]
    public async Task TwoDisjointCycles_TwoErrors() {
        var (_, analysis) = Run("""
            domain TwoRings
            A: entity { b: owned B }
            B: entity { a: owned A }
            C: entity { d: owned D }
            D: entity { c: owned C }
            """);

        var errors = Errors(analysis, Cycle);
        await Assert.That(errors.Count).IsEqualTo(2);
        await Assert.That(errors.Any(e => e.Message.Contains("A -> B -> A"))).IsTrue();
        await Assert.That(errors.Any(e => e.Message.Contains("C -> D -> C"))).IsTrue();
    }

    [Test]
    public async Task Cycle_IsReportedFromItsEarliestDeclaredMember() {
        // A reaches the B <-> C ring through C. The ring is reported starting at B (the
        // earliest-declared member of the ring), closed by C.b — not as C -> B -> C.
        var (_, analysis) = Run("""
            domain RingBehindARoot
            A: entity { c: owned C }
            B: entity { c: owned C }
            C: entity { b: owned B }
            """);

        var errors = Errors(analysis, Cycle);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("B -> C -> B");
        await Assert.That(CycleNav(errors[0]).Source.TypeName).IsEqualTo("C");
        await Assert.That(CycleNav(errors[0]).Name).IsEqualTo("b");
    }

    [Test]
    public async Task CycleDeclaredInReverseOrder_StartsAtTheFirstDeclaredEntity() {
        var (_, analysis) = Run("""
            domain Reversed
            B: entity { a: owned A }
            A: entity { b: owned B }
            """);

        var errors = Errors(analysis, Cycle);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("B -> A -> B");
        await Assert.That(CycleNav(errors[0]).Source.TypeName).IsEqualTo("A");
    }

    [Test]
    public async Task ParallelOwnedNavsOnTheCycle_AreOneCycle() {
        // Two navigations from A to B are also DMSTR010; the entity ring is still one cycle.
        var (_, analysis) = Run("""
            domain Parallel
            A: entity {
              b1: owned B
              b2: owned B
            }
            B: entity { a: owned A }
            """);

        await Assert.That(Errors(analysis, Cycle).Count).IsEqualTo(1);
        await Assert.That(Errors(analysis, Dmstr010).Count).IsEqualTo(1);
    }

    [Test]
    public async Task ViaPairedBothOwned_ReportsTheInversePairingAndTheCycle() {
        // Pinned, not endorsed: a `via` pairing owned on both ends is DMREL004 (S-11/S-12)
        // and, being a mutual ownership ring, also DMDEP002 — one definition of ownership.
        var (_, analysis) = Run("""
            domain ViaBothOwned
            A: entity { b: owned B via a }
            B: entity { a: owned A via b }
            """);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.InverseContradictory).Count).IsEqualTo(1);
        await Assert.That(Errors(analysis, Cycle).Count).IsEqualTo(1);
    }
}