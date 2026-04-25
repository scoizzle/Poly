using Poly.Data.Modeling;
using Poly.Data.Modeling.TypeSystem;
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
    public async Task Domain_AddRelationship_WithMatchingDomain_AddsRelationship() {
        var domain = DomainTestFactory.CreateDomain();
        var customer = CreatePrimitive(domain, "Customer");
        var invoice = CreatePrimitive(domain, "Invoice");
        domain.AddType(customer);
        domain.AddType(invoice);

        var relationship = new Relationship {
            Domain = domain,
            Name = "CustomerInvoices",
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

        var relationship = new Relationship {
            Domain = otherDomain,
            Name = "CustomerInvoices",
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

        var relationship = new Relationship {
            Domain = domain,
            Name = "CustomerInvoices",
            Source = customer,
            Target = invoice
        };

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            domain.AddRelationship(relationship);
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Relationship_AddProperty_WithMatchingDomain_AddsProperty() {
        var domain = DomainTestFactory.CreateDomain();
        var source = CreatePrimitive(domain, "Customer");
        var target = CreatePrimitive(domain, "SupportCase");
        var timestamp = CreatePrimitive(domain, "instant", TypeCategory.Instant);

        var relationship = new Relationship {
            Domain = domain,
            Name = "AgentSupportCases",
            Source = source,
            Target = target,
            Cardinality = RelationshipCardinality.ManyToMany
        };

        var assignedAt = new Property {
            Domain = domain,
            Name = "AssignedAt",
            Type = timestamp
        };

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

        var relationship = new Relationship {
            Domain = domain,
            Name = "AgentSupportCases",
            Source = source,
            Target = target,
            Cardinality = RelationshipCardinality.ManyToMany
        };

        var assignedAt = new Property {
            Domain = otherDomain,
            Name = "AssignedAt",
            Type = timestamp
        };

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            relationship.AddProperty(assignedAt);
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
    public async Task Stage_GetEffectiveActions_PrefersLocalActionOverInheritedAction() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity {
            Domain = domain,
            Name = "Case"
        };

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
        var entity = new Entity {
            Domain = domain,
            Name = "Case"
        };

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
}