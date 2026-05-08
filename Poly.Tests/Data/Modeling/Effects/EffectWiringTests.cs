using Poly.Data.Modeling;
using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;
using Poly.Introspection;

using DomainAction = Poly.Data.Modeling.Action;

namespace Poly.Tests.Data.Modeling.Effects;

public class EffectWiringTests {
    private static Domain CreateDomain(string? name = null) => new(name ?? "Test Domain");
    private static Entity CreateEntity(Domain domain, string name) => new(domain, name);
    private static DomainAction CreateAction(Domain domain, string name, Entity entity) => new(domain, name, entity);
    private static Property CreateParameter(Domain domain, string name, DomainType type) => new(domain, name, type);
    private static Primitive CreatePrimitive(Domain domain, string name) => new(domain, name, TypeCategory.Text);

    private sealed record TestEffect(Domain Domain) : Effect(Domain);

    [Test]
    public async Task EffectResult_Produces_AddsOutput() {
        var result = new EffectResult();
        var domain = CreateDomain();
        var primitive = CreatePrimitive(domain, "Text");

        result.Produces("output1", primitive);

        await Assert.That(result.Outputs.ContainsKey("output1")).IsTrue();
        await Assert.That(result.Outputs["output1"]).IsEqualTo(primitive);
    }

    [Test]
    public async Task EffectResult_HasOutput_ReturnsCorrectValue() {
        var result = new EffectResult();
        var domain = CreateDomain();
        var primitive = CreatePrimitive(domain, "Text");

        await Assert.That(result.HasOutput("output1")).IsFalse();

        result.Produces("output1", primitive);

        await Assert.That(result.HasOutput("output1")).IsTrue();
        await Assert.That(result.HasOutput("nonexistent")).IsFalse();
    }

    [Test]
    public async Task EffectResult_ProducesMultipleOutputs_StoresAll() {
        var result = new EffectResult();
        var domain = CreateDomain();
        var text = CreatePrimitive(domain, "Text");
        var entity = CreateEntity(domain, "Person");

        result.Produces("name", text);
        result.Produces("person", entity);

        await Assert.That(result.Outputs.Count).IsEqualTo(2);
        await Assert.That(result.HasOutput("name")).IsTrue();
        await Assert.That(result.HasOutput("person")).IsTrue();
    }

    [Test]
    public async Task EffectValueRef_Record_CanBeCreated() {
        var valueRef = new EffectValueRef("SourceEffect", "OutputName");

        await Assert.That(valueRef).IsNotNull();
        await Assert.That(valueRef).IsAssignableTo<EffectValueRef>();
    }

    [Test]
    public async Task EffectValueRef_InheritsFromDomainValue() {
        var valueRef = new EffectValueRef("SourceEffect", "OutputName");

        await Assert.That(valueRef).IsAssignableTo<DomainValue>();
        await Assert.That(valueRef.Name).IsEqualTo("SourceEffect.OutputName");
    }

    [Test]
    public async Task Effect_ResultProperty_ReturnsEffectResult() {
        var domain = CreateDomain();
        var effect = new TestEffect(domain);

        await Assert.That(effect.Result).IsNotNull();
        await Assert.That(effect.Result).IsAssignableTo<EffectResult>();
    }

    [Test]
    public async Task Effect_ProducesConvenienceMethod_AddsToResult() {
        var domain = CreateDomain();
        var effect = new TestEffect(domain);
        var primitive = CreatePrimitive(domain, "Text");

        effect.Produces("output1", primitive);

        await Assert.That(effect.Result.HasOutput("output1")).IsTrue();
        await Assert.That(effect.Result.Outputs["output1"]).IsEqualTo(primitive);
    }

    [Test]
    public async Task Effect_BindOutputTo_WiresOutputToTargetEffect() {
        var domain = CreateDomain();
        var sourceEffect = new TestEffect(domain);
        var targetEffect = new TestEffect(domain);
        var primitive = CreatePrimitive(domain, "Text");

        sourceEffect.Produces("output1", primitive);

        sourceEffect.BindOutputTo("output1", targetEffect, "param1");

        await Assert.That(targetEffect.IncomingBindings).IsNotEmpty();
        await Assert.That(targetEffect.IncomingBindings["param1"].SourceEffectName).IsEqualTo(sourceEffect.GetType().Name);
        await Assert.That(targetEffect.IncomingBindings["param1"].OutputName).IsEqualTo("output1");
    }

    [Test]
    public async Task Effect_BindOutputTo_ThrowsWhenOutputNotProduced() {
        var domain = CreateDomain();
        var sourceEffect = new TestEffect(domain);
        var targetEffect = new TestEffect(domain);

        await Assert.That(() => sourceEffect.BindOutputTo("nonexistent", targetEffect, "param1"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CreateEntityInstance_InitializeResult_ProducesEntityOutput() {
        var domain = CreateDomain();
        var entity = CreateEntity(domain, "Person");
        var effect = new CreateEntityInstance(domain) { EntityType = entity };

        effect.InitializeResult();

        await Assert.That(effect.Result.HasOutput("entity")).IsTrue();
        await Assert.That(effect.Result.Outputs["entity"]).IsEqualTo(entity);
    }

    [Test]
    public async Task CreateEntityInstance_InitializeResult_DoesNotProduceInitialStageByDefault() {
        var domain = CreateDomain();
        var entity = CreateEntity(domain, "Person");
        var effect = new CreateEntityInstance(domain) { EntityType = entity };

        effect.InitializeResult();

        await Assert.That(effect.Result.HasOutput("initialStage")).IsFalse();
    }

    [Test]
    public async Task InvokeAction_BindParameter_WithDomainValue_WorksCorrectly() {
        var domain = CreateDomain();
        var entity = CreateEntity(domain, "Person");
        var action = CreateAction(domain, "DoSomething", entity);
        var paramType = CreatePrimitive(domain, "Text");
        var parameter = CreateParameter(domain, "input1", paramType);
        var domainValue = new TestDomainValue(domain, "testValue", paramType);

        // Add parameter to action
        action._parameters.Add(parameter);

        var effect = new InvokeAction(domain) { TargetAction = action };
        effect.BindParameter(parameter, domainValue);

        await Assert.That(effect.ParameterBindings.ContainsKey("input1")).IsTrue();
        await Assert.That(effect.ParameterBindings["input1"]).IsEqualTo(domainValue);
    }

    [Test]
    public async Task InvokeAction_BindParameter_ThrowsWhenTypeMismatch() {
        var domain = CreateDomain();
        var entity = CreateEntity(domain, "Person");
        var action = CreateAction(domain, "DoSomething", entity);
        var paramType = CreatePrimitive(domain, "Text");
        var differentType = CreatePrimitive(domain, "Number");
        var parameter = CreateParameter(domain, "input1", paramType);
        var domainValue = new TestDomainValue(domain, "testValue", differentType);

        // Add parameter to action
        action._parameters.Add(parameter);

        var effect = new InvokeAction(domain) { TargetAction = action };

        await Assert.That(() => effect.BindParameter(parameter, domainValue))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task InvokeAction_BindParameter_WhenParameterIsBaseEntity_AllowsDerivedValue() {
        var domain = CreateDomain();
        var owner = CreateEntity(domain, "Owner");
        var baseEntity = CreateEntity(domain, "BaseEntity");
        var derivedEntity = new Entity(domain, "DerivedEntity", baseEntity);
        var action = CreateAction(domain, "DoSomething", owner);
        var parameter = CreateParameter(domain, "input1", baseEntity);
        var domainValue = new TestDomainValue(domain, "testValue", derivedEntity);

        action._parameters.Add(parameter);

        var effect = new InvokeAction(domain) { TargetAction = action };
        effect.BindParameter(parameter, domainValue);

        await Assert.That(effect.ParameterBindings.ContainsKey("input1")).IsTrue();
        await Assert.That(effect.ParameterBindings["input1"]).IsEqualTo(domainValue);
    }

    [Test]
    public async Task InvokeAction_BindParameter_WhenParameterIsDerivedEntity_ThrowsForBaseValue() {
        var domain = CreateDomain();
        var owner = CreateEntity(domain, "Owner");
        var baseEntity = CreateEntity(domain, "BaseEntity");
        var derivedEntity = new Entity(domain, "DerivedEntity", baseEntity);
        var action = CreateAction(domain, "DoSomething", owner);
        var parameter = CreateParameter(domain, "input1", derivedEntity);
        var domainValue = new TestDomainValue(domain, "testValue", baseEntity);

        action._parameters.Add(parameter);

        var effect = new InvokeAction(domain) { TargetAction = action };

        await Assert.That(() => effect.BindParameter(parameter, domainValue))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task InvokeAction_BindParameterFrom_WithSourceEffect_WorksCorrectly() {
        var domain = CreateDomain();
        var entity = CreateEntity(domain, "Person");
        var action = CreateAction(domain, "DoSomething", entity);
        var paramType = CreatePrimitive(domain, "Text");
        var parameter = CreateParameter(domain, "input1", paramType);

        // Add parameter to action
        action._parameters.Add(parameter);

        var sourceEffect = new TestEffect(domain);
        sourceEffect.Produces("output1", paramType);

        var effect = new InvokeAction(domain) { TargetAction = action };
        effect.BindParameterFrom("input1", sourceEffect, "output1");

        await Assert.That(effect.ParameterBindings.ContainsKey("input1")).IsTrue();
        await Assert.That(effect.ParameterBindings["input1"]).IsAssignableTo<EffectValueRef>();
    }

    [Test]
    public async Task InvokeAction_BindParameterFrom_WhenParameterIsBaseEntity_AllowsDerivedOutput() {
        var domain = CreateDomain();
        var owner = CreateEntity(domain, "Owner");
        var baseEntity = CreateEntity(domain, "BaseEntity");
        var derivedEntity = new Entity(domain, "DerivedEntity", baseEntity);
        var action = CreateAction(domain, "DoSomething", owner);
        var parameter = CreateParameter(domain, "input1", baseEntity);

        action._parameters.Add(parameter);

        var sourceEffect = new TestEffect(domain);
        sourceEffect.Produces("output1", derivedEntity);

        var effect = new InvokeAction(domain) { TargetAction = action };
        effect.BindParameterFrom("input1", sourceEffect, "output1");

        await Assert.That(effect.ParameterBindings.ContainsKey("input1")).IsTrue();
        await Assert.That(effect.ParameterBindings["input1"]).IsAssignableTo<EffectValueRef>();
    }

    [Test]
    public async Task InvokeAction_BindParameterFrom_WhenParameterIsDerivedEntity_ThrowsForBaseOutput() {
        var domain = CreateDomain();
        var owner = CreateEntity(domain, "Owner");
        var baseEntity = CreateEntity(domain, "BaseEntity");
        var derivedEntity = new Entity(domain, "DerivedEntity", baseEntity);
        var action = CreateAction(domain, "DoSomething", owner);
        var parameter = CreateParameter(domain, "input1", derivedEntity);

        action._parameters.Add(parameter);

        var sourceEffect = new TestEffect(domain);
        sourceEffect.Produces("output1", baseEntity);

        var effect = new InvokeAction(domain) { TargetAction = action };

        await Assert.That(() => effect.BindParameterFrom("input1", sourceEffect, "output1"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task InvokeAction_BindParameterFrom_ThrowsWhenSourceDoesNotProduceOutput() {
        var domain = CreateDomain();
        var entity = CreateEntity(domain, "Person");
        var action = CreateAction(domain, "DoSomething", entity);
        var paramType = CreatePrimitive(domain, "Text");
        var parameter = CreateParameter(domain, "input1", paramType);

        // Add parameter to action
        action._parameters.Add(parameter);

        var sourceEffect = new TestEffect(domain);

        var effect = new InvokeAction(domain) { TargetAction = action };

        await Assert.That(() => effect.BindParameterFrom("input1", sourceEffect, "nonexistent"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CrossEffectWiring_BindOutputTo_VerifyWiring() {
        var domain = CreateDomain();
        var entity = CreateEntity(domain, "Person");
        var action = CreateAction(domain, "DoSomething", entity);
        var paramType = CreatePrimitive(domain, "Text");
        var parameter = CreateParameter(domain, "input1", paramType);

        // Add parameter to action
        action._parameters.Add(parameter);

        var effectA = new TestEffect(domain);
        var effectB = new InvokeAction(domain) { TargetAction = action };

        effectA.Produces("output1", paramType);

        effectA.BindOutputTo("output1", effectB, "input1");

        await Assert.That(effectB.IncomingBindings).IsNotEmpty();
        await Assert.That(effectB.IncomingBindings["input1"].SourceEffectName).IsEqualTo(effectA.GetType().Name);
        await Assert.That(effectB.IncomingBindings["input1"].OutputName).IsEqualTo("output1");
    }

    private sealed record TestDomainValue(Domain domain, string name, DomainType type) : DomainValue(domain, name, type);
}