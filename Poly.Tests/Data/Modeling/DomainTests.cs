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

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            MutationApply.AddType(parentDomain, customer);
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Domain_AddType_WithDuplicateName_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();

        MutationApply.AddType(domain, CreatePrimitive(domain, "Customer"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            MutationApply.AddType(domain, CreatePrimitive(domain, "Customer"));
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

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            MutationApply.AddRelationship(domain, relationship);
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Domain_AddRelationship_WithForeignEndpoint_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var otherDomain = DomainTestFactory.CreateDomain("Other Domain");
        var customer = new Entity(domain, "Customer");
        var invoice = new Entity(otherDomain, "Invoice");
        MutationApply.AddType(domain, customer);

        var relationship = new Relationship(domain, "CustomerInvoices", customer, invoice, RelationshipCardinality.OneToMany, false);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            MutationApply.AddRelationship(domain, relationship);
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Domain_AddRelationship_WithDuplicateName_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var source = new Entity(domain, "Customer");
        var target = new Entity(domain, "SupportCase");
        MutationApply.AddType(domain, source);
        MutationApply.AddType(domain, target);

        MutationApply.AddRelationship(domain, new Relationship(domain, "CustomerCases", source, target, RelationshipCardinality.OneToOne, false));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            MutationApply.AddRelationship(domain, new Relationship(domain, "CustomerCases", source, target, RelationshipCardinality.OneToOne, false));
            await Task.CompletedTask;
        });
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

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            MutationApply.AddRelationship(domain, new Relationship(domain, "AgentNotes", agent, note, RelationshipCardinality.OneToMany, true));
            await Task.CompletedTask;
        });
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
        MutationApply.AddType(domain, customer);
        MutationApply.AddType(domain, agent);
        MutationApply.AddType(domain, note);

        MutationApply.AddRelationship(domain, new Relationship(domain, "CustomerNotes", customer, note, RelationshipCardinality.OneToMany, true));

        var second = new Relationship(domain, "AgentNotes", agent, note, RelationshipCardinality.OneToMany, false);

        MutationApply.AddRelationship(domain, second);

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

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            MutationApply.AddProperty(relationship, assignedAt);
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Entity_AddProperty_WithDuplicateName_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "Ticket");
        var stringType = CreatePrimitive(domain, "string", TypeCategory.Text);
        MutationApply.AddType(domain, entity);
        MutationApply.AddType(domain, stringType);

        MutationApply.AddProperty(entity, new Property(domain, "Title", stringType));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            MutationApply.AddProperty(entity, new Property(domain, "Title", stringType));
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Entity_AddRelationship_WhenNotRegisteredInDomain_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var source = new Entity(domain, "Customer");
        var target = new Entity(domain, "SupportCase");
        MutationApply.AddType(domain, source);
        MutationApply.AddType(domain, target);

        var relationship = new Relationship(domain, "CustomerCases", source, target, RelationshipCardinality.OneToOne, false);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            MutationApply.AddRelationship(source, relationship);
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
        MutationApply.AddType(domain, parent);
        MutationApply.AddType(domain, child);

        var parentStage = new Stage(domain, "Open");

        MutationApply.AddStage(parent, parentStage);

        await Assert.That(() => MutationApply.AddStage(child, new Stage(domain, "Draft")))
            .Throws<InvalidOperationException>()
                .WithMessage("Stage 'Draft' on child entity 'Child' must have a parent stage when parent entity 'Parent' defines stages.");
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
        MutationApply.AddType(domain, parent);
        MutationApply.AddType(domain, child);

        var parentStage = new Stage(domain, "Open");
        var childStage = new Stage(domain, "Draft") { Parent = parentStage };
        var grandChildStage = new Stage(domain, "Review") { Parent = childStage };

        MutationApply.AddStage(parent, parentStage);
        MutationApply.AddStage(child, childStage);

        await Assert.That(() => MutationApply.AddStage(child, grandChildStage))
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
        MutationApply.AddType(domain, owner);
        MutationApply.AddStage(owner, stage);

        MutationApply.AddPolicy(stage, new Policy(domain, "RequireTitle"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            MutationApply.AddPolicy(stage, new Policy(domain, "RequireTitle"));
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Stage_AddAction_WithDuplicateName_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "Ticket");
        var stage = new Stage(domain, "Open");
        MutationApply.AddType(domain, entity);
        MutationApply.AddStage(entity, stage);

        MutationApply.AddAction(stage, new DomainAction(domain, "Assign", entity));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            MutationApply.AddAction(stage, new DomainAction(domain, "Assign", entity));
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task StageTransitionRequirementAnalyzer_IncludesPropertyPolicyRequirements() {
        var domain = DomainTestFactory.CreateDomain();
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        MutationApply.AddType(domain, stringType);

        var ticket = new Entity(domain, "Ticket");
        var title = new Property(domain, "Title", stringType);

        var titlePolicy = new Policy(domain, "RequireTitleFromProperty");

        MutationApply.AddRule(titlePolicy, new PropertyRule {
            Value = title,
            Constraints = new RequiredConstraint()
        });

        MutationApply.AddPolicy(title, titlePolicy);
        MutationApply.AddProperty(ticket, title);

        var triage = new Stage(domain, "Triage");
        var open = new Stage(domain, "Open");

        MutationApply.AddStage(ticket, triage);
        MutationApply.AddStage(ticket, open);
        MutationApply.AddType(domain, ticket);

        // var analysis = StageTransitionRequirementAnalyzer.Analyze(triage, open, ticket);
        // var currentRequiredNames = analysis.CurrentRequiredProperties.Select(p => p.Name).ToArray();
        // var targetRequiredNames = analysis.TargetRequiredProperties.Select(p => p.Name).ToArray();

        // await Assert.That(currentRequiredNames).Contains("Title");
        // await Assert.That(targetRequiredNames).Contains("Title");
    }

    [Test]
    public async Task DomainModelValidationAnalyzer_StageTransitionRequest_ProducesMetadata() {
        var domain = DomainTestFactory.CreateDomain();
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        MutationApply.AddType(domain, stringType);

        var ticket = new Entity(domain, "Ticket");
        var title = new Property(domain, "Title", stringType);
        var triage = new Stage(domain, "Triage");
        var open = new Stage(domain, "Open");
        var openPolicy = new Policy(domain, "RequireTitleAtOpen");

        MutationApply.AddRule(openPolicy, new PropertyRule {
            Value = title,
            Constraints = new RequiredConstraint()
        });

        MutationApply.AddProperty(ticket, title);
        MutationApply.AddPolicy(open, openPolicy);
        MutationApply.AddStage(ticket, triage);
        MutationApply.AddStage(ticket, open);

        // var request = new StageTransitionRequirementAnalysisRequest(triage, open, ticket);
        // var builder = new AnalyzerBuilder();
        // builder.UseDomainModelValidation();

        // var analysis = builder.Build().Analyze(request);
        // var newlyRequiredNames = analysis
        //     .GetStageTransitionRequirements(request)
        //     .NewlyRequiredProperties
        //     .Select(property => property.Name)
        //     .ToArray();

        // await Assert.That(newlyRequiredNames).Contains("Title");
    }
}

public class ActionAndEventMutationTests {
    [Test]
    public async Task Entity_AddAction_WithDifferentEntity_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var owner = new Entity(domain, "SupportCase");
        var other = new Entity(domain, "Ticket");
        MutationApply.AddType(domain, owner);
        MutationApply.AddType(domain, other);
        var action = new DomainAction(domain, "Assign", other);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            MutationApply.AddAction(owner, action);
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Stage_AddAction_WhenAttachedAndEntityMismatched_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var owner = new Entity(domain, "SupportCase");
        var other = new Entity(domain, "Ticket");
        MutationApply.AddType(domain, owner);
        MutationApply.AddType(domain, other);
        var stage = new Stage(domain, "New");

        MutationApply.AddStage(owner, stage);

        var mismatchedAction = new DomainAction(domain, "Assign", other);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            MutationApply.AddAction(stage, mismatchedAction);
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Event_AddProperty_WithDuplicateName_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var eventType = new Event(domain, "CaseAssigned");
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        MutationApply.AddType(domain, eventType);

        MutationApply.AddProperty(eventType, new Property(domain, "AssignedTo", stringType));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            MutationApply.AddProperty(eventType, new Property(domain, "AssignedTo", stringType));
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Action_AddParameter_WithDuplicatePropertyName_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "SupportCase");
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var action = new DomainAction(domain, "AddNote", entity);
        MutationApply.AddType(domain, entity);
        MutationApply.AddAction(entity, action);

        MutationApply.AddParameter(action, new Property(domain, "NoteText", stringType));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            MutationApply.AddParameter(action, new Property(domain, "NoteText", stringType));
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
        MutationApply.AddType(domain, entity);
        MutationApply.AddType(domain, note);
        MutationApply.AddAction(entity, action);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            MutationApply.AddEffect(action, new CreateEntityInstance {
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
        MutationApply.AddType(domain, sourceEntity);
        MutationApply.AddType(domain, targetEntity);
        MutationApply.AddAction(sourceEntity, sourceAction);

        MutationApply.AddStage(sourceEntity, new Stage(domain, "New"));
        MutationApply.AddStage(targetEntity, foreignStage);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            MutationApply.AddEffect(sourceAction, new StageTransition {
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
        MutationApply.AddType(domain, entity);
        MutationApply.AddType(domain, @event);
        MutationApply.AddAction(entity, action);
        MutationApply.AddProperty(@event, new Property(domain, "AssignedTo", stringType));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            MutationApply.AddEffect(action, new PublishEvent { Event = @event });
            await Task.CompletedTask;
        });
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

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            MutationApply.AddEffect(sourceAction, new InvokeAction { TargetAction = targetAction });
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
        MutationApply.AddParameter(targetAction, reasonParameter);

        var sourceAction = new DomainAction(domain, "Escalate", entity);
        var sourceReason = new Property(domain, "SourceReason", stringType);

        var invoke = new InvokeAction { TargetAction = targetAction };
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

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            MutationApply.AddPolicy(property, new Policy(domain, "RequireTitle"));
            await Task.CompletedTask;
        });
    }
}