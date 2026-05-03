using Poly.Data.Modeling;
using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation.Constraints;
using Poly.Introspection;

using DomainAction = Poly.Data.Modeling.Action;

namespace Poly.Tests.Data.Modeling;

public class DomainTests {
    [Test]
    public async Task Domain_AddType_WithMatchingDomain_AddsType() {
        var domain = DomainTestFactory.CreateDomain();
        var customer = CreatePrimitive(domain, "Customer");

        MutationApply.AddType(domain, customer);

        await Assert.That(domain.Types.Contains(customer)).IsTrue();
    }

    [Test]
    public async Task Domain_AddType_WithDifferentDomain_ThrowsInvalidOperationException() {
        var parentDomain = DomainTestFactory.CreateDomain("Parent Domain");
        var otherDomain = DomainTestFactory.CreateDomain("Other Domain");
        var customer = CreatePrimitive(otherDomain, "Customer");

        var result = MutationApply.AddType(parentDomain, customer);
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("domain"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task Domain_AddType_WithDuplicateName_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var customer1 = CreatePrimitive(domain, "Customer");
        var customer2 = CreatePrimitive(domain, "Customer");
        var mutation = domain.CreateMutation();
        _ = mutation.AddType(customer1).AddType(customer2);
        var result = mutation.Apply();
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Duplicate") && d.Message.Contains("Customer"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task Entity_Constructor_WithParentEntityFromDifferentDomain_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var otherDomain = DomainTestFactory.CreateDomain("Other Domain");

        var parent = CreatePrimitive(otherDomain, "Parent");
        var mutation = domain.CreateMutation();
        _ = mutation.AddType(parent);
        var result = mutation.Apply();

        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.MutationInvariant && d.Message.Contains("Parent"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task Entity_Constructor_WithParentEntity_AssignsParentEntity() {
        var domain = DomainTestFactory.CreateDomain();

        var parent = new Entity(domain, "Parent");

        var child = new Entity(domain, "Child", parent);

        await Assert.That(ReferenceEquals(child.ParentEntity, parent)).IsTrue();
    }

    [Test]
    public async Task Domain_AddRelationship_WithMatchingDomain_AddsRelationship() {
        var domain = DomainTestFactory.CreateDomain();
        var customer = new Entity(domain, "Customer");
        var invoice = new Entity(domain, "Invoice");
        MutationApply.AddType(domain, customer);
        MutationApply.AddType(domain, invoice);

        var relationship = new Relationship(domain, "CustomerInvoices", customer, invoice, RelationshipCardinality.OneToMany, false);

        MutationApply.AddRelationship(domain, relationship);

        await Assert.That(domain.Relationships.Contains(relationship)).IsTrue();
    }

    [Test]
    public async Task Domain_AddRelationship_WithDifferentDomain_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var otherDomain = DomainTestFactory.CreateDomain("Other Domain");
        var customer = new Entity(domain, "Customer");
        var invoice = new Entity(domain, "Invoice");

        MutationApply.AddType(domain, customer);
        MutationApply.AddType(domain, invoice);

        var relationship = new Relationship(otherDomain, "CustomerInvoices", customer, invoice, RelationshipCardinality.OneToMany, false);

        var result = MutationApply.AddRelationship(domain, relationship);
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("domain"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task Domain_AddRelationship_WithForeignEndpoint_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var otherDomain = DomainTestFactory.CreateDomain("Other Domain");
        var customer = new Entity(domain, "Customer");
        var invoice = new Entity(otherDomain, "Invoice");
        MutationApply.AddType(domain, customer);

        var relationship = new Relationship(domain, "CustomerInvoices", customer, invoice, RelationshipCardinality.OneToMany, false);

        var result = MutationApply.AddRelationship(domain, relationship);
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("domain"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task Domain_AddRelationship_WithDuplicateName_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var source = new Entity(domain, "Customer");
        var target = new Entity(domain, "SupportCase");
        var mutation = domain.CreateMutation();
        _ = mutation.AddType(source).AddType(target);
        var rel1 = new Relationship(domain, "CustomerCases", source, target, RelationshipCardinality.OneToOne, false);
        var rel2 = new Relationship(domain, "CustomerCases", source, target, RelationshipCardinality.OneToOne, false);
        _ = mutation.AddRelationship(rel1).AddRelationship(rel2);
        var result = mutation.Apply();
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Duplicate") && d.Message.Contains("CustomerCases"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task Domain_AddRelationship_WhenOwnershipTargetAlreadyOwned_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var customer = new Entity(domain, "Customer");
        var agent = new Entity(domain, "Agent");
        var note = new Entity(domain, "Note");
        MutationApply.AddType(domain, customer);
        MutationApply.AddType(domain, agent);
        MutationApply.AddType(domain, note);

        MutationApply.AddRelationship(domain, new Relationship(domain, "CustomerNotes", customer, note, RelationshipCardinality.OneToMany, true));
        var second = new Relationship(domain, "AgentNotes", agent, note, RelationshipCardinality.OneToMany, true);
        var result = MutationApply.AddRelationship(domain, second);
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("multiple ownership relationships"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task Domain_AddRelationship_AfterRegistration_AllowsValidDefinitionMutation() {
        var domain = DomainTestFactory.CreateDomain();
        var source = new Entity(domain, "Customer");
        var target = new Entity(domain, "SupportCase");
        MutationApply.AddType(domain, source);
        MutationApply.AddType(domain, target);

        var relationship = new Relationship(domain, "CustomerCases", source, target, RelationshipCardinality.OneToMany, false);

        MutationApply.AddRelationship(domain, relationship);

        var mutation = domain.CreateMutation();
        _ = mutation.SetRelationship(relationship, source, target, RelationshipCardinality.OneToOne, false);
        _ = mutation.Apply();

        await Assert.That(relationship.Cardinality).IsEqualTo(RelationshipCardinality.OneToOne);
    }

    [Test]
    public async Task Relationship_Mutation_WithInvalidOwnershipCardinality_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var source = new Entity(domain, "Customer");
        var target = new Entity(domain, "SupportCase");
        MutationApply.AddType(domain, source);
        MutationApply.AddType(domain, target);

        var relationship = new Relationship(domain, "CustomerCases", source, target, RelationshipCardinality.ManyToMany, false);

        MutationApply.AddRelationship(domain, relationship);
        var result = MutationApply.AddRelationship(domain, new Relationship(domain, "OtherRel", source, target, RelationshipCardinality.ManyToMany, true));
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("must be one-to-one or one-to-many"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task Relationship_Mutation_WithDuplicateOwnershipTarget_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var customer = new Entity(domain, "Customer");
        var agent = new Entity(domain, "Agent");
        var note = new Entity(domain, "Note");
        MutationApply.AddType(domain, customer);
        MutationApply.AddType(domain, agent);
        MutationApply.AddType(domain, note);

        MutationApply.AddRelationship(domain, new Relationship(domain, "CustomerNotes", customer, note, RelationshipCardinality.OneToMany, true));
        var second = new Relationship(domain, "AgentNotes", agent, note, RelationshipCardinality.OneToMany, true);
        var result = MutationApply.AddRelationship(domain, second);
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("multiple ownership relationships"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task Relationship_AddProperty_WithMatchingDomain_AddsProperty() {
        var domain = DomainTestFactory.CreateDomain();
        var source = new Entity(domain, "Customer");
        var target = new Entity(domain, "SupportCase");
        var timestamp = CreatePrimitive(domain, "instant", TypeCategory.Instant);
        MutationApply.AddType(domain, source);
        MutationApply.AddType(domain, target);

        var relationship = new Relationship(domain, "AgentSupportCases", source, target, RelationshipCardinality.ManyToMany, false);

        var assignedAt = new Property(domain, "AssignedAt", timestamp);

        MutationApply.AddProperty(relationship, assignedAt);

        await Assert.That(relationship.Properties.Contains(assignedAt)).IsTrue();
    }

    [Test]
    public async Task Relationship_AddProperty_WithDifferentDomain_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var otherDomain = DomainTestFactory.CreateDomain("Other Domain");
        var source = new Entity(domain, "Customer");
        var target = new Entity(domain, "SupportCase");
        var timestamp = CreatePrimitive(otherDomain, "instant", TypeCategory.Instant);
        MutationApply.AddType(domain, source);
        MutationApply.AddType(domain, target);

        var relationship = new Relationship(domain, "AgentSupportCases", source, target, RelationshipCardinality.ManyToMany, false);
        MutationApply.AddRelationship(domain, relationship);

        var assignedAt = new Property(otherDomain, "AssignedAt", timestamp);

        var result = MutationApply.AddProperty(relationship, assignedAt);
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("domain"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task Entity_AddProperty_WithDuplicateName_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "Ticket");
        var stringType = CreatePrimitive(domain, "string", TypeCategory.Text);
        var mutation = domain.CreateMutation();
        _ = mutation.AddType(entity).AddType(stringType);
        var prop1 = new Property(domain, "Title", stringType);
        var prop2 = new Property(domain, "Title", stringType);
        _ = mutation.AddProperty(entity, prop1).AddProperty(entity, prop2);
        var result = mutation.Apply();
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Duplicate property name 'Title'"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task Entity_AddRelationship_WhenNotRegisteredInDomain_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var source = new Entity(domain, "Customer");
        var target = new Entity(domain, "SupportCase");
        MutationApply.AddType(domain, source);
        MutationApply.AddType(domain, target);

        var relationship = new Relationship(domain, "CustomerCases", source, target, RelationshipCardinality.OneToOne, false);

        var result = MutationApply.AddRelationship(source, relationship);
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("must be registered in domain"));
        await Assert.That(error is not null).IsTrue();
    }

    private static Primitive CreatePrimitive(Domain domain, string name, TypeCategory category = TypeCategory.Primitive) {
        return new Primitive(domain, name, category);
    }

}

public class StageTests {
    [Test]
    public async Task AddPolicy_WhenAnalyzerFails_RollsBackMutation() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "Ticket");
        var stage = new Stage(domain, "Open");
        var mutation = domain.CreateMutation();
        _ = mutation.AddType(entity).AddStage(entity, stage);
        var policy = new Policy(domain, "RequireTitle");
        _ = mutation.AddPolicy(stage, policy).AddPolicy(stage, policy);
        var result = mutation.Apply();
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Duplicate") && d.Message.Contains("RequireTitle"));
        await Assert.That(error is not null).IsTrue();
    }
    [Test]
    public async Task Entity_AddStage_WhenParentEntityHasStagesAndStageHasNoParent_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var parent = new Entity(domain, "Parent");
        var child = new Entity(domain, "Child", parent);
        var mutation = domain.CreateMutation();
        _ = mutation.AddType(parent).AddType(child);
        var parentStage = new Stage(domain, "Open");
        var draftStage = new Stage(domain, "Draft");
        _ = mutation.AddStage(parent, parentStage).AddStage(child, draftStage);
        var result = mutation.Apply();
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("must have a parent stage when parent entity 'Parent' defines stages."));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task Entity_AddStage_WhenParentEntityHasStagesAndDirectParentStageMatch_AddsStage() {
        var domain = DomainTestFactory.CreateDomain();
        var parent = new Entity(domain, "Parent");
        var child = new Entity(domain, "Child", parent);
        MutationApply.AddType(domain, parent);
        MutationApply.AddType(domain, child);

        var parentStage = new Stage(domain, "Open");
        var childStage = new Stage(domain, "Draft") { Parent = parentStage };
        MutationApply.AddStage(parent, parentStage);
        MutationApply.AddStage(child, childStage);

        await Assert.That(child.Stages.Contains(childStage)).IsTrue();
    }

    [Test]
    public async Task Entity_AddStage_WhenParentEntityHasStagesAndDirectParentStageDoesNotMatch_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var parent = new Entity(domain, "Parent");
        var child = new Entity(domain, "Child", parent);
        var mutation = domain.CreateMutation();
        _ = mutation.AddType(parent).AddType(child);
        var parentStage = new Stage(domain, "Open");
        var childStage = new Stage(domain, "Draft") { Parent = parentStage };
        var grandChildStage = new Stage(domain, "Review") { Parent = childStage };
        _ = mutation.AddStage(parent, parentStage).AddStage(child, childStage).AddStage(child, grandChildStage);
        var result = mutation.Apply();
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("must directly inherit from a stage defined on parent entity 'Parent'."));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task Stage_GetEffectiveActions_PrefersLocalActionOverInheritedAction() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "Case");

        var parent = new Stage(domain, "Parent");

        var child = new Stage(domain, "Child") { Parent = parent };

        var inheritedEscalate = new DomainAction(domain, "Escalate", entity);

        var localEscalate = new DomainAction(domain, "Escalate", entity);

        MutationApply.AddAction(parent, inheritedEscalate);
        MutationApply.AddAction(child, localEscalate);

        var effectiveActions = child.GetEffectiveActions().ToArray();

        await Assert.That(effectiveActions.Length).IsEqualTo(1);
        await Assert.That(ReferenceEquals(effectiveActions.Single(), localEscalate)).IsTrue();
    }

    [Test]
    public async Task Stage_GetEffectiveActions_IncludesParentChainActionsWithoutDuplicates() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "Case");

        var grandParent = new Stage(domain, "Grand Parent");

        var parent = new Stage(domain, "Parent") { Parent = grandParent };

        var child = new Stage(domain, "Child") { Parent = parent };

        var triage = new DomainAction(domain, "Triage", entity);

        var review = new DomainAction(domain, "Review", entity);

        var complete = new DomainAction(domain, "Complete", entity);

        MutationApply.AddAction(grandParent, triage);
        MutationApply.AddAction(parent, review);
        MutationApply.AddAction(child, complete);

        var names = child.GetEffectiveActions().Select(action => action.Name).ToArray();

        await Assert.That(names.Length).IsEqualTo(3);
        await Assert.That(names).Contains("Triage");
        await Assert.That(names).Contains("Review");
        await Assert.That(names).Contains("Complete");
    }

    [Test]
    public async Task Stage_AddPolicy_WithDuplicateName_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var owner = new Entity(domain, "Ticket");
        var stage = new Stage(domain, "Open");
        var mutation = domain.CreateMutation();
        _ = mutation.AddType(owner).AddStage(owner, stage);
        _ = mutation.AddPolicy(stage, new Policy(domain, "RequireTitle")).AddPolicy(stage, new Policy(domain, "RequireTitle"));
        var result = mutation.Apply();
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Duplicate") && d.Message.Contains("RequireTitle"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task Stage_AddAction_WithDuplicateName_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "Ticket");
        var stage = new Stage(domain, "Open");
        var mutation = domain.CreateMutation();
        _ = mutation.AddType(entity).AddStage(entity, stage);
        _ = mutation.AddAction(stage, new DomainAction(domain, "Assign", entity)).AddAction(stage, new DomainAction(domain, "Assign", entity));
        var result = mutation.Apply();
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Duplicate") && d.Message.Contains("Assign"));
        await Assert.That(error is not null).IsTrue();
    }
}

public class ActionAndEventMutationTests {
    [Test]
    public async Task Entity_AddAction_WithDifferentEntity_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var owner = new Entity(domain, "SupportCase");
        var other = new Entity(domain, "Ticket");
        var mutation = domain.CreateMutation();
        _ = mutation.AddType(owner).AddType(other);
        var action = new DomainAction(domain, "Assign", other);
        _ = mutation.AddAction(owner, action);
        var result = mutation.Apply();
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Assign") && d.Message.Contains("entity"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task Stage_AddAction_WhenAttachedAndEntityMismatched_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var owner = new Entity(domain, "SupportCase");
        var other = new Entity(domain, "Ticket");
        var mutation = domain.CreateMutation();
        _ = mutation.AddType(owner).AddType(other);
        var stage = new Stage(domain, "New");
        _ = mutation.AddStage(owner, stage);
        var mismatchedAction = new DomainAction(domain, "Assign", other);
        _ = mutation.AddAction(stage, mismatchedAction);
        var result = mutation.Apply();
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Assign") && d.Message.Contains("entity"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task Event_AddProperty_WithDuplicateName_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var eventType = new Event(domain, "CaseAssigned");
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var mutation = domain.CreateMutation();
        _ = mutation.AddType(eventType);
        _ = mutation.AddProperty(eventType, new Property(domain, "AssignedTo", stringType)).AddProperty(eventType, new Property(domain, "AssignedTo", stringType));
        var result = mutation.Apply();
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Duplicate") && d.Message.Contains("AssignedTo"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task Action_AddParameter_WithDuplicatePropertyName_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "SupportCase");
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var action = new DomainAction(domain, "AddNote", entity);
        var mutation = domain.CreateMutation();
        _ = mutation.AddType(entity).AddAction(entity, action);
        _ = mutation.AddParameter(action, new Property(domain, "NoteText", stringType)).AddParameter(action, new Property(domain, "NoteText", stringType));
        var result = mutation.Apply();
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Duplicate") && d.Message.Contains("NoteText"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task Action_AddEffect_CreateEntityInstanceWithMismatchedInitialStage_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "SupportCase");
        var note = new Entity(domain, "Note");
        var action = new DomainAction(domain, "AddNote", entity);
        var wrongStage = new Stage(domain, "Wrong");
        MutationApply.AddType(domain, entity);
        MutationApply.AddType(domain, note);
        MutationApply.AddAction(entity, action);

        var mutation = domain.CreateMutation();
        _ = mutation.AddType(entity).AddType(note).AddAction(entity, action);
        _ = mutation.AddEffect(action, new CreateEntityInstance(domain) { EntityType = note, InitialStage = wrongStage });
        var result = mutation.Apply();
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Wrong"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task Action_AddEffect_StageTransitionToForeignEntityStage_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var sourceEntity = new Entity(domain, "SupportCase");
        var targetEntity = new Entity(domain, "Note");
        var sourceAction = new DomainAction(domain, "Assign", sourceEntity);
        var foreignStage = new Stage(domain, "Draft");
        var mutation = domain.CreateMutation();
        _ = mutation.AddType(sourceEntity).AddType(targetEntity).AddAction(sourceEntity, sourceAction);
        _ = mutation.AddStage(sourceEntity, new Stage(domain, "New")).AddStage(targetEntity, foreignStage);
        _ = mutation.AddEffect(sourceAction, new StageTransition(domain) { TargetStage = foreignStage });
        var result = mutation.Apply();
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Draft") && d.Message.Contains("entity"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task Action_AddEffect_PublishEventWithoutRequiredBindings_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "SupportCase");
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var action = new DomainAction(domain, "Assign", entity);
        var @event = new Event(domain, "CaseAssigned");
        MutationApply.AddType(domain, entity);
        MutationApply.AddType(domain, @event);
        MutationApply.AddAction(entity, action);
        MutationApply.AddProperty(@event, new Property(domain, "AssignedTo", stringType));
        var result = MutationApply.AddEffect(action, new PublishEvent(domain) { Event = @event });
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("AssignedTo"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task Action_AddEffect_InvokeActionWithoutRequiredBindings_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "SupportCase");
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var targetAction = new DomainAction(domain, "Resolve", entity);
        MutationApply.AddType(domain, entity);
        MutationApply.AddAction(entity, targetAction);
        MutationApply.AddParameter(targetAction, new Property(domain, "Reason", stringType));
        var sourceAction = new DomainAction(domain, "Escalate", entity);
        MutationApply.AddAction(entity, sourceAction);
        var result = MutationApply.AddEffect(sourceAction, new InvokeAction(domain) { TargetAction = targetAction });
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Reason"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task Action_AddEffect_InvokeActionWithBoundParameters_AddsEffect() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "SupportCase");
        var stringType = new Primitive(domain, "string", TypeCategory.Text);

        var targetAction = new DomainAction(domain, "Resolve", entity);
        var reasonParameter = new Property(domain, "Reason", stringType);
        MutationApply.AddParameter(targetAction, reasonParameter);

        var sourceAction = new DomainAction(domain, "Escalate", entity);
        var sourceReason = new Property(domain, "SourceReason", stringType);

        var invoke = new InvokeAction(domain) { TargetAction = targetAction };
        invoke.BindParameter(reasonParameter, sourceReason);

        MutationApply.AddEffect(sourceAction, invoke);

        await Assert.That(sourceAction.Effects.Contains(invoke)).IsTrue();
    }

    [Test]
    public async Task Property_AddPolicy_WithDuplicateName_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var owner = new Entity(domain, "Ticket");
        var property = new Property(domain, "Title", stringType);
        MutationApply.AddType(domain, owner);
        MutationApply.AddProperty(owner, property);
        MutationApply.AddPolicy(property, new Policy(domain, "RequireTitle"));
        var result = MutationApply.AddPolicy(property, new Policy(domain, "RequireTitle"));
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Duplicate") && d.Message.Contains("RequireTitle"));
        await Assert.That(error is not null).IsTrue();
    }
}