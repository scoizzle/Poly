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

        domain.AddType(customer);

        await Assert.That(domain.Types.Contains(customer)).IsTrue();
    }

    [Test]
    public async Task Domain_AddType_WithDifferentDomain_ThrowsInvalidOperationException() {
        var parentDomain = DomainTestFactory.CreateDomain("Parent Domain");
        var otherDomain = DomainTestFactory.CreateDomain("Other Domain");
        var customer = CreatePrimitive(otherDomain, "Customer");

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            parentDomain.AddType(customer);
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Domain_AddType_WithDuplicateName_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();

        domain.AddType(CreatePrimitive(domain, "Customer"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            domain.AddType(CreatePrimitive(domain, "Customer"));
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Entity_Constructor_WithParentEntityFromDifferentDomain_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var otherDomain = DomainTestFactory.CreateDomain("Other Domain");

        var parent = new Entity(otherDomain, "Parent");

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            _ = new Entity(domain, "Child", parent);
            await Task.CompletedTask;
        });
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
        domain.AddType(customer);
        domain.AddType(invoice);

        var relationship = new Relationship(domain, "CustomerInvoices", customer, invoice, RelationshipCardinality.OneToMany, false);

        domain.AddRelationship(relationship);

        await Assert.That(domain.Relationships.Contains(relationship)).IsTrue();
    }

    [Test]
    public async Task Domain_AddRelationship_WithDifferentDomain_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var otherDomain = DomainTestFactory.CreateDomain("Other Domain");
        var customer = new Entity(domain, "Customer");
        var invoice = new Entity(domain, "Invoice");

        domain.AddType(customer);
        domain.AddType(invoice);

        var relationship = new Relationship(otherDomain, "CustomerInvoices", customer, invoice, RelationshipCardinality.OneToMany, false);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            domain.AddRelationship(relationship);
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Domain_AddRelationship_WithForeignEndpoint_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var otherDomain = DomainTestFactory.CreateDomain("Other Domain");
        var customer = new Entity(domain, "Customer");
        var invoice = new Entity(otherDomain, "Invoice");
        domain.AddType(customer);

        var relationship = new Relationship(domain, "CustomerInvoices", customer, invoice, RelationshipCardinality.OneToMany, false);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            domain.AddRelationship(relationship);
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Domain_AddRelationship_WithDuplicateName_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var source = new Entity(domain, "Customer");
        var target = new Entity(domain, "SupportCase");
        domain.AddType(source);
        domain.AddType(target);

        domain.AddRelationship(new Relationship(domain, "CustomerCases", source, target, RelationshipCardinality.OneToOne, false));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            domain.AddRelationship(new Relationship(domain, "CustomerCases", source, target, RelationshipCardinality.OneToOne, false));
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Domain_AddRelationship_WhenOwnershipTargetAlreadyOwned_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var customer = new Entity(domain, "Customer");
        var agent = new Entity(domain, "Agent");
        var note = new Entity(domain, "Note");
        domain.AddType(customer);
        domain.AddType(agent);
        domain.AddType(note);

        domain.AddRelationship(new Relationship(domain, "CustomerNotes", customer, note, RelationshipCardinality.OneToMany, true));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            domain.AddRelationship(new Relationship(domain, "AgentNotes", agent, note, RelationshipCardinality.OneToMany, true));
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Domain_AddRelationship_AfterRegistration_AllowsValidDefinitionMutation() {
        var domain = DomainTestFactory.CreateDomain();
        var source = new Entity(domain, "Customer");
        var target = new Entity(domain, "SupportCase");
        domain.AddType(source);
        domain.AddType(target);

        var relationship = new Relationship(domain, "CustomerCases", source, target, RelationshipCardinality.OneToMany, false);

        domain.AddRelationship(relationship);

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
        domain.AddType(source);
        domain.AddType(target);

        var relationship = new Relationship(domain, "CustomerCases", source, target, RelationshipCardinality.ManyToMany, false);

        domain.AddRelationship(relationship);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            var mutation = domain.CreateMutation();
            _ = mutation.SetRelationship(relationship, source, target, RelationshipCardinality.ManyToMany, true);
            _ = mutation.Apply();
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Relationship_Mutation_WithDuplicateOwnershipTarget_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var customer = new Entity(domain, "Customer");
        var agent = new Entity(domain, "Agent");
        var note = new Entity(domain, "Note");
        domain.AddType(customer);
        domain.AddType(agent);
        domain.AddType(note);

        domain.AddRelationship(new Relationship(domain, "CustomerNotes", customer, note, RelationshipCardinality.OneToMany, true));

        var second = new Relationship(domain, "AgentNotes", agent, note, RelationshipCardinality.OneToMany, false);

        domain.AddRelationship(second);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            var mutation = domain.CreateMutation();
            _ = mutation.SetRelationship(second, agent, note, RelationshipCardinality.OneToMany, true);
            _ = mutation.Apply();
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Relationship_AddProperty_WithMatchingDomain_AddsProperty() {
        var domain = DomainTestFactory.CreateDomain();
        var source = new Entity(domain, "Customer");
        var target = new Entity(domain, "SupportCase");
        var timestamp = CreatePrimitive(domain, "instant", TypeCategory.Instant);
        domain.AddType(source);
        domain.AddType(target);

        var relationship = new Relationship(domain, "AgentSupportCases", source, target, RelationshipCardinality.ManyToMany, false);

        var assignedAt = new Property(domain, "AssignedAt", timestamp);

        relationship.AddProperty(assignedAt);

        await Assert.That(relationship.Properties.Contains(assignedAt)).IsTrue();
    }

    [Test]
    public async Task Relationship_AddProperty_WithDifferentDomain_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var otherDomain = DomainTestFactory.CreateDomain("Other Domain");
        var source = new Entity(domain, "Customer");
        var target = new Entity(domain, "SupportCase");
        var timestamp = CreatePrimitive(otherDomain, "instant", TypeCategory.Instant);
        domain.AddType(source);
        domain.AddType(target);

        var relationship = new Relationship(domain, "AgentSupportCases", source, target, RelationshipCardinality.ManyToMany, false);

        var assignedAt = new Property(otherDomain, "AssignedAt", timestamp);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            relationship.AddProperty(assignedAt);
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Entity_AddProperty_WithDuplicateName_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "Ticket");
        var stringType = CreatePrimitive(domain, "string", TypeCategory.Text);
        domain.AddType(entity);
        domain.AddType(stringType);

        entity.AddProperty(new Property(domain, "Title", stringType));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            entity.AddProperty(new Property(domain, "Title", stringType));
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Entity_AddRelationship_WhenNotRegisteredInDomain_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var source = new Entity(domain, "Customer");
        var target = new Entity(domain, "SupportCase");
        domain.AddType(source);
        domain.AddType(target);

        var relationship = new Relationship(domain, "CustomerCases", source, target, RelationshipCardinality.OneToOne, false);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            source.AddRelationship(relationship);
            await Task.CompletedTask;
        });
    }

    private static Primitive CreatePrimitive(Domain domain, string name, TypeCategory category = TypeCategory.Primitive) {
        return new Primitive(domain, name, category);
    }

}

public class StageTests {
    [Test]
    public async Task Entity_AddStage_WhenParentEntityHasStagesAndStageHasNoParent_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var parent = new Entity(domain, "Parent");
        var child = new Entity(domain, "Child", parent);

        var parentStage = new Stage(domain, "Open");

        parent.AddStage(parentStage);

        await Assert.That(() => child.AddStage(new Stage(domain, "Draft")))
            .Throws<InvalidOperationException>()
                .WithMessage("Stage 'Draft' on child entity 'Child' must have a parent stage when parent entity 'Parent' defines stages.");
    }

    [Test]
    public async Task Entity_AddStage_WhenParentEntityHasStagesAndDirectParentStageMatch_AddsStage() {
        var domain = DomainTestFactory.CreateDomain();
        var parent = new Entity(domain, "Parent");
        var child = new Entity(domain, "Child", parent);

        var parentStage = new Stage(domain, "Open");
        var childStage = new Stage(domain, "Draft") { Parent = parentStage };
        parent.AddStage(parentStage);
        child.AddStage(childStage);

        await Assert.That(child.Stages.Contains(childStage)).IsTrue();
    }

    [Test]
    public async Task Entity_AddStage_WhenParentEntityHasStagesAndDirectParentStageDoesNotMatch_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var parent = new Entity(domain, "Parent");
        var child = new Entity(domain, "Child", parent);

        var parentStage = new Stage(domain, "Open");
        var childStage = new Stage(domain, "Draft") { Parent = parentStage };
        var grandChildStage = new Stage(domain, "Review") { Parent = childStage };

        parent.AddStage(parentStage);
        child.AddStage(childStage);

        await Assert.That(() => child.AddStage(grandChildStage))
            .Throws<InvalidOperationException>()
                .WithMessage("Stage 'Review' on child entity 'Child' must directly inherit from a stage defined on parent entity 'Parent'.");
    }

    [Test]
    public async Task Stage_GetEffectiveActions_PrefersLocalActionOverInheritedAction() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "Case");

        var parent = new Stage(domain, "Parent");

        var child = new Stage(domain, "Child") { Parent = parent };

        var inheritedEscalate = new DomainAction(domain, "Escalate", entity);

        var localEscalate = new DomainAction(domain, "Escalate", entity);

        parent.AddAction(inheritedEscalate);
        child.AddAction(localEscalate);

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

        grandParent.AddAction(triage);
        parent.AddAction(review);
        child.AddAction(complete);

        var names = child.GetEffectiveActions().Select(action => action.Name).ToArray();

        await Assert.That(names.Length).IsEqualTo(3);
        await Assert.That(names).Contains("Triage");
        await Assert.That(names).Contains("Review");
        await Assert.That(names).Contains("Complete");
    }

    [Test]
    public async Task Stage_AddPolicy_WithDuplicateName_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var stage = new Stage(domain, "Open");

        stage.AddPolicy(new Policy(domain, "RequireTitle"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            stage.AddPolicy(new Policy(domain, "RequireTitle"));
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Stage_AddAction_WithDuplicateName_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "Ticket");
        var stage = new Stage(domain, "Open");

        stage.AddAction(new DomainAction(domain, "Assign", entity));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            stage.AddAction(new DomainAction(domain, "Assign", entity));
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task StageTransitionRequirementAnalyzer_IncludesPropertyPolicyRequirements() {
        var domain = DomainTestFactory.CreateDomain();
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        domain.AddType(stringType);

        var ticket = new Entity(domain, "Ticket");
        var title = new Property(domain, "Title", stringType);

        var titlePolicy = new Policy(domain, "RequireTitleFromProperty");

        titlePolicy.AddRule(new PropertyRule {
            Value = title,
            Constraints = new RequiredConstraint()
        });

        title.AddPolicy(titlePolicy);
        ticket.AddProperty(title);

        var triage = new Stage(domain, "Triage");
        var open = new Stage(domain, "Open");

        ticket.AddStage(triage);
        ticket.AddStage(open);
        domain.AddType(ticket);

        var analysis = StageTransitionRequirementAnalyzer.Analyze(triage, open, ticket);
        var currentRequiredNames = analysis.CurrentRequiredProperties.Select(p => p.Name).ToArray();
        var targetRequiredNames = analysis.TargetRequiredProperties.Select(p => p.Name).ToArray();

        await Assert.That(currentRequiredNames).Contains("Title");
        await Assert.That(targetRequiredNames).Contains("Title");
    }

    [Test]
    public async Task DomainModelValidationAnalyzer_StageTransitionRequest_ProducesMetadata() {
        var domain = DomainTestFactory.CreateDomain();
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        domain.AddType(stringType);

        var ticket = new Entity(domain, "Ticket");
        var title = new Property(domain, "Title", stringType);
        var triage = new Stage(domain, "Triage");
        var open = new Stage(domain, "Open");
        var openPolicy = new Policy(domain, "RequireTitleAtOpen");

        openPolicy.AddRule(new PropertyRule {
            Value = title,
            Constraints = new RequiredConstraint()
        });

        ticket.AddProperty(title);
        open.AddPolicy(openPolicy);
        ticket.AddStage(triage);
        ticket.AddStage(open);

        var request = new StageTransitionRequirementAnalysisRequest(triage, open, ticket);
        var builder = new AnalyzerBuilder();
        builder.UseDomainModelValidation();

        var analysis = builder.Build().Analyze(request);
        var newlyRequiredNames = analysis
            .GetStageTransitionRequirements(request)
            .NewlyRequiredProperties
            .Select(property => property.Name)
            .ToArray();

        await Assert.That(newlyRequiredNames).Contains("Title");
    }
}

public class ActionAndEventMutationTests {
    [Test]
    public async Task Entity_AddAction_WithDifferentEntity_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var owner = new Entity(domain, "SupportCase");
        var other = new Entity(domain, "Ticket");
        var action = new DomainAction(domain, "Assign", other);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            owner.AddAction(action);
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Stage_AddAction_WhenAttachedAndEntityMismatched_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var owner = new Entity(domain, "SupportCase");
        var other = new Entity(domain, "Ticket");
        var stage = new Stage(domain, "New");

        owner.AddStage(stage);

        var mismatchedAction = new DomainAction(domain, "Assign", other);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            stage.AddAction(mismatchedAction);
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Event_AddProperty_WithDuplicateName_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var eventType = new Event(domain, "CaseAssigned");
        var stringType = new Primitive(domain, "string", TypeCategory.Text);

        eventType.AddProperty(new Property(domain, "AssignedTo", stringType));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            eventType.AddProperty(new Property(domain, "AssignedTo", stringType));
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Action_AddParameter_WithDuplicatePropertyName_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "SupportCase");
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var action = new DomainAction(domain, "AddNote", entity);

        action.AddParameter(new Property(domain, "NoteText", stringType));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            action.AddParameter(new Property(domain, "NoteText", stringType));
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Action_AddEffect_CreateEntityInstanceWithMismatchedInitialStage_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "SupportCase");
        var note = new Entity(domain, "Note");
        var action = new DomainAction(domain, "AddNote", entity);
        var wrongStage = new Stage(domain, "Wrong");

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            action.AddEffect(new CreateEntityInstance {
                EntityType = note,
                InitialStage = wrongStage
            });
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Action_AddEffect_StageTransitionToForeignEntityStage_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var sourceEntity = new Entity(domain, "SupportCase");
        var targetEntity = new Entity(domain, "Note");
        var sourceAction = new DomainAction(domain, "Assign", sourceEntity);
        var foreignStage = new Stage(domain, "Draft");

        sourceEntity.AddStage(new Stage(domain, "New"));
        targetEntity.AddStage(foreignStage);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            sourceAction.AddEffect(new StageTransition {
                TargetStage = foreignStage
            });
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Action_AddEffect_PublishEventWithoutRequiredBindings_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "SupportCase");
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var action = new DomainAction(domain, "Assign", entity);
        var @event = new Event(domain, "CaseAssigned");
        @event.AddProperty(new Property(domain, "AssignedTo", stringType));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            action.AddEffect(new PublishEvent { Event = @event });
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Action_AddEffect_InvokeActionWithoutRequiredBindings_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "SupportCase");
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var targetAction = new DomainAction(domain, "Resolve", entity);
        targetAction.AddParameter(new Property(domain, "Reason", stringType));

        var sourceAction = new DomainAction(domain, "Escalate", entity);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            sourceAction.AddEffect(new InvokeAction { TargetAction = targetAction });
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Action_AddEffect_InvokeActionWithBoundParameters_AddsEffect() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "SupportCase");
        var stringType = new Primitive(domain, "string", TypeCategory.Text);

        var targetAction = new DomainAction(domain, "Resolve", entity);
        var reasonParameter = new Property(domain, "Reason", stringType);
        targetAction.AddParameter(reasonParameter);

        var sourceAction = new DomainAction(domain, "Escalate", entity);
        var sourceReason = new Property(domain, "SourceReason", stringType);

        var invoke = new InvokeAction { TargetAction = targetAction };
        invoke.BindParameter(reasonParameter, sourceReason);

        sourceAction.AddEffect(invoke);

        await Assert.That(sourceAction.Effects.Contains(invoke)).IsTrue();
    }

    [Test]
    public async Task Property_AddPolicy_WithDuplicateName_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var property = new Property(domain, "Title", stringType);

        property.AddPolicy(new Policy(domain, "RequireTitle"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            property.AddPolicy(new Policy(domain, "RequireTitle"));
            await Task.CompletedTask;
        });
    }
}