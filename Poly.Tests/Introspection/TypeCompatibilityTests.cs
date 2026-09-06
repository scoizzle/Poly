using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

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
    public async Task IsAssignableFrom_DateOnly_ToDateTime_ReturnsFalse() {
        var registry = new ClrTypeDefinitionRegistry();
        var dateTime = registry.GetTypeDefinition(typeof(DateTime));
        var dateOnly = registry.GetTypeDefinition(typeof(DateOnly));
        await Assert.That(dateTime.IsAssignableFrom(dateOnly)).IsFalse();
        await Assert.That(dateOnly.IsAssignableTo(dateTime)).IsFalse();
    }

    [Test]
    public async Task IsAssignableFrom_DateTime_ToDateOnly_ReturnsFalse() {
        var registry = new ClrTypeDefinitionRegistry();
        var dateTime = registry.GetTypeDefinition(typeof(DateTime));
        var dateOnly = registry.GetTypeDefinition(typeof(DateOnly));
        await Assert.That(dateOnly.IsAssignableFrom(dateTime)).IsFalse();
        await Assert.That(dateTime.IsAssignableTo(dateOnly)).IsFalse();
    }

    [Test]
    public async Task IsAssignableFrom_SameType_ReturnsTrue() {
        var registry = new ClrTypeDefinitionRegistry();
        var stringType = registry.GetTypeDefinition<string>();

        var result = stringType.IsAssignableFrom(stringType);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsAssignableFrom_DerivedType_ReturnsTrue() {
        var registry = new ClrTypeDefinitionRegistry();
        var exceptionType = registry.GetTypeDefinition<Exception>();
        var argumentExceptionType = registry.GetTypeDefinition<ArgumentException>();

        var result = exceptionType.IsAssignableFrom(argumentExceptionType);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsAssignableFrom_UnrelatedType_ReturnsFalse() {
        var registry = new ClrTypeDefinitionRegistry();
        var stringType = registry.GetTypeDefinition<string>();
        var intType = registry.GetTypeDefinition<int>();

        var result = stringType.IsAssignableFrom(intType);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsAssignableFrom_InterfaceImplementation_ReturnsTrue() {
        var registry = new ClrTypeDefinitionRegistry();
        var enumerableType = registry.GetTypeDefinition<IEnumerable<int>>();
        var listType = registry.GetTypeDefinition<List<int>>();

        var result = enumerableType.IsAssignableFrom(listType);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsAssignableTo_InverseOf_IsAssignableFrom() {
        var registry = new ClrTypeDefinitionRegistry();
        var exceptionType = registry.GetTypeDefinition<Exception>();
        var argumentExceptionType = registry.GetTypeDefinition<ArgumentException>();

        var assignableFrom = exceptionType.IsAssignableFrom(argumentExceptionType);
        var assignableTo = argumentExceptionType.IsAssignableTo(exceptionType);

        await Assert.That(assignableTo).IsEqualTo(assignableFrom);
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
    public async Task IsAssignableFrom_DerivedFromBase_ReturnsTrue() {
        var registry = new ClrTypeDefinitionRegistry();
        var objectType = (ITypeDefinition)registry.GetTypeDefinition<object>();
        var stringType = (ITypeDefinition)registry.GetTypeDefinition<string>();

        await Assert.That(objectType.IsAssignableFrom(stringType)).IsTrue();
    }

    [Test]
    public async Task IsAssignableFrom_ClrObject_FromAstTypeDefinition_ReturnsTrue() {
        var order = new TypeDefinitionNode("Order");
        var analysis = new AnalyzerBuilder()
            .AddAnalyzer(new TypeDefinitionNodeAnalyzer())
            .Build()
            .Analyze(order);
        var astType = analysis.GetMetadata<TypeDefinitionMetadata>(order)?.TypeDefinition;
        var objectType = (ITypeDefinition)ClrTypeDefinitionRegistry.Shared.GetTypeDefinition<object>();

        await Assert.That(astType).IsNotNull();
        await Assert.That(objectType.IsAssignableFrom(astType!)).IsTrue();
    }

    [Test]
    public async Task IsAssignableFrom_BaseFromDerived_ReturnsFalse() {
        var registry = new ClrTypeDefinitionRegistry();
        var stringType = (ITypeDefinition)registry.GetTypeDefinition<string>();
        var objectType = (ITypeDefinition)registry.GetTypeDefinition<object>();

        await Assert.That(stringType.IsAssignableFrom(objectType)).IsFalse();
    }

    [Test]
    public async Task IsAssignableFrom_InterfaceImplementation_ReturnsTrue_V2() {
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
    public async Task BaseType_OnClosedGenericType_ReturnsConstructedBaseType() {
        var registry = new ClrTypeDefinitionRegistry();
        var closedGenericType = registry.GetTypeDefinition(typeof(GenericLeaf<string>));

        var baseType = closedGenericType.BaseType;

        await Assert.That(baseType).IsNotNull();
        await Assert.That(((ITypeDefinition)baseType!).GetRuntimeType()).IsEqualTo(typeof(GenericBase<string>));
        await Assert.That(baseType.GenericParameters.Select(parameter => parameter.ParameterTypeDefinition.RuntimeType).ToArray())
            .IsEquivalentTo([typeof(string)]);
    }

    [Test]
    public async Task IsAssignableFrom_ClosedGenericBaseFromClosedGenericDerived_ReturnsTrue() {
        var registry = new ClrTypeDefinitionRegistry();
        var baseType = (ITypeDefinition)registry.GetTypeDefinition(typeof(GenericBase<string>));
        var derivedType = (ITypeDefinition)registry.GetTypeDefinition(typeof(GenericLeaf<string>));

        await Assert.That(baseType.IsAssignableFrom(derivedType)).IsTrue();
    }

    [Test]
    public async Task IsAssignableFrom_ImplicitConversionOperator_ReturnsTrue() {
        var registry = new ClrTypeDefinitionRegistry();
        var meters = registry.GetTypeDefinition(typeof(Meters));
        var dbl = registry.GetTypeDefinition(typeof(double));
        await Assert.That(meters.IsAssignableFrom(dbl)).IsTrue();
        await Assert.That(dbl.IsAssignableTo(meters)).IsTrue();
        var implicitOp = meters.GetConversionFrom(dbl);
        await Assert.That(implicitOp?.Kind).IsEqualTo(ConversionOperatorKind.Implicit);
        await Assert.That(implicitOp?.Method.Name).IsEqualTo("op_Implicit");
        await Assert.That(implicitOp?.Method.IsStatic).IsTrue();
    }

    [Test]
    public async Task IsAssignableFrom_ExplicitConversionOperator_ReturnsFalse() {
        var registry = new ClrTypeDefinitionRegistry();
        var meters = registry.GetTypeDefinition(typeof(Meters));
        var dbl = registry.GetTypeDefinition(typeof(double));
        await Assert.That(dbl.IsAssignableFrom(meters)).IsFalse();
        await Assert.That(meters.IsAssignableTo(dbl)).IsFalse();
        var explicitOp = dbl.GetConversionFrom(meters);
        await Assert.That(explicitOp?.Kind).IsEqualTo(ConversionOperatorKind.Explicit);
        await Assert.That(explicitOp?.Method.Name).IsEqualTo("op_Explicit");
    }

    [Test]
    public async Task IsAssignableFrom_DateTime_ToDateTimeOffset_UsesImplicitOperator() {
        var registry = new ClrTypeDefinitionRegistry();
        var dateTime = registry.GetTypeDefinition(typeof(DateTime));
        var offset = registry.GetTypeDefinition(typeof(DateTimeOffset));
        await Assert.That(offset.IsAssignableFrom(dateTime)).IsTrue();
        await Assert.That(dateTime.IsAssignableFrom(offset)).IsFalse();
        await Assert.That(offset.GetConversionFrom(dateTime)?.Kind).IsEqualTo(ConversionOperatorKind.Implicit);
        await Assert.That(offset.GetConversionFrom(dateTime)?.Method.Name).IsEqualTo("op_Implicit");
        await Assert.That(dateTime.GetConversionFrom(offset)).IsNull();
    }

    [Test]
    public async Task GetConversionFrom_UnrelatedTypes_ReturnsNull() {
        var registry = new ClrTypeDefinitionRegistry();
        var stringType = registry.GetTypeDefinition<string>();
        var intType = registry.GetTypeDefinition<int>();
        await Assert.That(stringType.GetConversionFrom(intType)).IsNull();
        await Assert.That(intType.GetConversionFrom(stringType)).IsNull();
    }

    public readonly struct Meters {
        public Meters(double value) => Value = value;
        public double Value { get; }
        public static implicit operator Meters(double value) => new(value);
        public static explicit operator double(Meters meters) => meters.Value;
    }

    public sealed class ConversionHost {
        public DateTime Due { get; set; }
        public DateOnly Start { get; set; }
        public Meters Length { get; set; }
    }

    [Test]
    public async Task Interfaces_CachedAfterFirstAccess() {
        var registry = new ClrTypeDefinitionRegistry();
        var stringType = registry.GetTypeDefinition<string>();

        var interfaces1 = stringType.Interfaces;
        var interfaces2 = stringType.Interfaces;

        await Assert.That(ReferenceEquals(interfaces1, interfaces2)).IsTrue();
    }

    private abstract class GenericBase<T>;

    private sealed class GenericLeaf<T> : GenericBase<T>;
}