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

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var pass = new DomainImplementationLoweringPass();
        var typeDefs = pass.LowerToTypeDefinitions(domain, analysis);

        var csharp = new CSharpGenerator().Generate(typeDefs);

        await Assert.That(csharp).Contains("#nullable enable");
        await Assert.That(csharp).Contains("class TestEntity");
        await Assert.That(csharp).DoesNotContain("Console.WriteLine(");
        await Assert.That(csharp).Contains("String Name");
        await Assert.That(csharp).Contains("Int64 Age");
        await Assert.That(csharp).Contains("Boolean IsActive");
        await Assert.That(csharp).Contains("private TestEntity(String name, Int64 age, Boolean isActive)");
        await Assert.That(csharp).Contains("public interface IActor");
        await Assert.That(csharp).Contains("class ActionExecutionContext");
        await Assert.That(csharp).Contains("List<object> _events");
        await Assert.That(csharp).Contains("IActor? Actor");
        await Assert.That(csharp).Contains("TryCreate(ActionExecutionContext context, String name, Int64 age, Boolean isActive)");
        await Assert.That(csharp).Contains("protected set;");
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

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var pass = new DomainImplementationLoweringPass();
        var typeDefs = pass.LowerToTypeDefinitions(domain, analysis);

        var csharp = new CSharpGenerator().Generate(typeDefs);

        await Assert.That(csharp).Contains("enum TestEntityStage");
        await Assert.That(csharp).Contains("CurrentStage { get; protected set; }");
        await Assert.That(csharp).Contains("Draft");
        await Assert.That(csharp).Contains("Published");
        await Assert.That(csharp).Contains("Archived");
    }

    [Test]
    public async Task InheritedActionWithStageTransition_LoweredToCSharp_UsesChildEntityStageEnum() {
        var domain = new Domain("TestDomain");
        var textType = new Primitive(domain, "Text", TypeCategory.Text);
        MutationApply.AddType(domain, textType);

        var user = new Entity(domain, "User");
        MutationApply.AddType(domain, user);
        var active = new Stage(domain, "Active");
        var inactive = new Stage(domain, "Inactive");
        MutationApply.AddStage(user, active);
        MutationApply.AddStage(user, inactive);

        var deactivate = new DomainAction(domain, "Deactivate", user);
        MutationApply.AddAction(user, deactivate);
        MutationApply.AddEffect(deactivate, new StageTransition(domain) { TargetStage = inactive });

        var employee = new Entity(domain, "Employee", parentEntity: user);
        MutationApply.AddType(domain, employee);

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var pass = new DomainImplementationLoweringPass();
        var typeDefs = pass.LowerToTypeDefinitions(domain, analysis);

        var csharp = new CSharpGenerator().Generate(typeDefs);

        await Assert.That(csharp).Contains("class Employee");
        await Assert.That(csharp).Contains("Result Deactivate(ActionExecutionContext context)");
        await Assert.That(csharp).Contains("EmployeeStage.");
        await Assert.That(csharp).Contains("enum EmployeeStage");
        await Assert.That(csharp).Contains("enum UserStage");
        await Assert.That(csharp).Contains("DeactivateRequiresActiveStage");
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

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var pass = new DomainImplementationLoweringPass();
        var typeDefs = pass.LowerToTypeDefinitions(domain, analysis);

        var csharp = new CSharpGenerator().Generate(typeDefs);

        await Assert.That(csharp).Contains("class TestEntity");
        await Assert.That(csharp).Contains("Result SetName(ActionExecutionContext context, String newName)");
        await Assert.That(csharp).Contains("String newName");
        await Assert.That(csharp).Contains("this.Name = newName");
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

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var pass = new DomainImplementationLoweringPass();
        var typeDefs = pass.LowerToTypeDefinitions(domain, analysis);

        var csharp = new CSharpGenerator().Generate(typeDefs);

        await Assert.That(csharp).Contains("if (");
        await Assert.That(csharp).Contains("return");
        await Assert.That(csharp).Contains("Success");
        await Assert.That(csharp).Contains("Failure");
        await Assert.That(csharp).Contains("IReadOnlyCollection<String> ErrorCodes");
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

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var pass = new DomainImplementationLoweringPass();
        var typeDefs = pass.LowerToTypeDefinitions(domain, analysis);

        var csharp = new CSharpGenerator().Generate(typeDefs);

        await Assert.That(csharp).Contains("record ItemCreated(String itemName);");
    }

    [Test]
    public async Task EntityWithAcronymProperty_LoweredToCSharp_CamelCasesSynthesizedParameters() {
        var domain = new Domain("TestDomain");
        var textType = new Primitive(domain, "Text", TypeCategory.Text);
        MutationApply.AddType(domain, textType);

        var entity = new Entity(domain, "Book");
        MutationApply.AddProperty(entity, new Property(domain, "ISBN", textType));
        MutationApply.AddType(domain, entity);

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var pass = new DomainImplementationLoweringPass();
        var typeDefs = pass.LowerToTypeDefinitions(domain, analysis);

        var csharp = new CSharpGenerator().Generate(typeDefs);

        await Assert.That(csharp).Contains("private Book(String isbn)");
        await Assert.That(csharp).Contains("TryCreate(ActionExecutionContext context, String isbn)");
        await Assert.That(csharp).Contains("this.ISBN = isbn;");
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

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var pass = new DomainImplementationLoweringPass();
        var typeDefs = pass.LowerToTypeDefinitions(domain, analysis);

        var csharp = new CSharpGenerator().Generate(typeDefs);

        await Assert.That(csharp).Contains("class SourceTargets");
        await Assert.That(csharp).Contains("private SourceTargets(SourceEntity source, TargetEntity target)");
        await Assert.That(csharp).Contains("TryCreate(ActionExecutionContext context, SourceEntity source, TargetEntity target)");
        await Assert.That(csharp).Contains("SourceEntity Source");
        await Assert.That(csharp).Contains("TargetEntity Target");
        await Assert.That(csharp).DoesNotContain("SetTargetEntity(");
        await Assert.That(csharp).DoesNotContain("AddTargetEntity(");
    }

    [Test]
    public async Task FullPipeline_HumanReadableOutput_HasIndentationAndBraces() {
        var domain = new Domain("TestDomain");
        var textType = new Primitive(domain, "Text", TypeCategory.Text);
        MutationApply.AddType(domain, textType);

        var entity = new Entity(domain, "TestEntity");
        var nameProperty = new Property(domain, "Name", textType);
        var setName = new DomainAction(domain, "SetName", entity);
        var newName = new Property(domain, "newName", textType);
        MutationApply.AddProperty(entity, nameProperty);
        MutationApply.AddAction(entity, setName);
        MutationApply.AddParameter(setName, newName);
        MutationApply.AddEffect(setName, new Assign(domain) { Target = nameProperty, Value = newName });
        MutationApply.AddType(domain, entity);

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var pass = new DomainImplementationLoweringPass();
        var typeDefs = pass.LowerToTypeDefinitions(domain, analysis);
        var testStatements = pass.GenerateTestStatements(domain, analysis);

        var csharp = new CSharpGenerator().Generate(typeDefs, testStatements);

        await Assert.That(csharp).Contains("    ");
        await Assert.That(csharp).Contains("{");
        await Assert.That(csharp).Contains("}");
        await Assert.That(csharp).Contains("Console.WriteLine(\"Testing TestEntity...\");");
        await Assert.That(csharp).Contains("var _context = new ActionExecutionContext");
        await Assert.That(csharp).Contains("public class TestEntity");
    }

    [Test]
    public async Task EntityWithActorPolicy_LoweredToCSharp_EvaluatesAgainstRuntimeContext() {
        var domain = new Domain("TestDomain");
        var textType = new Primitive(domain, "Text", TypeCategory.Text);
        MutationApply.AddType(domain, textType);

        var actor = new Actor(domain, "EmployeeActor");
        var department = new Property(domain, "Department", textType);
        MutationApply.AddProperty(actor, department);
        MutationApply.AddType(domain, actor);

        var entity = new Entity(domain, "Document");
        MutationApply.AddType(domain, entity);

        var policy = new Policy(domain, "DepartmentPolicy");
        MutationApply.AddRule(policy, new ActorPropertyRule(domain, "DepartmentMatches", department, new EqualityConstraint("Engineering")));
        MutationApply.AddPolicy(entity, policy);

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var pass = new DomainImplementationLoweringPass();
        var typeDefs = pass.LowerToTypeDefinitions(domain, analysis);

        var csharp = new CSharpGenerator().Generate(typeDefs);

        await Assert.That(csharp).Contains("context.Actor is EmployeeActor");
        await Assert.That(csharp).Contains("((EmployeeActor)context.Actor!).Department == \"Engineering\"");
    }

    [Test]
    public async Task EntityWithRolePolicy_LoweredToCSharp_UsesContextRoleEvaluation() {
        var domain = new Domain("TestDomain");
        var entity = new Entity(domain, "Document");
        MutationApply.AddType(domain, entity);

        var policy = new Policy(domain, "EditorOnly");
        MutationApply.AddRule(policy, new ActorRoleRule(domain, "MustBeEditor", "Editor"));
        MutationApply.AddPolicy(entity, policy);

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var pass = new DomainImplementationLoweringPass();
        var typeDefs = pass.LowerToTypeDefinitions(domain, analysis);

        var csharp = new CSharpGenerator().Generate(typeDefs);

        await Assert.That(csharp).Contains("context.Actor!.IsInRole(\"Editor\")");
        await Assert.That(csharp).Contains("public interface IActor");
        await Assert.That(csharp).Contains("Boolean IsInRole(String role);");
    }

    [Test]
    public async Task EntityWithPublishEventEffect_LoweredToCSharp_PublishesThroughRuntimeContext() {
        var domain = new Domain("TestDomain");
        var textType = new Primitive(domain, "Text", TypeCategory.Text);
        MutationApply.AddType(domain, textType);

        var entity = new Entity(domain, "Order");
        var number = new Property(domain, "Number", textType);
        MutationApply.AddProperty(entity, number);
        MutationApply.AddType(domain, entity);

        var shipped = new Event(domain, "OrderShipped");
        MutationApply.AddProperty(shipped, new Property(domain, "Number", textType));
        MutationApply.AddEvent(entity, shipped);

        var ship = new DomainAction(domain, "Ship", entity);
        MutationApply.AddAction(entity, ship);
        var publish = new PublishEvent(domain) { Event = shipped };
        _ = domain.CreateMutation()
            .AddEffect(ship, publish)
            .SetEventPropertyBinding(ship, publish, "Number", new EventPropertyBindingSource.EntityProperty("Number"))
            .Apply();

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var pass = new DomainImplementationLoweringPass();
        var typeDefs = pass.LowerToTypeDefinitions(domain, analysis);

        var csharp = new CSharpGenerator().Generate(typeDefs);

        await Assert.That(csharp).Contains("context.Events(new OrderShipped(this.Number!))");
    }

    // ── Contract interface tests ─────────────────────────────────────────────

    [Test]
    public async Task LowerToContractInterfaces_EntityWithoutStages_GeneratesBaseInterfaceOnly() {
        var domain = new Domain("TestDomain");
        var textType = new Primitive(domain, "Text", TypeCategory.Text);
        MutationApply.AddType(domain, textType);

        var entity = new Entity(domain, "User");
        MutationApply.AddProperty(entity, new Property(domain, "Name", textType));
        MutationApply.AddType(domain, entity);

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var pass = new DomainImplementationLoweringPass();
        var contracts = pass.LowerToContractInterfaces(domain, analysis);
        var csharp = new CSharpGenerator().Generate(contracts);

        await Assert.That(csharp).Contains("public interface IUser");
        await Assert.That(csharp).Contains("String Name { get; }");
        await Assert.That(csharp).DoesNotContain("IUserActive");
        await Assert.That(csharp).DoesNotContain("IUserInactive");
    }

    [Test]
    public async Task LowerToContractInterfaces_EntityWithStages_GeneratesStageInterfaces() {
        var domain = new Domain("TestDomain");
        var textType = new Primitive(domain, "Text", TypeCategory.Text);
        MutationApply.AddType(domain, textType);

        var user = new Entity(domain, "User");
        MutationApply.AddType(domain, user);
        MutationApply.AddStage(user, new Stage(domain, "Active"));
        MutationApply.AddStage(user, new Stage(domain, "Inactive"));

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var pass = new DomainImplementationLoweringPass();
        var contracts = pass.LowerToContractInterfaces(domain, analysis);
        var csharp = new CSharpGenerator().Generate(contracts);

        await Assert.That(csharp).Contains("public interface IUser");
        await Assert.That(csharp).Contains("public interface IActiveUser : IUser");
        await Assert.That(csharp).Contains("public interface IInactiveUser : IUser");
    }

    [Test]
    public async Task LowerToContractInterfaces_StageActions_AppearOnCorrectStageInterface() {
        var domain = new Domain("TestDomain");
        var textType = new Primitive(domain, "Text", TypeCategory.Text);
        MutationApply.AddType(domain, textType);

        var user = new Entity(domain, "User");
        MutationApply.AddType(domain, user);
        var active = new Stage(domain, "Active");
        var inactive = new Stage(domain, "Inactive");
        MutationApply.AddStage(user, active);
        MutationApply.AddStage(user, inactive);

        var deactivate = new DomainAction(domain, "Deactivate", user);
        MutationApply.AddAction(active, deactivate);
        var reason = new Property(domain, "reason", textType);
        MutationApply.AddParameter(deactivate, reason);

        var activate = new DomainAction(domain, "Activate", user);
        MutationApply.AddAction(inactive, activate);

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var pass = new DomainImplementationLoweringPass();
        var contracts = pass.LowerToContractInterfaces(domain, analysis);
        var csharp = new CSharpGenerator().Generate(contracts);

        await Assert.That(csharp).Contains("interface IActiveUser");
        await Assert.That(csharp).Contains("Result Deactivate(ActionExecutionContext context, String reason)");
        await Assert.That(csharp).Contains("interface IInactiveUser");
        await Assert.That(csharp).Contains("Result Activate(ActionExecutionContext context)");
    }

    [Test]
    public async Task LowerToContractInterfaces_ChildEntity_InterfaceInheritsParent() {
        var domain = new Domain("TestDomain");
        var textType = new Primitive(domain, "Text", TypeCategory.Text);
        MutationApply.AddType(domain, textType);

        var user = new Entity(domain, "User");
        MutationApply.AddType(domain, user);
        MutationApply.AddStage(user, new Stage(domain, "Active"));
        MutationApply.AddStage(user, new Stage(domain, "Inactive"));

        var employee = new Entity(domain, "Employee", parentEntity: user);
        MutationApply.AddProperty(employee, new Property(domain, "EmployeeId", textType));
        MutationApply.AddType(domain, employee);

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var pass = new DomainImplementationLoweringPass();
        var contracts = pass.LowerToContractInterfaces(domain, analysis);
        var csharp = new CSharpGenerator().Generate(contracts);

        await Assert.That(csharp).Contains("interface IEmployee : IUser");
        await Assert.That(csharp).Contains("String EmployeeId");
        await Assert.That(csharp).Contains("interface IActiveEmployee");
        await Assert.That(csharp).Contains("interface IInactiveEmployee");
    }

    [Test]
    public async Task LowerToContractInterfaces_ChildStage_InheritsParentStageInterface() {
        var domain = new Domain("TestDomain");
        var textType = new Primitive(domain, "Text", TypeCategory.Text);
        MutationApply.AddType(domain, textType);

        var user = new Entity(domain, "User");
        MutationApply.AddType(domain, user);
        var active = new Stage(domain, "Active");
        MutationApply.AddStage(user, active);

        var deactivate = new DomainAction(domain, "Deactivate", user);
        MutationApply.AddAction(active, deactivate);

        var employee = new Entity(domain, "Employee", parentEntity: user);
        MutationApply.AddType(domain, employee);
        var activeChecking = new Stage(domain, "ActiveChecking") { Parent = active };
        MutationApply.AddStage(employee, activeChecking);

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var pass = new DomainImplementationLoweringPass();
        var contracts = pass.LowerToContractInterfaces(domain, analysis);
        var csharp = new CSharpGenerator().Generate(contracts);

        await Assert.That(csharp).Contains("interface IActiveUser");
        await Assert.That(csharp).Contains("interface IActiveCheckingEmployee : IEmployee, IActiveUser");
        // Deactivate is declared on the parent stage interface and inherited
        await Assert.That(csharp).Contains("Result Deactivate(ActionExecutionContext context)");
    }

    [Test]
    public async Task ActionWithoutStageTransition_LoweredToCSharp_HasGuardFromStageAssignment() {
        var domain = new Domain("TestDomain");
        var textType = new Primitive(domain, "Text", TypeCategory.Text);
        MutationApply.AddType(domain, textType);

        var entity = new Entity(domain, "Order");
        MutationApply.AddType(domain, entity);
        var pending = new Stage(domain, "Pending");
        var shipped = new Stage(domain, "Shipped");
        var delivered = new Stage(domain, "Delivered");
        MutationApply.AddStage(entity, pending);
        MutationApply.AddStage(entity, shipped);
        MutationApply.AddStage(entity, delivered);

        // Actions WITHOUT StageTransition effects — guards should come from stage assignment
        var cancel = new DomainAction(domain, "Cancel", entity);
        MutationApply.AddAction(entity, cancel);
        MutationApply.AddAction(pending, cancel);

        var ship = new DomainAction(domain, "Ship", entity);
        MutationApply.AddAction(entity, ship);
        MutationApply.AddAction(pending, ship);

        // Action WITH StageTransition effect — guards come from transition
        var complete = new DomainAction(domain, "Complete", entity);
        MutationApply.AddAction(entity, complete);
        MutationApply.AddAction(shipped, complete);
        MutationApply.AddEffect(complete, new StageTransition(domain) { TargetStage = delivered });

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var pass = new DomainImplementationLoweringPass();
        var typeDefs = pass.LowerToTypeDefinitions(domain, analysis);
        var csharp = new CSharpGenerator().Generate(typeDefs);

        // Cancel has guard from stage assignment (assigned to Pending)
        await Assert.That(csharp).Contains("CancelRequiresPendingStage");
        await Assert.That(csharp).Contains("Cancel(ActionExecutionContext context)");

        // Ship has guard from stage assignment (assigned to Pending)
        await Assert.That(csharp).Contains("ShipRequiresPendingStage");

        // Complete has guard from StageTransition (Shipped -> Delivered)
        await Assert.That(csharp).Contains("CompleteRequiresShippedStage");
    }
}