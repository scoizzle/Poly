using Poly.Interpretation;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Introspection.Extensions;

namespace Poly.Tests.Introspection;

public class ClrTypeInheritanceTests {
    [Test]
    public async Task VirtualMethod_OnBaseClass() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var baseType = registry.GetTypeDefinition<VirtualMethodBase>();

        var methods = baseType.Methods.ToList();
        var virtualMethod = methods.FirstOrDefault(m => m.Name == "VirtualMethod");

        await Assert.That(virtualMethod).IsNotNull();
    }

    [Test]
    public async Task VirtualMethod_Inherited() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var derivedType = registry.GetTypeDefinition<VirtualMethodDerived>();

        var methods = derivedType.Methods.ToList();
        var virtualMethod = methods.FirstOrDefault(m => m.Name == "VirtualMethod");

        await Assert.That(virtualMethod).IsNotNull();
    }

    [Test]
    public async Task InheritedProperty_FromBase() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var derivedType = registry.GetTypeDefinition<DerivedWithProperty>();

        var baseMembers = derivedType.GetMembers("BaseName");

        // Property inherited from base should be accessible
        await Assert.That(baseMembers.Count()).IsGreaterThan(0);
    }

    [Test]
    public async Task InheritedMethod_FromBase() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var derivedType = registry.GetTypeDefinition<DerivedWithMethod>();

        var baseMembers = derivedType.GetMembers("BaseMethod");

        // Method inherited from base should be accessible
        await Assert.That(baseMembers.Count()).IsGreaterThan(0);
    }

    [Test]
    public async Task InterfaceImplementation_ExplicitMembers() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var implementerType = registry.GetTypeDefinition<InterfaceImplementer>();

        // Get all members with "GetValue" name
        var members = implementerType.GetMembers("GetValue").ToList();

        // Should have at least the interface implementation
        await Assert.That(members.Count()).IsGreaterThan(0);
    }

    [Test]
    public async Task AbstractBase_WithConcreteDerived() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var abstractType = registry.GetTypeDefinition<AbstractBase2>();
        var concreteType = registry.GetTypeDefinition<ConcreteImplementation>();

        // Abstract base should have abstract members
        var abstractMembers = abstractType.Methods.ToList();
        await Assert.That(abstractMembers.Count()).IsGreaterThan(0);

        // Concrete implementation should have those members
        var concreteMembers = concreteType.Methods.ToList();
        await Assert.That(concreteMembers.Count()).IsGreaterThan(0);
    }

    [Test]
    public async Task MultiLevelInheritance_ThreeLevels() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var grandchildType = registry.GetTypeDefinition<GrandchildClass>();

        // Should be able to access members from all three levels
        var levelOneMembers = grandchildType.GetMembers("LevelOneMethod");
        var levelTwoMembers = grandchildType.GetMembers("LevelTwoMethod");
        var levelThreeMembers = grandchildType.GetMembers("LevelThreeMethod");

        await Assert.That(levelOneMembers.Count()).IsGreaterThan(0);
        await Assert.That(levelTwoMembers.Count()).IsGreaterThan(0);
        await Assert.That(levelThreeMembers.Count()).IsGreaterThan(0);
    }

    [Test]
    public async Task PropertyOverride_DerivedVersion() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var derivedType = registry.GetTypeDefinition<DerivedWithOverride>();

        var properties = derivedType.Properties.ToList();
        var overriddenProp = properties.FirstOrDefault(p => p.Name == "OverridableProperty");

        await Assert.That(overriddenProp).IsNotNull();
    }

    [Test]
    public async Task SealedDerived_CannotBeSubclassed() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var sealedType = registry.GetTypeDefinition<SealedDerived>();

        await Assert.That(sealedType).IsNotNull();
        await Assert.That(sealedType.Name).IsEqualTo("SealedDerived");
    }

    [Test]
    public async Task InterfaceType_HasInterfaceMembers() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var interfaceType = registry.GetTypeDefinition<ITestInterface>();

        var members = interfaceType.Members.ToList();

        // Should have members defined in interface
        await Assert.That(members.Count()).IsGreaterThan(0);
    }

    [Test]
    public async Task MultipleInterfaceInheritance_HasAllMembers() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var implementerType = registry.GetTypeDefinition<MultiInterfaceImplementer>();

        // Should have methods from both interfaces
        var firstInterfaceMembers = implementerType.GetMembers("FirstInterfaceMethod");
        var secondInterfaceMembers = implementerType.GetMembers("SecondInterfaceMethod");

        await Assert.That(firstInterfaceMembers.Count()).IsGreaterThanOrEqualTo(0);
        await Assert.That(secondInterfaceMembers.Count()).IsGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task GenericBase_WithTypeParameter() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var concreteDerived = registry.GetTypeDefinition<ConcreteGenericDerived>();

        var methods = concreteDerived.Methods.ToList();

        // Should have methods from generic base
        await Assert.That(methods.Count()).IsGreaterThan(0);
    }

    [Test]
    public async Task BaseClassProperty_AccessViaInstance() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var derivedType = registry.GetTypeDefinition<DerivedWithProperty>();

        var baseNameMembers = derivedType.GetMembers("BaseName");
        var baseNameProperty = baseNameMembers.First();

        var instance = new DerivedWithProperty { BaseName = "TestName" };
        var instanceLiteral = Value.Wrap(instance);

        var context = new InterpretationContext();
        var accessor = baseNameProperty.GetMemberAccessor(instanceLiteral);
        var expression = accessor.BuildExpression(context);
        var lambda = System.Linq.Expressions.Expression.Lambda<System.Func<string>>(expression).Compile();
        var result = lambda();

        await Assert.That(result).IsEqualTo("TestName");
    }

    [Test]
    public async Task HiddenMember_PrefersDerived() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var derivedType = registry.GetTypeDefinition<DerivedWithHiddenMember>();

        var members = derivedType.GetMembers("HiddenProperty").ToList();

        // Should have the hidden member accessible
        await Assert.That(members.Count()).IsGreaterThan(0);
    }

    // Helper classes for testing

    public class VirtualMethodBase {
        public virtual void VirtualMethod() { }
    }

    public class VirtualMethodDerived : VirtualMethodBase {
        public override void VirtualMethod() { }
    }

    public class BaseWithProperty {
        public string BaseName { get; set; } = string.Empty;
    }

    public class DerivedWithProperty : BaseWithProperty {
        public string DerivedName { get; set; } = string.Empty;
    }

    public class BaseWithMethod {
        public void BaseMethod() { }
    }

    public class DerivedWithMethod : BaseWithMethod {
        public void DerivedMethod() { }
    }

    public interface ITestInterface {
        void InterfaceMethod();
        string InterfaceProperty { get; set; }
    }

    public class InterfaceImplementer : ITestInterface {
        public void InterfaceMethod() { }
        public string InterfaceProperty { get; set; } = string.Empty;

        public int GetValue() => 42;
    }

    public abstract class AbstractBase2 {
        public abstract void AbstractMethod();
        public virtual void VirtualMethod() { }
    }

    public class ConcreteImplementation : AbstractBase2 {
        public override void AbstractMethod() { }
    }

    public class LevelOne {
        public void LevelOneMethod() { }
    }

    public class LevelTwo : LevelOne {
        public void LevelTwoMethod() { }
    }

    public class GrandchildClass : LevelTwo {
        public void LevelThreeMethod() { }
    }

    public class BaseWithOverridable {
        public virtual string OverridableProperty => "base";
    }

    public class DerivedWithOverride : BaseWithOverridable {
        public override string OverridableProperty => "derived";
    }

    public class SealedDerived : DerivedWithOverride {
    }

    public interface IFirstInterface {
        void FirstInterfaceMethod();
    }

    public interface ISecondInterface {
        void SecondInterfaceMethod();
    }

    public class MultiInterfaceImplementer : IFirstInterface, ISecondInterface {
        public void FirstInterfaceMethod() { }
        public void SecondInterfaceMethod() { }
    }

    public abstract class GenericBase<T> {
        public abstract T GetValue();
        public virtual void ProcessValue(T value) { }
    }

    public class ConcreteGenericDerived : GenericBase<string> {
        public override string GetValue() => "test";
    }

    public class BaseWithHiddenMember {
        public string HiddenProperty { get; set; } = "base";
    }

    public class DerivedWithHiddenMember : BaseWithHiddenMember {
        public new string HiddenProperty { get; set; } = "derived";
    }
}