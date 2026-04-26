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
        var customer = CreatePrimitive(domain, "Customer");
        var invoice = CreatePrimitive(domain, "Invoice");
        domain.AddType(customer);
        domain.AddType(invoice);

        var relationship = new Relationship(domain, "CustomerInvoices") {
            Source = customer,
            Target = invoice,
            Cardinality = RelationshipCardinality.OneToMany
        };

        domain.AddRelationship(relationship);

        await Assert.That(domain.Relationships.Contains(relationship)).IsTrue();
    }

    [Test]
    public async Task Domain_AddRelationship_WithDifferentDomain_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var otherDomain = DomainTestFactory.CreateDomain("Other Domain");
        var customer = CreatePrimitive(domain, "Customer");
        var invoice = CreatePrimitive(domain, "Invoice");

        var relationship = new Relationship(otherDomain, "CustomerInvoices") {
            Source = customer,
            Target = invoice
        };

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            domain.AddRelationship(relationship);
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Domain_AddRelationship_WithForeignEndpoint_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var otherDomain = DomainTestFactory.CreateDomain("Other Domain");
        var customer = CreatePrimitive(domain, "Customer");
        var invoice = CreatePrimitive(otherDomain, "Invoice");
        domain.AddType(customer);

        var relationship = new Relationship(domain, "CustomerInvoices") {
            Source = customer,
            Target = invoice
        };

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

        domain.AddRelationship(new Relationship(domain, "CustomerCases") {
            Source = source,
            Target = target
        });

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            domain.AddRelationship(new Relationship(domain, "CustomerCases") {
                Source = source,
                Target = target
            });
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

        domain.AddRelationship(new Relationship(domain, "CustomerNotes") {
            Source = customer,
            Target = note,
            Cardinality = RelationshipCardinality.OneToMany,
            SourceOwnsTarget = true
        });

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            domain.AddRelationship(new Relationship(domain, "AgentNotes") {
                Source = agent,
                Target = note,
                Cardinality = RelationshipCardinality.OneToMany,
                SourceOwnsTarget = true
            });
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Relationship_AddProperty_WithMatchingDomain_AddsProperty() {
        var domain = DomainTestFactory.CreateDomain();
        var source = CreatePrimitive(domain, "Customer");
        var target = CreatePrimitive(domain, "SupportCase");
        var timestamp = CreatePrimitive(domain, "instant", TypeCategory.Instant);

        var relationship = new Relationship(domain, "AgentSupportCases") {
            Source = source,
            Target = target,
            Cardinality = RelationshipCardinality.ManyToMany
        };

        var assignedAt = new Property(domain, "AssignedAt", timestamp);

        relationship.AddProperty(assignedAt);

        await Assert.That(relationship.Properties.Contains(assignedAt)).IsTrue();
    }

    [Test]
    public async Task Relationship_AddProperty_WithDifferentDomain_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var otherDomain = DomainTestFactory.CreateDomain("Other Domain");
        var source = CreatePrimitive(domain, "Customer");
        var target = CreatePrimitive(domain, "SupportCase");
        var timestamp = CreatePrimitive(otherDomain, "instant", TypeCategory.Instant);

        var relationship = new Relationship(domain, "AgentSupportCases") {
            Source = source,
            Target = target,
            Cardinality = RelationshipCardinality.ManyToMany
        };

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

        var relationship = new Relationship(domain, "CustomerCases") {
            Source = source,
            Target = target
        };

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            source.AddRelationship(relationship);
            await Task.CompletedTask;
        });
    }

    private static Primitive CreatePrimitive(Domain domain, string name, TypeCategory category = TypeCategory.Primitive) {
        return new Primitive {
            Domain = domain,
            Name = name,
            Category = category
        };
    }

}

public class StageTests {
    [Test]
    public async Task Entity_AddStage_WhenParentEntityHasStagesAndStageHasNoParent_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var parent = new Entity(domain, "Parent");
        var child = new Entity(domain, "Child", parent);

        var parentStage = new Stage {
            Name = "Open",
            Domain = domain
        };

        parent.AddStage(parentStage);

        await Assert.That(() => child.AddStage(new Stage {
            Name = "Draft",
            Domain = domain
        }))
            .Throws<InvalidOperationException>()
                .WithMessage("Stage 'Draft' on child entity 'Child' must have a parent stage when parent entity 'Parent' defines stages.");
    }

    [Test]
    public async Task Entity_AddStage_WhenParentEntityHasStagesAndDirectParentStageMatch_AddsStage() {
        var domain = DomainTestFactory.CreateDomain();
        var parent = new Entity(domain, "Parent");
        var child = new Entity(domain, "Child", parent);

        var parentStage = new Stage {
            Name = "Open",
            Domain = domain
        };
        var childStage = new Stage {
            Name = "Draft",
            Domain = domain,
            Parent = parentStage
        };
        parent.AddStage(parentStage);
        child.AddStage(childStage);

        await Assert.That(child.Stages.Contains(childStage)).IsTrue();
    }

    [Test]
    public async Task Entity_AddStage_WhenParentEntityHasStagesAndDirectParentStageDoesNotMatch_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var parent = new Entity(domain, "Parent");
        var child = new Entity(domain, "Child", parent);

        var parentStage = new Stage {
            Name = "Open",
            Domain = domain
        };
        var childStage = new Stage {
            Name = "Draft",
            Domain = domain,
            Parent = parentStage
        };
        var grandChildStage = new Stage {
            Name = "Review",
            Domain = domain,
            Parent = childStage
        };

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

        var parent = new Stage {
            Name = "Parent",
            Domain = domain
        };

        var child = new Stage {
            Name = "Child",
            Domain = domain,
            Parent = parent
        };

        var inheritedEscalate = new DomainAction {
            Domain = domain,
            Entity = entity,
            Name = "Escalate"
        };

        var localEscalate = new DomainAction {
            Domain = domain,
            Entity = entity,
            Name = "Escalate"
        };

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

        var grandParent = new Stage {
            Name = "Grand Parent",
            Domain = domain
        };

        var parent = new Stage {
            Name = "Parent",
            Domain = domain,
            Parent = grandParent
        };

        var child = new Stage {
            Name = "Child",
            Domain = domain,
            Parent = parent
        };

        var triage = new DomainAction {
            Domain = domain,
            Entity = entity,
            Name = "Triage"
        };

        var review = new DomainAction {
            Domain = domain,
            Entity = entity,
            Name = "Review"
        };

        var complete = new DomainAction {
            Domain = domain,
            Entity = entity,
            Name = "Complete"
        };

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
        var stage = new Stage {
            Name = "Open",
            Domain = domain
        };

        stage.AddPolicy(new Policy {
            Domain = domain,
            Name = "RequireTitle"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            stage.AddPolicy(new Policy {
                Domain = domain,
                Name = "RequireTitle"
            });
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Stage_AddAction_WithDuplicateName_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "Ticket");
        var stage = new Stage {
            Name = "Open",
            Domain = domain
        };

        stage.AddAction(new DomainAction {
            Domain = domain,
            Entity = entity,
            Name = "Assign"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            stage.AddAction(new DomainAction {
                Domain = domain,
                Entity = entity,
                Name = "Assign"
            });
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task StageTransitionRequirementAnalyzer_IncludesPropertyPolicyRequirements() {
        var domain = DomainTestFactory.CreateDomain();
        var stringType = new Primitive {
            Domain = domain,
            Name = "string",
            Category = TypeCategory.Text
        };
        domain.AddType(stringType);

        var ticket = new Entity(domain, "Ticket");
        var title = new Property(domain, "Title", stringType);

        var titlePolicy = new Policy {
            Domain = domain,
            Name = "RequireTitleFromProperty"
        };

        titlePolicy.AddRule(new PropertyRule {
            Value = title,
            Constraints = new RequiredConstraint()
        });

        title.AddPolicy(titlePolicy);
        ticket.AddProperty(title);

        var triage = new Stage {
            Name = "Triage",
            Domain = domain
        };
        var open = new Stage {
            Name = "Open",
            Domain = domain
        };

        ticket.AddStage(triage);
        ticket.AddStage(open);
        domain.AddType(ticket);

        var analysis = StageTransitionRequirementAnalyzer.Analyze(triage, open, ticket);
        var currentRequiredNames = analysis.CurrentRequiredProperties.Select(p => p.Name).ToArray();
        var targetRequiredNames = analysis.TargetRequiredProperties.Select(p => p.Name).ToArray();

        await Assert.That(currentRequiredNames).Contains("Title");
        await Assert.That(targetRequiredNames).Contains("Title");
    }
}

public class ActionAndEventMutationTests {
    [Test]
    public async Task Event_AddProperty_WithDuplicateName_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var eventType = new Event {
            Domain = domain,
            Name = "CaseAssigned"
        };
        var stringType = new Primitive {
            Domain = domain,
            Name = "string",
            Category = TypeCategory.Text
        };

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
        var stringType = new Primitive {
            Domain = domain,
            Name = "string",
            Category = TypeCategory.Text
        };
        var action = new DomainAction {
            Domain = domain,
            Entity = entity,
            Name = "AddNote"
        };

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
        var action = new DomainAction {
            Domain = domain,
            Entity = entity,
            Name = "AddNote"
        };
        var wrongStage = new Stage {
            Domain = domain,
            Name = "Wrong"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            action.AddEffect(new CreateEntityInstance {
                EntityType = note,
                InitialStage = wrongStage
            });
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Property_AddPolicy_WithDuplicateName_ThrowsInvalidOperationException() {
        var domain = DomainTestFactory.CreateDomain();
        var stringType = new Primitive {
            Domain = domain,
            Name = "string",
            Category = TypeCategory.Text
        };
        var property = new Property(domain, "Title", stringType);

        property.AddPolicy(new Policy {
            Domain = domain,
            Name = "RequireTitle"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            property.AddPolicy(new Policy {
                Domain = domain,
                Name = "RequireTitle"
            });
            await Task.CompletedTask;
        });
    }
}