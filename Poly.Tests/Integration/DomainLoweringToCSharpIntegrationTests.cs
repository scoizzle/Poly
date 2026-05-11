using Poly.Data.Modeling;
using Poly.Data.Modeling.Analysis;
using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.Effects.Mutations;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation.Constraints;
using Poly.Interpretation.CSharp;
using Poly.Introspection;

using DomainAction = Poly.Data.Modeling.Action;

namespace Poly.Tests.Integration;

public class DomainLoweringToCSharpIntegrationTests {
    [Test]
    public async Task EntityWithPrimitives_LoweredToCSharp_ProducesClassWithTypedProperties() {
        var domain = new Domain("TestDomain");
        var textType = new Primitive(domain, "Text", TypeCategory.Text);
        var numberType = new Primitive(domain, "Number", TypeCategory.Numeric);
        var boolType = new Primitive(domain, "Boolean", TypeCategory.Primitive);

        MutationApply.AddType(domain, textType);
        MutationApply.AddType(domain, numberType);
        MutationApply.AddType(domain, boolType);

        var entity = new Entity(domain, "TestEntity");
        MutationApply.AddProperty(entity, new Property(domain, "Name", textType));
        MutationApply.AddProperty(entity, new Property(domain, "Age", numberType));
        MutationApply.AddProperty(entity, new Property(domain, "IsActive", boolType));
        MutationApply.AddType(domain, entity);

        var analysis = new DomainModelAnalyzer().Analyze(domain);
        var pass = new DomainImplementationLoweringPass();
        var typeDefs = pass.LowerToTypeDefinitions(domain, analysis);

        var csharp = new CSharpGenerator().Generate(typeDefs);

        await Assert.That(csharp).Contains("class TestEntity");
        await Assert.That(csharp).Contains("String Name");
        await Assert.That(csharp).Contains("Int64 Age");
        await Assert.That(csharp).Contains("Boolean IsActive");
        await Assert.That(csharp).Contains("{ get; set; }");
    }

    [Test]
    public async Task EntityWithStageEnum_LoweredToCSharp_ProducesEnum() {
        var domain = new Domain("TestDomain");
        var textType = new Primitive(domain, "Text", TypeCategory.Text);
        MutationApply.AddType(domain, textType);

        var entity = new Entity(domain, "TestEntity");
        MutationApply.AddProperty(entity, new Property(domain, "Name", textType));
        MutationApply.AddType(domain, entity);
        MutationApply.AddStage(entity, new Stage(domain, "Draft"));
        MutationApply.AddStage(entity, new Stage(domain, "Published"));
        MutationApply.AddStage(entity, new Stage(domain, "Archived"));

        var analysis = new DomainModelAnalyzer().Analyze(domain);
        var pass = new DomainImplementationLoweringPass();
        var typeDefs = pass.LowerToTypeDefinitions(domain, analysis);

        var csharp = new CSharpGenerator().Generate(typeDefs);

        await Assert.That(csharp).Contains("enum TestEntityStage");
        await Assert.That(csharp).Contains("Draft");
        await Assert.That(csharp).Contains("Published");
        await Assert.That(csharp).Contains("Archived");
    }

    [Test]
    public async Task EntityWithAction_LoweredToCSharp_ProducesMethodWithBody() {
        var domain = new Domain("TestDomain");
        var textType = new Primitive(domain, "Text", TypeCategory.Text);
        MutationApply.AddType(domain, textType);

        var entity = new Entity(domain, "TestEntity");
        var nameProp = new Property(domain, "Name", textType);
        MutationApply.AddProperty(entity, nameProp);
        MutationApply.AddType(domain, entity);

        var setAction = new DomainAction(domain, "SetName", entity);
        var param = new Property(domain, "newName", textType);
        MutationApply.AddAction(entity, setAction);
        MutationApply.AddParameter(setAction, param);

        var assignEffect = new Assign(domain) { Target = nameProp, Value = param };
        MutationApply.AddEffect(setAction, assignEffect);

        var analysis = new DomainModelAnalyzer().Analyze(domain);
        var pass = new DomainImplementationLoweringPass();
        var typeDefs = pass.LowerToTypeDefinitions(domain, analysis);

        var csharp = new CSharpGenerator().Generate(typeDefs);

        await Assert.That(csharp).Contains("class TestEntity");
        await Assert.That(csharp).Contains("Result SetName(");
        await Assert.That(csharp).Contains("String newName");
        await Assert.That(csharp).Contains("this.Name = this.newName");
    }

    [Test]
    public async Task EntityWithConstraints_LoweredToCSharp_ContainsGuardConditions() {
        var domain = new Domain("TestDomain");
        var textType = new Primitive(domain, "Text", TypeCategory.Text);
        MutationApply.AddType(domain, textType);

        var entity = new Entity(domain, "TestEntity");
        var nameProp = new Property(domain, "Name", textType);
        MutationApply.AddProperty(entity, nameProp);
        MutationApply.AddConstraint(nameProp, new RequiredConstraint());
        MutationApply.AddConstraint(nameProp, new LengthConstraint(1, 100));
        MutationApply.AddType(domain, entity);

        var setAction = new DomainAction(domain, "SetName", entity);
        var param = new Property(domain, "newName", textType);
        MutationApply.AddAction(entity, setAction);
        MutationApply.AddParameter(setAction, param);

        var assignEffect = new Assign(domain) { Target = nameProp, Value = param };
        MutationApply.AddEffect(setAction, assignEffect);

        var analysis = new DomainModelAnalyzer().Analyze(domain);
        var pass = new DomainImplementationLoweringPass();
        var typeDefs = pass.LowerToTypeDefinitions(domain, analysis);

        var csharp = new CSharpGenerator().Generate(typeDefs);

        await Assert.That(csharp).Contains("if (");
        await Assert.That(csharp).Contains("return");
        await Assert.That(csharp).Contains("Success");
        await Assert.That(csharp).Contains("Failure");
    }

    [Test]
    public async Task EventType_LoweredToCSharp_ProducesEventRecord() {
        var domain = new Domain("TestDomain");
        var textType = new Primitive(domain, "Text", TypeCategory.Text);
        MutationApply.AddType(domain, textType);

        var entity = new Entity(domain, "TestEntity");
        MutationApply.AddType(domain, entity);

        var ev = new Event(domain, "ItemCreated");
        MutationApply.AddProperty(ev, new Property(domain, "ItemName", textType));
        MutationApply.AddEvent(entity, ev);

        var analysis = new DomainModelAnalyzer().Analyze(domain);
        var pass = new DomainImplementationLoweringPass();
        var typeDefs = pass.LowerToTypeDefinitions(domain, analysis);

        var csharp = new CSharpGenerator().Generate(typeDefs);

        await Assert.That(csharp).Contains("record ItemCreated(String ItemName);");
    }

    [Test]
    public async Task Relationship_LoweredToCSharp_ProducesSourceTargetProperties() {
        var domain = new Domain("TestDomain");
        var textType = new Primitive(domain, "Text", TypeCategory.Text);
        MutationApply.AddType(domain, textType);

        var source = new Entity(domain, "SourceEntity");
        var target = new Entity(domain, "TargetEntity");
        MutationApply.AddType(domain, source);
        MutationApply.AddType(domain, target);

        var rel = new Relationship(domain, "SourceTargets", source, target, RelationshipCardinality.OneToMany, true);
        MutationApply.AddRelationship(domain, rel);

        var analysis = new DomainModelAnalyzer().Analyze(domain);
        var pass = new DomainImplementationLoweringPass();
        var typeDefs = pass.LowerToTypeDefinitions(domain, analysis);

        var csharp = new CSharpGenerator().Generate(typeDefs);

        await Assert.That(csharp).Contains("class SourceTargets");
        await Assert.That(csharp).Contains("SourceEntity Source");
        await Assert.That(csharp).Contains("TargetEntity Target");
    }

    [Test]
    public async Task FullPipeline_HumanReadableOutput_HasIndentationAndBraces() {
        var domain = new Domain("TestDomain");
        var textType = new Primitive(domain, "Text", TypeCategory.Text);
        MutationApply.AddType(domain, textType);

        var entity = new Entity(domain, "TestEntity");
        MutationApply.AddProperty(entity, new Property(domain, "Name", textType));
        MutationApply.AddType(domain, entity);

        var analysis = new DomainModelAnalyzer().Analyze(domain);
        var pass = new DomainImplementationLoweringPass();
        var typeDefs = pass.LowerToTypeDefinitions(domain, analysis);

        var csharp = new CSharpGenerator().Generate(typeDefs);

        await Assert.That(csharp).Contains("    ");
        await Assert.That(csharp).Contains("{");
        await Assert.That(csharp).Contains("}");
        await Assert.That(csharp).Contains("Console.WriteLine(\"OK\");");
        await Assert.That(csharp).Contains("public class TestEntity");
    }
}