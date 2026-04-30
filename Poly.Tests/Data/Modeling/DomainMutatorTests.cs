using Poly.Data.Modeling;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;
using Poly.Data.Modeling.Validation.Constraints;
using Poly.Introspection;

namespace Poly.Tests.Data.Modeling;

public class DomainMutationTests {
    [Test]
    public async Task AddType_ValidMutation_Commits() {
        var domain = new Domain("Support");
        var mutation = domain.CreateMutation();
        var stringType = new Primitive(domain, "string", TypeCategory.Text);

        _ = mutation.AddType(stringType);
        var result = mutation.Apply();

        await Assert.That(result.HasErrors).IsFalse();
        await Assert.That(domain.Types.Contains(stringType)).IsTrue();
    }

    [Test]
    public async Task AddPolicy_WhenAnalyzerFails_RollsBackMutation() {
        var domain = new Domain("Support");
        var mutation = domain.CreateMutation();

        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var parent = new Entity(domain, "Parent");
        var child = new Entity(domain, "Child");
        var parentOnlyProperty = new Property(domain, "ExternalValue", stringType);

        _ = mutation.AddType(stringType);
        _ = mutation.AddType(parent);
        _ = mutation.AddType(child);
        _ = mutation.AddProperty(parent, parentOnlyProperty);

        var invalidPolicy = new Policy(domain, "MissingPropertyPolicy");
        _ = mutation.AddRule(invalidPolicy, new PropertyRule {
            Value = parentOnlyProperty,
            Constraints = new RequiredConstraint()
        });

        _ = mutation.AddPolicy(child, invalidPolicy);
        var result = mutation.Apply();
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("ExternalValue"));
        await Assert.That(error is not null).IsTrue();
        await Assert.That(child.Policies.Contains(invalidPolicy)).IsFalse();
    }

    [Test]
    public async Task AddProperty_CommitsThroughSharedAnalyzer() {
        var domain = new Domain("Support");
        var mutation = domain.CreateMutation();

        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var entity = new Entity(domain, "Ticket");
        var title = new Property(domain, "Title", stringType);

        _ = mutation.AddType(stringType);
        _ = mutation.AddType(entity);

        _ = mutation.AddProperty(entity, title);
        var result = mutation.Apply();

        await Assert.That(result.HasErrors).IsFalse();
        await Assert.That(entity.Properties.Contains(title)).IsTrue();
    }

    [Test]
    public async Task SetRelationship_WithInvalidOwnership_ThrowsAndPreservesState() {
        var domain = new Domain("Support");
        var mutation = domain.CreateMutation();

        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var source = new Entity(domain, "Source");
        var target = new Entity(domain, "Target");

        _ = mutation.AddType(stringType);
        _ = mutation.AddType(source);
        _ = mutation.AddType(target);

        var relationship = new Relationship(domain, "SourceTarget", source, target, RelationshipCardinality.OneToOne, true);

        _ = mutation.AddRelationship(relationship);
        _ = mutation.AddEntityRelationship(source, relationship);

        _ = mutation.SetRelationship(relationship, source, target, RelationshipCardinality.ManyToMany, true);
        var result = mutation.Apply();
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("must be one-to-one or one-to-many"));
        await Assert.That(error is not null).IsTrue();
        await Assert.That(relationship.Cardinality).IsEqualTo(RelationshipCardinality.OneToOne);
    }

    [Test]
    public async Task Mutation_AfterApply_IsNotReusable() {
        var domain = new Domain("Support");
        var mutation = domain.CreateMutation();

        _ = mutation.AddType(new Primitive(domain, "string", TypeCategory.Text));
        _ = mutation.Apply();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            _ = mutation.AddType(new Primitive(domain, "int", TypeCategory.Integer));
            await Task.CompletedTask;
        });
    }
}