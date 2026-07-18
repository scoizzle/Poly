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
        var domain = new Domain("Test", [first, second], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.StructuralDuplicate)).IsTrue();
    }

    [Test]
    public async Task StructuralCycle_EntityParentCycle_ReportsError() {
        var entityA = new Entity("A", [], [], [], []) { ParentEntityName = "B" };
        var entityB = new Entity("B", [], [], [], []) { ParentEntityName = "A" };
        var domain = new Domain("Test", [entityA, entityB], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.StructuralCycle)).IsTrue();
    }

    [Test]
    public async Task StructuralOwnership_ManyToManyWithOwnership_ReportsError() {
        var text = new Poly.DomainModeling.PrimitiveType("Text", TypeCategory.Text, []);
        var source = new Entity("Source", [], [], [], []);
        var target = new Entity("Target", [], [], [], []);
        var rel = new Relationship("OwnsMany",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.ManyToMany, []) { SourceOwnsTarget = true };
        var domain = new Domain("Test", [text, source, target], [rel]);

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
        var domain = new Domain("Test", [text, source, target], [rel]);

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
        var domain = new Domain("Test", [text, source, target], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.StructuralOwnership)).IsFalse();
    }
}

public class SemanticAnalysisTests {
    [Test]
    public async Task SemanticTypeCompatibility_DisallowedNullableCategory_ReportsError() {
        var nullable = new Poly.DomainModeling.PrimitiveType("NullableInt", TypeCategory.Nullable, []);
        var domain = new Domain("Test", [nullable], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SemanticTypeCompatibility)).IsTrue();
    }

    [Test]
    public async Task SemanticTypeCompatibility_DisallowedCollectionCategory_ReportsError() {
        var collection = new Poly.DomainModeling.PrimitiveType("CollectionInt", TypeCategory.Collection, []);
        var domain = new Domain("Test", [collection], []);

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
        var child = new Entity("Ticket", [childProp], [], [], []) { ParentEntityName = "BaseTicket" };
        var text = new Poly.DomainModeling.PrimitiveType("Text", TypeCategory.Text, []);
        var domain = new Domain("Test", [text, parent, child], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SemanticConstraintMismatch)).IsTrue();
    }

    [Test]
    public async Task SemanticReferenceResolution_UndefinedTypeReference_ReportsError() {
        var entity = new Entity("Ticket", [new Property("Title", new DomainTypeReference("UndefinedType"), [])], [], [], []);
        var domain = new Domain("Test", [entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SemanticReferenceResolution)).IsTrue();
    }



    [Test]
    public async Task SemanticReferenceResolution_UndefinedParentEntity_ReportsError() {
        var entity = new Entity("Ticket", [], [], [], []) { ParentEntityName = "NonExistent" };
        var domain = new Domain("Test", [entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SemanticReferenceResolution)).IsTrue();
    }
}

public class EffectBindingTests {
    [Test]
    public async Task EffectBinding_UnknownTypeInCreateEntityInstance_ReportsError() {
        var action = new Poly.DomainModeling.Action("Create", InvocationResult.Void, [], [
            new CreateEntityInstance(new DomainTypeReference("NonExistent"))
        ], []);
        var entity = new Entity("Maker", [], [action], [], []);
        var domain = new Domain("Test", [entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectBinding)).IsTrue();
    }



    [Test]
    public async Task EffectBinding_UnknownActionInInvoke_ReportsError() {
        var action = new Poly.DomainModeling.Action("DoIt", InvocationResult.Void, [], [
            new InvokeActionEffect("NonExistentAction", [])
        ], []);
        var entity = new Entity("Worker", [], [action], [], []);
        var domain = new Domain("Test", [entity], []);

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
        var domain = new Domain("Test", [text, entity], []);

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
        var domain = new Domain("Test", [entity], []);

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
        var domain = new Domain("Test", [text, entity], []);

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
        var domain = new Domain("Test", [entity], []);

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
        var rel = new Relationship("rel",
            new DomainTypeReference("Customer"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToMany, []);
        var domain = new Domain("Test", [maker, order], [rel]);

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
        var domain = new Domain("Test", [customer, order], [rel]);

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
        var domain = new Domain("Test", [customer, order], [rel]);

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
        var domain = new Domain("Test", [source, target], [rel]);

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
        var domain = new Domain("Test", [source, target, something], [rel, otherRel]);

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
        var domain = new Domain("Test", [maker, customer, order], [rel]);

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
        var domain = new Domain("Test", [customer, order, invoice], [rel]);

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
        var domain = new Domain("Test", [customer, order], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectBinding)).IsFalse();
    }
}

public class UnsatisfiedRequirementTests {
    [Test]
    public async Task EffectUnsatisfiedRequirement_StageTransitionSatisfied_DoesNotReport() {
        var text = new Poly.DomainModeling.PrimitiveType("Text", TypeCategory.Text, []);
        var title = new Property("Title", new DomainTypeReference("Text"), [new RequiredConstraint()]);
        var stage = new Stage("Open", [], [], [], []);
        var action = new Poly.DomainModeling.Action("OpenTicket", InvocationResult.Void, [
            new Property("incomingTitle", new DomainTypeReference("Text"), [])
        ], [
            new AssignEffect(new PropertyAccess("Title"), new ParameterAccess("incomingTitle")),
            new StageTransitionEffect(new StageReference("Open"))
        ], []);
        var entity = new Entity("Ticket", [title], [action], [], [stage]);
        var domain = new Domain("Test", [text, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectUnsatisfiedRequirement)).IsFalse();
    }

    [Test]
    public async Task EffectUnsatisfiedRequirement_StageTransitionMissingAssignment_ReportsWarning() {
        var text = new Poly.DomainModeling.PrimitiveType("Text", TypeCategory.Text, []);
        var title = new Property("Title", new DomainTypeReference("Text"), [new RequiredConstraint()]);
        var stage = new Stage("Open", [], [], [], []);
        var action = new Poly.DomainModeling.Action("OpenTicket", InvocationResult.Void, [], [
            new StageTransitionEffect(new StageReference("Open"))
        ], []);
        var entity = new Entity("Ticket", [title], [action], [], [stage]);
        var domain = new Domain("Test", [text, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectUnsatisfiedRequirement)).IsTrue();
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
        var domain = new Domain("Test", [text, target, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectUnsatisfiedRequirement)).IsTrue();
    }
}