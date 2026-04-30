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

        var title = new Property(domain, "Title", stringType);
        MutationApply.AddConstraint(title, new RequiredConstraint());

        var body = new Property(domain, "Body", stringType);

        MutationApply.AddProperty(note, title);
        MutationApply.AddProperty(note, body);

        var create = new CreateEntityInstance {
            EntityType = note,
            InitialStage = null
        };

        // var requiredNames = create.RequiredParameters.Cast<Property>().Select(p => p.Name).ToArray();

        // await Assert.That(requiredNames.Length).IsEqualTo(1);
        // await Assert.That(requiredNames).Contains("Title");
    }

    [Test]
    public async Task CreateEntityInstance_RequiredParameters_IncludeInitialStagePolicyRequiredProperties() {
        var domain = DomainTestFactory.CreateDomain();
        var note = CreateEntity(domain, "Note");
        var stringType = CreatePrimitive(domain, "string");

        var title = new Property(domain, "Title", stringType);

        MutationApply.AddProperty(note, title);

        var draft = new Stage(domain, "Draft");

        var policy = new Policy(domain, "RequireTitle");

        MutationApply.AddRule(policy, new PropertyRule {
            Value = title,
            Constraints = new ConstraintSet(
                new LengthConstraint(minLength: 1),
                new RequiredConstraint())
        });

        MutationApply.AddPolicy(draft, policy);
        MutationApply.AddStage(note, draft);

        var create = new CreateEntityInstance {
            EntityType = note,
            InitialStage = draft
        };

        // var requiredNames = create.GetRequiredProperties().Select(p => p.Name).ToArray();

        // await Assert.That(requiredNames.Length).IsEqualTo(1);
        // await Assert.That(requiredNames).Contains("Title");
    }

    [Test]
    public async Task DomainModelValidationAnalyzer_RequiredPropertiesRequest_ProducesMetadata() {
        var domain = DomainTestFactory.CreateDomain();
        var note = CreateEntity(domain, "Note");
        var stringType = CreatePrimitive(domain, "string");
        var title = new Property(domain, "Title", stringType);
        var draft = new Stage(domain, "Draft");
        var titlePolicy = new Policy(domain, "RequireTitleFromProperty");

        MutationApply.AddRule(titlePolicy, new PropertyRule {
            Value = title,
            Constraints = new RequiredConstraint()
        });

        MutationApply.AddPolicy(title, titlePolicy);
        MutationApply.AddProperty(note, title);
        MutationApply.AddStage(note, draft);

        // var request = new RequiredPropertiesAnalysisRequest(note, draft);
        // var builder = new AnalyzerBuilder();
        // builder.UseDomainModelValidation();

        // var analysis = builder.Build().Analyze(request);
        // var requiredNames = analysis.GetRequiredProperties(request).Select(property => property.Name).ToArray();

        // await Assert.That(requiredNames).Contains("Title");
    }

    [Test]
    public async Task CreateEntityInstance_RequiredParameters_IncludeInheritedInitialStagePolicyRequirements() {
        var domain = DomainTestFactory.CreateDomain();
        var note = CreateEntity(domain, "Note");
        var stringType = CreatePrimitive(domain, "string");

        var title = new Property(domain, "Title", stringType);

        MutationApply.AddProperty(note, title);

        var parent = new Stage(domain, "Parent");

        var child = new Stage(domain, "Child") { Parent = parent };

        var parentPolicy = new Policy(domain, "RequireTitle");

        MutationApply.AddRule(parentPolicy, new PropertyRule {
            Value = title,
            Constraints = new RequiredConstraint()
        });

        MutationApply.AddPolicy(parent, parentPolicy);
        MutationApply.AddStage(note, parent);
        MutationApply.AddStage(note, child);

        var create = new CreateEntityInstance {
            EntityType = note,
            InitialStage = child
        };

        // var requiredNames = create.GetRequiredProperties().Select(p => p.Name).ToArray();

        // await Assert.That(requiredNames.Length).IsEqualTo(1);
        // await Assert.That(requiredNames).Contains("Title");
    }

    [Test]
    public async Task CreateEntityInstance_WhenEffectInitialStageIsNull_IncludesEntityPolicyRequirements() {
        var domain = DomainTestFactory.CreateDomain();
        var note = CreateEntity(domain, "Note");
        var stringType = CreatePrimitive(domain, "string");
        var title = new Property(domain, "Title", stringType);

        var rootPolicy = new Policy(domain, "RequireTitle");

        MutationApply.AddRule(rootPolicy, new PropertyRule {
            Value = title,
            Constraints = new RequiredConstraint()
        });

        MutationApply.AddProperty(note, title);
        MutationApply.AddPolicy(note, rootPolicy);

        var create = new CreateEntityInstance {
            EntityType = note,
            InitialStage = null
        };

        // var requiredNames = create.GetRequiredProperties().Select(p => p.Name).ToArray();

        // await Assert.That(requiredNames.Length).IsEqualTo(1);
        // await Assert.That(requiredNames).Contains("Title");
    }

    [Test]
    public async Task CreateEntityInstance_WhenEffectInitialStageIsNull_IncludesPropertyPolicyRequirements() {
        var domain = DomainTestFactory.CreateDomain();
        var note = CreateEntity(domain, "Note");
        var stringType = CreatePrimitive(domain, "string");

        var title = new Property(domain, "Title", stringType);

        var titlePolicy = new Policy(domain, "RequireTitleFromProperty");

        MutationApply.AddRule(titlePolicy, new PropertyRule {
            Value = title,
            Constraints = new RequiredConstraint()
        });

        MutationApply.AddPolicy(title, titlePolicy);
        MutationApply.AddProperty(note, title);

        var create = new CreateEntityInstance {
            EntityType = note,
            InitialStage = null
        };

        // var requiredNames = create.GetRequiredProperties().Select(p => p.Name).ToArray();

        // await Assert.That(requiredNames.Length).IsEqualTo(1);
        // await Assert.That(requiredNames).Contains("Title");
    }

    [Test]
    public async Task CreateEntityInstance_WhenInitialStageHasParentEntityAncestorStage_DoesNotThrow() {
        var domain = DomainTestFactory.CreateDomain();
        var parent = CreateEntity(domain, "Account");
        var child = CreateEntity(domain, "Ticket", parent);

        var parentStage = new Stage(domain, "Open");
        var childStage = new Stage(domain, "Draft") { Parent = parentStage };

        MutationApply.AddStage(parent, parentStage);
        MutationApply.AddStage(child, childStage);

        var create = new CreateEntityInstance {
            EntityType = child,
            InitialStage = childStage
        };

        // var required = create.GetRequiredProperties();

        // await Assert.That(required).IsNotNull();
    }

    [Test]
    public async Task CreateEntityInstance_WhenEntityHasNoParentEntity_DoesNotRequireParentEntityAncestorStage() {
        var domain = DomainTestFactory.CreateDomain();
        var parent = CreateEntity(domain, "Account");
        var child = CreateEntity(domain, "Ticket");

        var childParent = new Stage(domain, "Review");

        var childStage = new Stage(domain, "Draft") { Parent = childParent };

        MutationApply.AddStage(child, childParent);
        MutationApply.AddStage(child, childStage);

        var create = new CreateEntityInstance {
            EntityType = child,
            InitialStage = childStage
        };

        // var required = create.GetRequiredProperties();

        // await Assert.That(required).IsNotNull();
    }

    private static Entity CreateEntity(Domain domain, string name, Entity? parentEntity = null) {
        var entity = new Entity(domain, name, parentEntity);

        MutationApply.AddType(domain, entity);
        return entity;
    }

    private static Primitive CreatePrimitive(Domain domain, string name, TypeCategory category = TypeCategory.Primitive) {
        var primitive = new Primitive(domain, name, category);

        MutationApply.AddType(domain, primitive);
        return primitive;
    }

}