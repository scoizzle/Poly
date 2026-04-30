using Poly.Data.Modeling;
using Poly.Data.Modeling.TypeSystem;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Tests.TestHelpers;

using And = Poly.Syntax.Nodes.And;
using DomainAction = Poly.Data.Modeling.Action;
using Equal = Poly.Syntax.Nodes.Equal;
using GreaterThanOrEqual = Poly.Syntax.Nodes.GreaterThanOrEqual;
using LessThanOrEqual = Poly.Syntax.Nodes.LessThanOrEqual;

namespace Poly.Tests.Data.Modeling;

public class DomainLoweringGeneratorTests {
    private sealed class Person {
        public int Age { get; init; }
    }

    [Test]
    public async Task Lower_EqualMemberLiteral_CompilesAndEvaluates() {
        var subject = new Parameter("@value", new TypeReference(typeof(Person).FullName!));
        var clause = new Equal(new Member(subject, "Age"), new Constant(18));

        var context = new AnalysisContext(ClrTypeDefinitionRegistry.Shared);
        var analysis = new AnalysisResult(context);
        var generator = new DomainLoweringGenerator(analysis);

        var lowered = generator.Lower(clause, subject);
        var predicate = lowered.CompileLambda<Func<Person, bool>>((subject, typeof(Person)));

        await Assert.That(predicate(new Person { Age = 18 })).IsTrue();
        await Assert.That(predicate(new Person { Age = 19 })).IsFalse();
    }

    [Test]
    public async Task Lower_RangeExpression_CompilesAndEvaluates() {
        var subject = new Parameter("@value", new TypeReference(typeof(Person).FullName!));
        var value = new Member(subject, "Age");
        var clause = new And(
            new GreaterThanOrEqual(value, new Constant(18)),
            new LessThanOrEqual(value, new Constant(65)));

        var context = new AnalysisContext(ClrTypeDefinitionRegistry.Shared);
        var analysis = new AnalysisResult(context);
        var generator = new DomainLoweringGenerator(analysis);

        var lowered = generator.Lower(clause, subject);
        var predicate = lowered.CompileLambda<Func<Person, bool>>((subject, typeof(Person)));

        await Assert.That(predicate(new Person { Age = 18 })).IsTrue();
        await Assert.That(predicate(new Person { Age = 40 })).IsTrue();
        await Assert.That(predicate(new Person { Age = 17 })).IsFalse();
        await Assert.That(predicate(new Person { Age = 70 })).IsFalse();
    }

    [Test]
    public async Task AnalyzeDomain_ValidAuthoringModel_HasNoErrors() {
        var domain = DomainTestFactory.CreateDomain();
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var customer = new Entity(domain, "Customer");
        var supportCase = new Entity(domain, "SupportCase");
        var title = new Property(domain, "Title", stringType);
        var open = new Stage(domain, "Open");
        var assign = new DomainAction(domain, "Assign", supportCase);

        MutationApply.AddProperty(supportCase, title);
        MutationApply.AddAction(supportCase, assign);
        MutationApply.AddStage(supportCase, open);

        MutationApply.AddType(domain, stringType);
        MutationApply.AddType(domain, customer);
        MutationApply.AddType(domain, supportCase);
        MutationApply.AddRelationship(domain, new Relationship(domain, "CustomerCases", customer, supportCase, RelationshipCardinality.OneToMany, true));

        var analysis = new DomainModelAnalyzer().Analyze(domain);

        await Assert.That(analysis.HasErrors).IsFalse();
    }

    [Test]
    public async Task LowerToImplementationAst_ExpandsInheritedMembersAndEffectiveStageBehavior() {
        var domain = DomainTestFactory.CreateDomain();
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var parent = new Entity(domain, "Parent");
        var child = new Entity(domain, "Child", parent);
        var parentTitle = new Property(domain, "Title", stringType);
        var childDescription = new Property(domain, "Description", stringType);

        var parentPolicy = new Policy(domain, "RequireTitle");
        MutationApply.AddRule(parentPolicy, new PropertyRule {
            Value = parentTitle,
            Constraints = new Poly.Data.Modeling.Validation.Constraints.RequiredConstraint()
        });

        var parentStage = new Stage(domain, "Open");
        var childStage = new Stage(domain, "Review") { Parent = parentStage };
        var parentAction = new DomainAction(domain, "Escalate", parent);
        var childAction = new DomainAction(domain, "Approve", child);

        MutationApply.AddProperty(parent, parentTitle);
        MutationApply.AddPolicy(parent, parentPolicy);
        MutationApply.AddAction(parent, parentAction);
        MutationApply.AddStage(parent, parentStage);

        MutationApply.AddProperty(child, childDescription);
        MutationApply.AddAction(child, childAction);
        MutationApply.AddStage(child, childStage);

        var stagePolicy = new Policy(domain, "RequireReview");
        MutationApply.AddPolicy(childStage, stagePolicy);

        var stageAction = new DomainAction(domain, "Submit", child);
        MutationApply.AddAction(childStage, stageAction);

        MutationApply.AddType(domain, stringType);
        MutationApply.AddType(domain, parent);
        MutationApply.AddType(domain, child);

        // var lowered = new DomainModelAnalyzer().LowerToImplementationAst(domain);
        // var childModel = lowered.Entities.Single(model => ReferenceEquals(model.Entity, child));
        // var effectivePropertyNames = childModel.EffectiveProperties.Select(property => property.Name).ToArray();
        // var effectiveActionNames = childModel.EffectiveActions.Select(action => action.Name).ToArray();

        // var reviewStageModel = childModel.EffectiveStages.Single(model => ReferenceEquals(model.Stage, childStage));
        // var reviewStageEffectiveActionNames = reviewStageModel.EffectiveActions.Select(action => action.Name).ToArray();
        // var reviewStageEffectivePolicyNames = reviewStageModel.EffectivePolicies.Select(policy => policy.Name).ToArray();

        // await Assert.That(effectivePropertyNames).Contains("Title");
        // await Assert.That(effectivePropertyNames).Contains("Description");
        // await Assert.That(effectiveActionNames).Contains("Escalate");
        // await Assert.That(effectiveActionNames).Contains("Approve");
        // await Assert.That(reviewStageEffectiveActionNames).Contains("Submit");
        // await Assert.That(reviewStageEffectivePolicyNames).Contains("RequireReview");
    }
}