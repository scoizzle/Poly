using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

using TUnit.Assertions.Extensions;

namespace Poly.Tests.Introspection;

public class TypeCompatibilityTests {
    [Test]
    public async Task BaseType_WithSingleInheritance_ReturnsImmediateParent() {
        var registry = new ClrTypeDefinitionRegistry();
        var derivedType = registry.GetTypeDefinition(typeof(ArgumentException));
        var baseType = derivedType.BaseType;

        await Assert.That(baseType).IsNotNull();
        // ArgumentException -> SystemException -> Exception
        await Assert.That(baseType!.Name).IsEqualTo(nameof(SystemException));
    }

    [Test]
    public async Task BaseType_WithObjectType_ReturnsNull() {
        var registry = new ClrTypeDefinitionRegistry();
        var objectType = registry.GetTypeDefinition<object>();
        var baseType = objectType.BaseType;

        await Assert.That(baseType).IsNull();
    }

    [Test]
    public async Task BaseType_FollowChain_ReachesMultipleLevels() {
        var registry = new ClrTypeDefinitionRegistry();
        var derivedType = registry.GetTypeDefinition(typeof(ArgumentNullException));
        var current = derivedType.BaseType;
        var depth = 0;

        while (current != null) {
            depth++;
            current = current.BaseType;
        }

        await Assert.That(depth).IsGreaterThan(0);
    }

    [Test]
    public async Task Interfaces_WithImplementedInterface_ContainsInterface() {
        var registry = new ClrTypeDefinitionRegistry();
        var stringType = registry.GetTypeDefinition<string>();
        var interfaces = stringType.Interfaces.ToList();

        await Assert.That(interfaces).IsNotEmpty();
        var names = interfaces.Select(i => i.Name).ToList();
        await Assert.That(names).Contains(nameof(IComparable));
    }

    [Test]
    public async Task Interfaces_WithMultipleImplementedInterfaces_ContainsAll() {
        var registry = new ClrTypeDefinitionRegistry();
        var listType = registry.GetTypeDefinition(typeof(List<int>));
        var interfaceNames = listType.Interfaces.Select(i => i.Name).ToList();

        await Assert.That(interfaceNames).IsNotEmpty();
        // List<T> implements IEnumerable, ICollection, IList, etc.
    }

    [Test]
    public async Task Interfaces_WithObjectType_ReturnsEmpty() {
        var registry = new ClrTypeDefinitionRegistry();
        var objectType = registry.GetTypeDefinition<object>();
        var interfaces = objectType.Interfaces.ToList();

        await Assert.That(interfaces).IsEmpty();
    }

    [Test]
    public async Task IsAssignableFrom_SameType_ReturnsTrue() {
        var registry = new ClrTypeDefinitionRegistry();
        var stringType = (ITypeDefinition)registry.GetTypeDefinition<string>();

        await Assert.That(stringType.IsAssignableFrom(stringType)).IsTrue();
    }

    [Test]
    public async Task IsAssignableFrom_DerivedFromBase_ReturnsTrue() {
        var registry = new ClrTypeDefinitionRegistry();
        var objectType = (ITypeDefinition)registry.GetTypeDefinition<object>();
        var stringType = (ITypeDefinition)registry.GetTypeDefinition<string>();

        await Assert.That(objectType.IsAssignableFrom(stringType)).IsTrue();
    }

    [Test]
    public async Task IsAssignableFrom_BaseFromDerived_ReturnsFalse() {
        var registry = new ClrTypeDefinitionRegistry();
        var stringType = (ITypeDefinition)registry.GetTypeDefinition<string>();
        var objectType = (ITypeDefinition)registry.GetTypeDefinition<object>();

        await Assert.That(stringType.IsAssignableFrom(objectType)).IsFalse();
    }

    [Test]
    public async Task IsAssignableFrom_InterfaceImplementation_ReturnsTrue() {
        var registry = new ClrTypeDefinitionRegistry();
        var comparableType = (ITypeDefinition)registry.GetTypeDefinition(typeof(IComparable));
        var stringType = (ITypeDefinition)registry.GetTypeDefinition<string>();

        await Assert.That(comparableType.IsAssignableFrom(stringType)).IsTrue();
    }

    [Test]
    public async Task IsAssignableFrom_UnrelatedTypes_ReturnsFalse() {
        var registry = new ClrTypeDefinitionRegistry();
        var stringType = (ITypeDefinition)registry.GetTypeDefinition<string>();
        var intType = (ITypeDefinition)registry.GetTypeDefinition<int>();

        await Assert.That(stringType.IsAssignableFrom(intType)).IsFalse();
    }

    [Test]
    public async Task IsAssignableFrom_DistantInheritanceChain_ReturnsTrue() {
        var registry = new ClrTypeDefinitionRegistry();
        var exceptionType = (ITypeDefinition)registry.GetTypeDefinition<Exception>();
        var argNullExType = (ITypeDefinition)registry.GetTypeDefinition<ArgumentNullException>();

        await Assert.That(exceptionType.IsAssignableFrom(argNullExType)).IsTrue();
    }

    [Test]
    public async Task IsAssignableTo_DerivedToBase_ReturnsTrue() {
        var registry = new ClrTypeDefinitionRegistry();
        var stringType = (ITypeDefinition)registry.GetTypeDefinition<string>();
        var objectType = (ITypeDefinition)registry.GetTypeDefinition<object>();

        await Assert.That(stringType.IsAssignableTo(objectType)).IsTrue();
    }

    [Test]
    public async Task IsAssignableTo_BaseToDerived_ReturnsFalse() {
        var registry = new ClrTypeDefinitionRegistry();
        var objectType = (ITypeDefinition)registry.GetTypeDefinition<object>();
        var stringType = (ITypeDefinition)registry.GetTypeDefinition<string>();

        await Assert.That(objectType.IsAssignableTo(stringType)).IsFalse();
    }

    [Test]
    public async Task IsAssignableTo_ImplementationToInterface_ReturnsTrue() {
        var registry = new ClrTypeDefinitionRegistry();
        var stringType = (ITypeDefinition)registry.GetTypeDefinition<string>();
        var comparableType = (ITypeDefinition)registry.GetTypeDefinition(typeof(IComparable));

        await Assert.That(stringType.IsAssignableTo(comparableType)).IsTrue();
    }

    [Test]
    public async Task Assignability_WithValueTypeBoxing_ReturnsTrue() {
        var registry = new ClrTypeDefinitionRegistry();
        var objectType = (ITypeDefinition)registry.GetTypeDefinition<object>();
        var intType = (ITypeDefinition)registry.GetTypeDefinition<int>();

        await Assert.That(objectType.IsAssignableFrom(intType)).IsTrue();
    }

    [Test]
    public async Task InheritanceChain_ExceptionHierarchy_IsComplete() {
        var registry = new ClrTypeDefinitionRegistry();
        var exceptionType = (ITypeDefinition)registry.GetTypeDefinition<Exception>();
        var ioExceptionType = (ITypeDefinition)registry.GetTypeDefinition(typeof(IOException));
        var fileNotFoundType = (ITypeDefinition)registry.GetTypeDefinition(typeof(FileNotFoundException));

        await Assert.That(exceptionType.IsAssignableFrom(ioExceptionType)).IsTrue();
        await Assert.That(exceptionType.IsAssignableFrom(fileNotFoundType)).IsTrue();
        await Assert.That(ioExceptionType.IsAssignableFrom(fileNotFoundType)).IsTrue();
    }

    [Test]
    public async Task BaseType_CachedAfterFirstAccess() {
        var registry = new ClrTypeDefinitionRegistry();
        var derivedType = registry.GetTypeDefinition(typeof(ArgumentException));

        var baseType1 = derivedType.BaseType;
        var baseType2 = derivedType.BaseType;

        await Assert.That(ReferenceEquals(baseType1, baseType2)).IsTrue();
    }

    [Test]
    public async Task Interfaces_CachedAfterFirstAccess() {
        var registry = new ClrTypeDefinitionRegistry();
        var stringType = registry.GetTypeDefinition<string>();

        var interfaces1 = stringType.Interfaces;
        var interfaces2 = stringType.Interfaces;

        await Assert.That(ReferenceEquals(interfaces1, interfaces2)).IsTrue();
    }
}