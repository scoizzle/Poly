using Poly.Data.Modeling;
using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;
using Poly.Data.Modeling.Validation.Constraints;
using Poly.Introspection;

namespace Poly.Tests.Data.Modeling;

public class CreateEntityInstanceTests {
    [Test]
    public async Task CreateEntityInstance_RequiredParameters_IncludeEntityPropertiesWithRequiredConstraint() {
        var domain = DomainTestFactory.CreateDomain();
        var note = CreateEntity(domain, "Note");
        var stringType = CreatePrimitive(domain, "string");

        var title = new Property {
            Domain = domain,
            Name = "Title",
            Type = stringType,
            Constraints = [new RequiredConstraint()]
        };

        var body = new Property {
            Domain = domain,
            Name = "Body",
            Type = stringType
        };

        note.AddProperty(title);
        note.AddProperty(body);

        var create = new CreateEntityInstance {
            EntityType = note,
            OwnershipRelationship = CreateOwnership(domain, note),
            InitialStage = null
        };

        var requiredNames = create.RequiredParameters.Cast<Property>().Select(p => p.Name).ToArray();

        await Assert.That(requiredNames.Length).IsEqualTo(1);
        await Assert.That(requiredNames).Contains("Title");
    }

    [Test]
    public async Task CreateEntityInstance_RequiredParameters_IncludeInitialStagePolicyRequiredProperties() {
        var domain = DomainTestFactory.CreateDomain();
        var note = CreateEntity(domain, "Note");
        var stringType = CreatePrimitive(domain, "string");

        var title = new Property {
            Domain = domain,
            Name = "Title",
            Type = stringType
        };

        note.AddProperty(title);

        var draft = new Stage {
            Domain = domain,
            Name = "Draft"
        };

        var policy = new Policy {
            Domain = domain,
            Name = "RequireTitle"
        };

        policy.AddRule(new PropertyRule {
            Value = title,
            Constraints = new ConstraintSet(
                new LengthConstraint(minLength: 1),
                new RequiredConstraint())
        });

        draft.AddPolicy(policy);
        note.AddStage(draft);

        var create = new CreateEntityInstance {
            EntityType = note,
            OwnershipRelationship = CreateOwnership(domain, note),
            InitialStage = draft
        };

        var requiredNames = create.GetRequiredProperties().Select(p => p.Name).ToArray();

        await Assert.That(requiredNames.Length).IsEqualTo(1);
        await Assert.That(requiredNames).Contains("Title");
    }

    [Test]
    public async Task CreateEntityInstance_RequiredParameters_IncludeInheritedInitialStagePolicyRequirements() {
        var domain = DomainTestFactory.CreateDomain();
        var note = CreateEntity(domain, "Note");
        var stringType = CreatePrimitive(domain, "string");

        var title = new Property {
            Domain = domain,
            Name = "Title",
            Type = stringType
        };

        note.AddProperty(title);

        var parent = new Stage {
            Domain = domain,
            Name = "Parent"
        };

        var child = new Stage {
            Domain = domain,
            Name = "Child",
            Parent = parent
        };

        var parentPolicy = new Policy {
            Domain = domain,
            Name = "RequireTitle"
        };

        parentPolicy.AddRule(new PropertyRule {
            Value = title,
            Constraints = new RequiredConstraint()
        });

        parent.AddPolicy(parentPolicy);
        note.AddStage(parent);
        note.AddStage(child);

        var create = new CreateEntityInstance {
            EntityType = note,
            OwnershipRelationship = CreateOwnership(domain, note),
            InitialStage = child
        };

        var requiredNames = create.GetRequiredProperties().Select(p => p.Name).ToArray();

        await Assert.That(requiredNames.Length).IsEqualTo(1);
        await Assert.That(requiredNames).Contains("Title");
    }

    [Test]
    public async Task CreateEntityInstance_WhenEffectInitialStageIsNull_IncludesEntityPolicyRequirements() {
        var domain = DomainTestFactory.CreateDomain();
        var note = CreateEntity(domain, "Note");
        var stringType = CreatePrimitive(domain, "string");
        var title = new Property {
            Domain = domain,
            Name = "Title",
            Type = stringType
        };

        var rootPolicy = new Policy {
            Domain = domain,
            Name = "RequireTitle"
        };

        rootPolicy.AddRule(new PropertyRule {
            Value = title,
            Constraints = new RequiredConstraint()
        });

        note.AddProperty(title);
        note.AddPolicy(rootPolicy);

        var create = new CreateEntityInstance {
            EntityType = note,
            OwnershipRelationship = CreateOwnership(domain, note),
            InitialStage = null
        };

        var requiredNames = create.GetRequiredProperties().Select(p => p.Name).ToArray();

        await Assert.That(requiredNames.Length).IsEqualTo(1);
        await Assert.That(requiredNames).Contains("Title");
    }

    private static Entity CreateEntity(Domain domain, string name) {
        var entity = new Entity {
            Domain = domain,
            Name = name
        };

        domain.AddType(entity);
        return entity;
    }

    private static Primitive CreatePrimitive(Domain domain, string name, TypeCategory category = TypeCategory.Primitive) {
        var primitive = new Primitive {
            Domain = domain,
            Name = name,
            Category = category
        };

        domain.AddType(primitive);
        return primitive;
    }

    private static Relationship CreateOwnership(Domain domain, Entity target) {
        var owner = CreateEntity(domain, $"{target.Name}Owner");

        var relationship = new Relationship {
            Domain = domain,
            Name = $"{owner.Name}{target.Name}Ownership",
            Source = owner,
            Target = target,
            Cardinality = RelationshipCardinality.OneToMany,
            SourceOwnsTarget = true
        };

        domain.AddRelationship(relationship);
        owner.AddRelationship(relationship);

        return relationship;
    }
}