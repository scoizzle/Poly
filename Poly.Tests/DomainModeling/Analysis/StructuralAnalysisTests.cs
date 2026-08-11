using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Effects;
using Poly.Introspection;

namespace Poly.Tests.DomainModeling.Analysis;

public class StructuralAnalysisTests {
    [Test]
    public async Task StructuralDuplicate_DuplicateEntityNames_ReportsError() {
        var first = new Entity("Ticket", [], [], [], []);
        var second = new Entity("Ticket", [], [], [], []);
        var domain = DomainTestFactory.Create("Test", [first, second], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.StructuralDuplicate)).IsTrue();
    }

    // Entity inheritance was removed — no StructuralCycle test needed.

    [Test]
    public async Task StructuralOwnership_ManyToManyWithOwnership_ReportsError() {
        var text = new Poly.DomainModeling.PrimitiveType("Text", TypeCategory.Text, []);
        var source = new Entity("Source", [], [], [], []);
        var target = new Entity("Target", [], [], [], []);
        var rel = new Relationship("OwnsMany",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.ManyToMany, []) { SourceOwnsTarget = true };
        var domain = DomainTestFactory.Create("Test", [text, source, target], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.StructuralOwnership)).IsTrue();
    }

    [Test]
    public async Task StructuralOwnership_ManyToManyOwnedByTarget_ReportsError() {
        var text = new Poly.DomainModeling.PrimitiveType("Text", TypeCategory.Text, []);
        var source = new Entity("Source", [], [], [], []);
        var target = new Entity("Target", [], [], [], []);
        var rel = new Relationship("OwnedByMany",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.ManyToOne, []) { SourceOwnsTarget = true };
        var domain = DomainTestFactory.Create("Test", [text, source, target], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.StructuralOwnership)).IsTrue();
    }

    [Test]
    public async Task StructuralOwnership_OneToOneSourceOwned_IsValid() {
        var text = new Poly.DomainModeling.PrimitiveType("Text", TypeCategory.Text, []);
        var source = new Entity("Source", [], [], [], []);
        var target = new Entity("Target", [], [], [], []);
        var rel = new Relationship("OwnsOne",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.OneToOne, []) { SourceOwnsTarget = true };
        var domain = DomainTestFactory.Create("Test", [text, source, target], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.StructuralOwnership)).IsFalse();
    }
}

public class SemanticAnalysisTests {
    [Test]
    public async Task SemanticTypeCompatibility_DisallowedNullableCategory_ReportsError() {
        var nullable = new Poly.DomainModeling.PrimitiveType("NullableInt", TypeCategory.Nullable, []);
        var domain = DomainTestFactory.Create("Test", [nullable], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SemanticTypeCompatibility)).IsTrue();
    }

    [Test]
    public async Task SemanticTypeCompatibility_DisallowedCollectionCategory_ReportsError() {
        var collection = new Poly.DomainModeling.PrimitiveType("CollectionInt", TypeCategory.Collection, []);
        var domain = DomainTestFactory.Create("Test", [collection], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SemanticTypeCompatibility)).IsTrue();
    }



    [Test]
    public async Task SemanticConstraintMismatch_ChildEnumNotSubsetOfParent_ReportsError() {
        var parentProp = new Property("Status", new DomainTypeReference("Text"), [
            new EnumConstraint([
                new EnumConstraint.Member("Open"),
                new EnumConstraint.Member("Closed")
            ])
        ]);
        var childProp = new Property("Status", new DomainTypeReference("Text"), [
            new EnumConstraint([
                new EnumConstraint.Member("Open"),
                new EnumConstraint.Member("Closed"),
                new EnumConstraint.Member("Archived")
            ])
        ]);
        var parent = new Entity("BaseTicket", [parentProp], [], [], []);
        var child = new Entity("Ticket", [childProp], [], [], []);
        var text = new Poly.DomainModeling.PrimitiveType("Text", TypeCategory.Text, []);
        var domain = DomainTestFactory.Create("Test", [text, parent, child], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        // Inheritance was removed — parent-child constraint mismatch no longer applies.
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SemanticConstraintMismatch)).IsFalse();
    }

    [Test]
    public async Task SemanticReferenceResolution_UndefinedTypeReference_ReportsError() {
        var entity = new Entity("Ticket", [new Property("Title", new DomainTypeReference("UndefinedType"), [])], [], [], []);
        var domain = DomainTestFactory.Create("Test", [entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SemanticReferenceResolution)).IsTrue();
    }



    // Entity inheritance was removed — no parent-entity tests needed.
}

public class EffectBindingTests {
    [Test]
    public async Task EffectBinding_UnknownTypeInCreateEntityInstance_ReportsError() {
        var action = new Poly.DomainModeling.Action("Create", InvocationResult.Void, [], [
            new CreateEntityInstance(new DomainTypeReference("NonExistent"))
        ], []);
        var entity = new Entity("Maker", [], [action], [], []);
        var domain = DomainTestFactory.Create("Test", [entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectBinding)).IsTrue();
    }

    [Test]
    public async Task EffectBinding_InvokeActionUnknownRelationship_WithCatalog_ReportsError() {
        // amu-w1-1: relationship resolve is catalog-only (no domain.Relationships scan).
        // With analysis + catalog present, an unknown relationship name must be
        // reported (fail closed), not silently passed.
        var target = new Entity("Target", [], Actions: [
            new Poly.DomainModeling.Action("Tag", InvocationResult.Void, [], [], [])
        ], [], []);
        var source = new Entity("Source", [], Actions: [
            new Poly.DomainModeling.Action("Go", InvocationResult.Void, [], [
                new InvokeActionEffect("Tag", [], TargetRelationship: "NoSuchRel")
            ], [])
        ], [], []);
        var domain = DomainTestFactory.Create("Test", [source, target], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.GetCatalog(domain)).IsNotNull();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectBinding)).IsTrue();
    }



    [Test]
    public async Task EffectBinding_UnknownActionInInvoke_ReportsError() {
        var action = new Poly.DomainModeling.Action("DoIt", InvocationResult.Void, [], [
            new InvokeActionEffect("NonExistentAction", [])
        ], []);
        var entity = new Entity("Worker", [], [action], [], []);
        var domain = DomainTestFactory.Create("Test", [entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectBinding)).IsTrue();
    }

    [Test]
    public async Task EffectBinding_UnknownPropertyInAssign_ReportsError() {
        var text = new Poly.DomainModeling.PrimitiveType("Text", TypeCategory.Text, []);
        var action = new Poly.DomainModeling.Action("Assign", InvocationResult.Void, [], [
            new AssignEffect(new PropertyAccess("NonExistent"), new Literal("value"))
        ], []);
        var entity = new Entity("Ticket", [], [action], [], []);
        var domain = DomainTestFactory.Create("Test", [text, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectBinding)).IsTrue();
    }

    [Test]
    public async Task EffectBinding_UnknownStageInTransition_ReportsError() {
        var action = new Poly.DomainModeling.Action("Transition", InvocationResult.Void, [], [
            new StageTransitionEffect(new StageReference("NonExistentStage"))
        ], []);
        var entity = new Entity("Ticket", [], [action], [], []);
        var domain = DomainTestFactory.Create("Test", [entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectBinding)).IsTrue();
    }

    [Test]
    public async Task EffectBinding_UnknownRelationshipInLink_ReportsError() {
        var text = new Poly.DomainModeling.PrimitiveType("Text", TypeCategory.Text, []);
        var action = new Poly.DomainModeling.Action("Link", InvocationResult.Void, [], [
            new LinkRelationshipEffect("NonExistentRel", new PropertyAccess("Target"))
        ], []);
        var entity = new Entity("Source", [], [action], [], []);
        var domain = DomainTestFactory.Create("Test", [text, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectBinding)).IsTrue();
    }

    [Test]
    public async Task EffectBinding_CreateInUnknownRelationship_ReportsError() {
        // P2′.1: CreateIn effect with unknown relationship name.
        var action = new Poly.DomainModeling.Action("DoIt", InvocationResult.Void, [], [
            new CreateEntityInRelationshipEffect("NonExistentRel", [])
        ], []);
        var entity = new Entity("Maker", [], [action], [], []);
        var domain = DomainTestFactory.Create("Test", [entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectBinding)).IsTrue();
    }

    [Test]
    public async Task EffectBinding_CreateInWrongSourceEntity_ReportsError() {
        // P2′.4: CreateIn effect where the action's entity is not the relationship source.
        var order = new Entity("Order", [], [], [], []);
        var action = new Poly.DomainModeling.Action("DoIt", InvocationResult.Void, [], [
            new CreateEntityInRelationshipEffect("rel", [])
        ], []);
        // "Maker" is NOT the source of "rel" (Customer is)
        var maker = new Entity("Maker", [], [action], [], []);
        var customer = new Entity("Customer", [], [], [], []);
        var rel = new Relationship("rel",
            new DomainTypeReference("Customer"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToMany, []);
        var domain = DomainTestFactory.Create("Test", [maker, order, customer], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectBinding)).IsTrue();
    }

    [Test]
    public async Task EffectBinding_CreateInHappyPath_NoError() {
        // P2′.4: CreateIn on the correct source entity → no effect binding errors.
        var order = new Entity("Order", [], [], [], []);
        var action = new Poly.DomainModeling.Action("DoIt", InvocationResult.Void, [], [
            new CreateEntityInRelationshipEffect("rel", [])
        ], []);
        var customer = new Entity("Customer", [], [action], [], []);
        var rel = new Relationship("rel",
            new DomainTypeReference("Customer"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToMany, []);
        var domain = DomainTestFactory.Create("Test", [customer, order], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        // Should have NO effect binding errors
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectBinding)).IsFalse();
    }

    [Test]
    public async Task EffectBinding_CreateInUnknownInitializer_ReportsError() {
        // P2′.1: CreateIn initializer references unknown property on target entity.
        var order = new Entity("Order", [
            new Property("Name", new DomainTypeReference("Text"), [])
        ], [], [], []);
        var action = new Poly.DomainModeling.Action("DoIt", InvocationResult.Void, [], [
            new CreateEntityInRelationshipEffect("rel",
                [new PropertyBinding("NonExistentProp", new Literal("val"))])
        ], []);
        var customer = new Entity("Customer", [], [action], [], []);
        var rel = new Relationship("rel",
            new DomainTypeReference("Customer"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToMany, []);
        var domain = DomainTestFactory.Create("Test", [customer, order], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectBinding)).IsTrue();
    }

    [Test]
    public async Task EffectBinding_BareCreateExclusivelyOwned_ReportsError() {
        // P2′′.1: Bare create of exclusively-owned entity (only owned target, never source) → error
        var target = new Entity("Child", [], [], [], []);
        var action = new Poly.DomainModeling.Action("DoIt", InvocationResult.Void, [], [
            new CreateEntityInstance(new DomainTypeReference("Child"))
        ], []);
        var source = new Entity("Parent", [], [action], [], []);
        var rel = new Relationship("owns",
            new DomainTypeReference("Parent"), new DomainTypeReference("Child"),
            RelationshipCardinality.OneToOne, []) { SourceOwnsTarget = true };
        var domain = DomainTestFactory.Create("Test", [source, target], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectBinding)).IsTrue();
    }

    [Test]
    public async Task EffectBinding_BareCreateNotExclusivelyOwned_NoError() {
        // P2′′.1: Bare create of entity that is also a source (not exclusively owned) → allowed
        var target = new Entity("Child", [], [], [], []);
        var action = new Poly.DomainModeling.Action("DoIt", InvocationResult.Void, [], [
            new CreateEntityInstance(new DomainTypeReference("Child"))
        ], []);
        var source = new Entity("Parent", [], [action], [], []);
        var rel = new Relationship("owns",
            new DomainTypeReference("Parent"), new DomainTypeReference("Child"),
            RelationshipCardinality.OneToOne, []) { SourceOwnsTarget = true };
        // Child is also a source of another relationship → not exclusively owned
        var otherRel = new Relationship("other",
            new DomainTypeReference("Child"), new DomainTypeReference("Something"),
            RelationshipCardinality.OneToMany, []);
        var something = new Entity("Something", [], [], [], []);
        var domain = DomainTestFactory.Create("Test", [source, target, something], [rel, otherRel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectBinding)).IsFalse();
    }

    [Test]
    public async Task EffectBinding_CreateWithRelationshipNameWrongSource_ReportsError() {
        // P2′′.2: CreateEntityInstance with RelationshipName where action entity is not the relationship source
        var order = new Entity("Order", [], [], [], []);
        var customer = new Entity("Customer", [], [], [], []);
        var action = new Poly.DomainModeling.Action("DoIt", InvocationResult.Void, [], [
            new CreateEntityInstance(new DomainTypeReference("Order"), [], "rel")
        ], []);
        // "Maker" is NOT the source of "rel" (Customer is)
        var maker = new Entity("Maker", [], [action], [], []);
        var rel = new Relationship("rel",
            new DomainTypeReference("Customer"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToMany, []);
        var domain = DomainTestFactory.Create("Test", [maker, customer, order], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectBinding)).IsTrue();
    }

    [Test]
    public async Task EffectBinding_CreateWithRelationshipNameTargetTypeMismatch_ReportsError() {
        // P2′′.2: CreateEntityInstance with RelationshipName where created type ≠ relationship target
        var invoice = new Entity("Invoice", [], [], [], []);
        var order = new Entity("Order", [], [], [], []);
        var action = new Poly.DomainModeling.Action("DoIt", InvocationResult.Void, [], [
            new CreateEntityInstance(new DomainTypeReference("Invoice"), [], "rel")
        ], []);
        var customer = new Entity("Customer", [], [action], [], []);
        var rel = new Relationship("rel",
            new DomainTypeReference("Customer"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToMany, []);
        var domain = DomainTestFactory.Create("Test", [customer, order, invoice], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectBinding)).IsTrue();
    }

    [Test]
    public async Task EffectBinding_CreateWithRelationshipNameHappyPath_NoError() {
        // P2′′.2: CreateEntityInstance with correct RelationshipName on source entity, matching target type → OK
        var order = new Entity("Order", [], [], [], []);
        var action = new Poly.DomainModeling.Action("DoIt", InvocationResult.Void, [], [
            new CreateEntityInstance(new DomainTypeReference("Order"), [], "rel")
        ], []);
        var customer = new Entity("Customer", [], [action], [], []);
        var rel = new Relationship("rel",
            new DomainTypeReference("Customer"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToMany, []);
        var domain = DomainTestFactory.Create("Test", [customer, order], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectBinding)).IsFalse();
    }

    // ── E3b invoke shape (DMEFF007) ────────────────────────────

    [Test]
    public async Task EffectInvokeShape_AnyWithoutRelationship_ReportsError() {
        var action = new Poly.DomainModeling.Action("Run", InvocationResult.Void, [], [
            new InvokeActionEffect("Run", [], Quantifier: StageSubscriptionQuantifier.Any)
        ], []);
        var entity = new Entity("Worker", [], [action], [], []);
        var domain = DomainTestFactory.Create("Test", [entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectInvokeShape)).IsTrue();
    }

    [Test]
    public async Task EffectInvokeShape_WhereWithoutRelationship_ReportsError() {
        var action = new Poly.DomainModeling.Action("Run", InvocationResult.Void, [], [
            new InvokeActionEffect("Run", [],
                Filter: DomainExpression.Literal(true))
        ], []);
        var entity = new Entity("Worker", [], [action], [], []);
        var domain = DomainTestFactory.Create("Test", [entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectInvokeShape)).IsTrue();
    }

    [Test]
    public async Task EffectInvokeShape_WhereWithoutQuantifier_ReportsError() {
        var target = new Entity("Target", [
            new Property("Status", new DomainTypeReference("Text"), [])
        ], Actions: [
            new Poly.DomainModeling.Action("Tag", InvocationResult.Void, [], [], [])
        ], [], []);
        var source = new Entity("Source", [], Actions: [
            new Poly.DomainModeling.Action("Go", InvocationResult.Void, [], [
                new InvokeActionEffect("Tag", [],
                    TargetRelationship: "link",
                    Filter: DomainExpression.Equal(
                        DomainExpression.Property("Status"),
                        DomainExpression.Literal("x")))
            ], [])
        ], [], []);
        var rel = new Relationship("link",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.OneToOne, []);
        var domain = DomainTestFactory.Create("Test", [source, target], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectInvokeShape)).IsTrue();
    }

    [Test]
    public async Task EffectInvokeShape_AnyOnSingularRelationship_ReportsError() {
        var target = new Entity("Target", [], Actions: [
            new Poly.DomainModeling.Action("Tag", InvocationResult.Void, [], [], [])
        ], [], []);
        var source = new Entity("Source", [], Actions: [
            new Poly.DomainModeling.Action("Go", InvocationResult.Void, [], [
                new InvokeActionEffect("Tag", [],
                    TargetRelationship: "link",
                    Quantifier: StageSubscriptionQuantifier.Any)
            ], [])
        ], [], []);
        var rel = new Relationship("link",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.OneToOne, []);
        var domain = DomainTestFactory.Create("Test", [source, target], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectInvokeShape)).IsTrue();
    }

    [Test]
    public async Task EffectInvokeShape_BareInvokeOnMany_ReportsError() {
        var target = new Entity("Target", [], Actions: [
            new Poly.DomainModeling.Action("Tag", InvocationResult.Void, [], [], [])
        ], [], []);
        var source = new Entity("Source", [], Actions: [
            new Poly.DomainModeling.Action("Go", InvocationResult.Void, [], [
                new InvokeActionEffect("Tag", [], TargetRelationship: "items")
            ], [])
        ], [], []);
        var rel = new Relationship("items",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.OneToMany, []);
        var domain = DomainTestFactory.Create("Test", [source, target], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectInvokeShape)).IsTrue();
    }

    [Test]
    public async Task EffectInvokeShape_AllOnManyWithWhere_NoShapeError() {
        var target = new Entity("Target", [
            new Property("Size", new DomainTypeReference("Number"), [])
        ], Actions: [
            new Poly.DomainModeling.Action("Tag", InvocationResult.Void, [], [], [])
        ], [], []);
        var source = new Entity("Source", [], Actions: [
            new Poly.DomainModeling.Action("Go", InvocationResult.Void, [], [
                new InvokeActionEffect("Tag", [],
                    TargetRelationship: "items",
                    Quantifier: StageSubscriptionQuantifier.All,
                    Filter: DomainExpression.GreaterThan(
                        DomainExpression.Property("Size"),
                        DomainExpression.Literal(10L)))
            ], [])
        ], [], []);
        var rel = new Relationship("items",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.OneToMany, []);
        var domain = DomainTestFactory.Create("Test", [source, target], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectInvokeShape)).IsFalse();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectBinding)).IsFalse();
    }

    [Test]
    public async Task EffectInvokeShape_SingularCrossEntity_NoShapeError() {
        var target = new Entity("Target", [], Actions: [
            new Poly.DomainModeling.Action("Tag", InvocationResult.Void, [], [], [])
        ], [], []);
        var source = new Entity("Source", [], Actions: [
            new Poly.DomainModeling.Action("Go", InvocationResult.Void, [], [
                new InvokeActionEffect("Tag", [], TargetRelationship: "link")
            ], [])
        ], [], []);
        var rel = new Relationship("link",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.OneToOne, []);
        var domain = DomainTestFactory.Create("Test", [source, target], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code is DomainModelDiagnosticCodes.EffectInvokeShape
                or DomainModelDiagnosticCodes.EffectBinding)).IsFalse();
    }

    [Test]
    public async Task EffectInvokeShape_WhereUnknownTargetProperty_ReportsBindingError() {
        var target = new Entity("Target", [
            new Property("Size", new DomainTypeReference("Number"), [])
        ], Actions: [
            new Poly.DomainModeling.Action("Tag", InvocationResult.Void, [], [], [])
        ], [], []);
        var source = new Entity("Source", [], Actions: [
            new Poly.DomainModeling.Action("Go", InvocationResult.Void, [], [
                new InvokeActionEffect("Tag", [],
                    TargetRelationship: "items",
                    Quantifier: StageSubscriptionQuantifier.Any,
                    Filter: DomainExpression.Property("MissingProp"))
            ], [])
        ], [], []);
        var rel = new Relationship("items",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.OneToMany, []);
        var domain = DomainTestFactory.Create("Test", [source, target], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectBinding)).IsTrue();
    }

    [Test]
    public async Task EffectInvokeShape_EachQuantifier_ReportsError() {
        var target = new Entity("Target", [], Actions: [
            new Poly.DomainModeling.Action("Tag", InvocationResult.Void, [], [], [])
        ], [], []);
        var source = new Entity("Source", [], Actions: [
            new Poly.DomainModeling.Action("Go", InvocationResult.Void, [], [
                new InvokeActionEffect("Tag", [],
                    TargetRelationship: "items",
                    Quantifier: StageSubscriptionQuantifier.Each)
            ], [])
        ], [], []);
        var rel = new Relationship("items",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.OneToMany, []);
        var domain = DomainTestFactory.Create("Test", [source, target], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectInvokeShape)).IsTrue();
    }

    [Test]
    public async Task EffectInvokeShape_ReverseSideInvoke_ReportsError() {
        // Fail-closed: only source may cross-invoke via RelName.
        var target = new Entity("Target", [], Actions: [
            new Poly.DomainModeling.Action("Ping", InvocationResult.Void, [], [
                new InvokeActionEffect("Ack", [], TargetRelationship: "link")
            ], [])
        ], [], []);
        var source = new Entity("Source", [], Actions: [
            new Poly.DomainModeling.Action("Ack", InvocationResult.Void, [], [], [])
        ], [], []);
        var rel = new Relationship("link",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.OneToOne, []);
        var domain = DomainTestFactory.Create("Test", [source, target], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectInvokeShape)).IsTrue();
    }

    [Test]
    public async Task EffectInvokeShape_ManyToMany_ReportsError() {
        var target = new Entity("Target", [], Actions: [
            new Poly.DomainModeling.Action("Tag", InvocationResult.Void, [], [], [])
        ], [], []);
        var source = new Entity("Source", [], Actions: [
            new Poly.DomainModeling.Action("Go", InvocationResult.Void, [], [
                new InvokeActionEffect("Tag", [],
                    TargetRelationship: "peers",
                    Quantifier: StageSubscriptionQuantifier.All)
            ], [])
        ], [], []);
        var rel = new Relationship("peers",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.ManyToMany, []);
        var domain = DomainTestFactory.Create("Test", [source, target], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectInvokeShape)).IsTrue();
    }

    [Test]
    public async Task EffectInvokeShape_SelfRelationship_ReportsError() {
        var node = new Entity("Node", [], Actions: [
            new Poly.DomainModeling.Action("Go", InvocationResult.Void, [], [
                new InvokeActionEffect("Go", [], TargetRelationship: "next")
            ], [])
        ], [], []);
        var rel = new Relationship("next",
            new DomainTypeReference("Node"), new DomainTypeReference("Node"),
            RelationshipCardinality.OneToOne, []);
        var domain = DomainTestFactory.Create("Test", [node], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectInvokeShape)).IsTrue();
    }

    [Test]
    public async Task EffectInvokeShape_MissingParameterBinding_ReportsError() {
        var target = new Entity("Target", [], Actions: [
            new Poly.DomainModeling.Action("Set", InvocationResult.Void,
                Parameters: [new Property("msg", new DomainTypeReference("Text"), [])],
                Effects: [], Policies: [])
        ], [], []);
        var source = new Entity("Source", [], Actions: [
            new Poly.DomainModeling.Action("Go", InvocationResult.Void, [], [
                new InvokeActionEffect("Set", [], TargetRelationship: "link")
            ], [])
        ], [], []);
        var rel = new Relationship("link",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.OneToOne, []);
        var domain = DomainTestFactory.Create("Test", [source, target], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectBinding)).IsTrue();
    }

    [Test]
    public async Task EffectInvokeShape_FilterParameterAccess_ReportsError() {
        var target = new Entity("Target", [
            new Property("Size", new DomainTypeReference("Number"), [])
        ], Actions: [
            new Poly.DomainModeling.Action("Tag", InvocationResult.Void, [], [], [])
        ], [], []);
        var source = new Poly.DomainModeling.Action("Go", InvocationResult.Void,
            Parameters: [new Property("threshold", new DomainTypeReference("Number"), [])],
            Effects: [
                new InvokeActionEffect("Tag", [],
                    TargetRelationship: "items",
                    Quantifier: StageSubscriptionQuantifier.Any,
                    Filter: DomainExpression.GreaterThan(
                        DomainExpression.Property("Size"),
                        DomainExpression.Parameter("threshold")))
            ],
            Policies: []);
        var sourceEntity = new Entity("Source", [], [source], [], []);
        var rel = new Relationship("items",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.OneToMany, []);
        var domain = DomainTestFactory.Create("Test", [sourceEntity, target], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectInvokeShape)).IsTrue();
    }
}

public class UnsatisfiedRequirementTests {
    [Test]
    public async Task EffectUnsatisfiedRequirement_StageTransitionSatisfied_DoesNotReport() {
        var text = new Poly.DomainModeling.PrimitiveType("Text", TypeCategory.Text, []);
        var title = new Property("Title", new DomainTypeReference("Text"), [new RequiredConstraint()]);
        var stage = new Stage("Open", [],
            [new Policy("RequiresTitle", DomainExpression.Exists(new PropertyAccess("Title")))],
            [], []);
        var action = new Poly.DomainModeling.Action("OpenTicket", InvocationResult.Void, [
            new Property("incomingTitle", new DomainTypeReference("Text"), [])
        ], [
            new AssignEffect(new PropertyAccess("Title"), new ParameterAccess("incomingTitle")),
            new StageTransitionEffect(new StageReference("Open"))
        ], []);
        var entity = new Entity("Ticket", [title], [action], [], [stage]);
        var domain = DomainTestFactory.Create("Test", [text, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectUnsatisfiedRequirement)).IsFalse();
    }

    [Test]
    public async Task EffectUnsatisfiedRequirement_StageTransitionMissingAssignment_ReportsWarning() {
        var text = new Poly.DomainModeling.PrimitiveType("Text", TypeCategory.Text, []);
        var title = new Property("Title", new DomainTypeReference("Text"), []);
        // Stage-scoped requirement: a stage policy requires Title to exist while in
        // the stage — entering without assigning it is a genuine gap.
        var stage = new Stage("Open", [],
            [new Policy("RequiresTitle", DomainExpression.Exists(new PropertyAccess("Title")))],
            [], []);
        var action = new Poly.DomainModeling.Action("OpenTicket", InvocationResult.Void, [], [
            new StageTransitionEffect(new StageReference("Open"))
        ], []);
        var entity = new Entity("Ticket", [title], [action], [], [stage]);
        var domain = DomainTestFactory.Create("Test", [text, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectUnsatisfiedRequirement)).IsTrue();
    }

    [Test]
    public async Task EffectUnsatisfiedRequirement_EntityRequiredProp_DoesNotWarnOnTransition() {
        // A property with a `required` constraint is a CREATION invariant — set at
        // create time and enforced by the Create factory. It is NOT a stage-entry
        // requirement, so transitioning into a stage must not warn (false positive:
        // TinyCompiler's EntryPath warned on every transition despite being set at
        // `create in builds { EntryPath: ... }`).
        var text = new Poly.DomainModeling.PrimitiveType("Text", TypeCategory.Text, []);
        var entryPath = new Property("EntryPath", new DomainTypeReference("Text"), [new RequiredConstraint()]);
        var stage = new Stage("Lexing", [], [], [], []);
        var action = new Poly.DomainModeling.Action("Begin", InvocationResult.Void, [], [
            new StageTransitionEffect(new StageReference("Lexing"))
        ], []);
        var entity = new Entity("Compilation", [entryPath], [action], [], [stage]);
        var domain = DomainTestFactory.Create("Test", [text, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectUnsatisfiedRequirement)).IsFalse();
    }

    [Test]
    public async Task EffectUnsatisfiedRequirement_EntityPolicyExists_DoesNotWarnOnTransition() {
        // An ENTITY-level policy `source exists` is a LINK-TIME invariant — the nav is
        // established by create-in / link_instances, not by a transition-time assign.
        // Transition warnings use STAGE-scoped requirements only; entity-level policy
        // Exists targets are not transition-entry concerns (same class as the
        // creation-required false positives). Pins the intentional semantic so a
        // future "restore the entity fallback" cannot silently reintroduce the noise.
        var text = new Poly.DomainModeling.PrimitiveType("Text", TypeCategory.Text, []);
        var source = new Property("source", new DomainTypeReference("SourceFile"), []);
        var policy = new Policy("HasSource", DomainExpression.Exists(new PropertyAccess("source")));
        var stage = new Stage("Lexing", [], [], [], []);
        var action = new Poly.DomainModeling.Action("Begin", InvocationResult.Void, [], [
            new StageTransitionEffect(new StageReference("Lexing"))
        ], []);
        var entity = new Entity("Compilation", [source], [action], [policy], [stage]);
        var domain = DomainTestFactory.Create("Test", [text, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectUnsatisfiedRequirement)).IsFalse();
    }

    [Test]
    public async Task EffectUnsatisfiedRequirement_CreateEntityMissingRequiredProperty_ReportsWarning() {
        var text = new Poly.DomainModeling.PrimitiveType("Text", TypeCategory.Text, []);
        var title = new Property("Title", new DomainTypeReference("Text"), [new RequiredConstraint()]);
        var target = new Entity("Order", [title], [], [], []);
        var action = new Poly.DomainModeling.Action("CreateOrder", InvocationResult.Void, [], [
            new CreateEntityInstance(new DomainTypeReference("Order"))
        ], []);
        var entity = new Entity("Factory", [], [action], [], []);
        var domain = DomainTestFactory.Create("Test", [text, target, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectUnsatisfiedRequirement)).IsTrue();
    }
}