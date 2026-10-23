using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Tests.Introspection;

namespace Poly.Tests.Interpretation;

public class TypeCastTests {
    [Test]
    public async Task TypeCast_IntToDouble_ReturnsDouble() {
        // Arrange
        var node = new TypeCast(Wrap(42), TypeReference.To<double>());

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<double>()).IsEqualTo(42.0);
    }

    [Test]
    public async Task TypeCast_DoubleToInt_ReturnsInt() {
        // Arrange
        var node = new TypeCast(Wrap(3.14), TypeReference.To<int>());

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<int>()).IsEqualTo(3);
    }

    [Test]
    public async Task TypeCast_LongToInt_ReturnsInt() {
        // Arrange
        var node = new TypeCast(Wrap(9999L), TypeReference.To<int>());

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<int>()).IsEqualTo(9999);
    }

    [Test]
    public async Task TypeCast_WithParameter_EvaluatesCorrectly() {
        // Arrange
        var param = new Parameter("value", TypeReference.To<int>());
        var node = new TypeCast(param, TypeReference.To<double>());

        // Act
        var program = Interpreter.Compile(node);
        using var exec = Interpreter.Execute(program, s => s.SetArgs(42));

        // Assert
        await Assert.That(exec.GetValue<double>()).IsEqualTo(42.0);
    }

    [Test]
    public async Task TypeCast_StringToObject_WorksCorrectly() {
        // Arrange
        var node = new TypeCast(Wrap("hello"), TypeReference.To<object>());

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<object>()).IsEqualTo("hello");
    }

    [Test]
    public async Task TypeCast_ObjectToString_WorksCorrectly() {
        // Arrange
        var obj = (object)"world";
        var node = new TypeCast(Wrap(obj), TypeReference.To<string>());

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<string>()).IsEqualTo("world");
    }

    [Test]
    public async Task TypeCast_NullableToNonNullable_WorksCorrectly() {
        // Arrange
        var node = new TypeCast(Wrap(42 as int?), TypeReference.To<int>());

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<int>()).IsEqualTo(42);
    }

    [Test]
    public async Task TypeCast_NonNullableToNullable_WorksCorrectly() {
        // Arrange
        var node = new TypeCast(Wrap(42), TypeReference.To<int?>());

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<int>()).IsEqualTo(42);
    }

    [Test]
    public async Task TypeCast_GetTypeDefinition_ReturnsTargetType() {
        var node = new TypeCast(Wrap(42), TypeReference.To<double>());
        var analysis = Interpreter.Analyze(node);
        var resolved = analysis.GetResolvedType(node);
        await Assert.That(resolved).IsNotNull();
        await Assert.That(resolved!.GetRuntimeType()).IsEqualTo(typeof(double));
    }

    [Test]
    public async Task TypeCast_ToString_ReturnsExpectedFormat() {
        // Arrange
        var node = new TypeCast(Wrap(42), TypeReference.To<double>());

        // Act
        var result = node.ToString();

        // Assert
        await Assert.That(result).Contains("System.Double");
    }

    [Test]
    public async Task TypeCast_WithNullArguments_ThrowsArgumentNullException() {
        // Act & Assert
        await Assert.That(() => new TypeCast(null!, TypeReference.To<double>())).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task TypeIs_ObjectAgainstString_ReturnsTrue() {
        // Arrange
        var value = Wrap((object)"hello");
        var node = new TypeIs(value, TypeReference.To<string>());

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<bool>()).IsTrue();
    }

    [Test]
    public async Task TypeIs_ObjectAgainstInt_ReturnsFalse() {
        // Arrange
        var value = Wrap((object)"hello");
        var node = new TypeIs(value, TypeReference.To<int>());

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<bool>()).IsFalse();
    }

    [Test]
    public async Task TypeAs_ObjectToString_ReturnsStringValue() {
        // Arrange
        var value = Wrap((object)"hello");
        var node = new TypeAs(value, TypeReference.To<string>());

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<string>()).IsEqualTo("hello");
    }

    [Test]
    public async Task TypeAs_ObjectToString_WithIncompatibleValue_ReturnsNull() {
        // Arrange
        var value = Wrap((object)42);
        var node = new TypeAs(value, TypeReference.To<string>());

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<object>()).IsNull();
    }

    [Test]
    public async Task TypeCast_ImplicitOperator_DoubleToMeters() {
        var node = new TypeCast(Wrap(2.5), TypeReference.To<TypeCompatibilityTests.Meters>());
        var analysis = Interpreter.Analyze(node);
        await Assert.That(analysis.GetNodeReplacement(node)).IsTypeOf<Invoke>();
        using var exec = Interpreter.Execute(Interpreter.Compile(node, analysis));
        await Assert.That(exec.GetValue<TypeCompatibilityTests.Meters>().Value).IsEqualTo(2.5);
    }

    [Test]
    public async Task TypeCast_ExplicitOperator_MetersToDouble() {
        var node = new TypeCast(
            Wrap(new TypeCompatibilityTests.Meters(2.5)),
            TypeReference.To<double>());
        var analysis = Interpreter.Analyze(node);
        await Assert.That(analysis.GetNodeReplacement(node)).IsTypeOf<Invoke>();
        using var exec = Interpreter.Execute(Interpreter.Compile(node, analysis));
        await Assert.That(exec.GetValue<double>()).IsEqualTo(2.5);
    }

    [Test]
    public async Task TypeCast_ImplicitOperator_DateTimeToDateTimeOffset() {
        var stamp = new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);
        var node = new TypeCast(Wrap(stamp), TypeReference.To<DateTimeOffset>());
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<DateTimeOffset>()).IsEqualTo(new DateTimeOffset(stamp));
    }

    [Test]
    public async Task TypeAs_ObjectToNonNullableValueType_RejectsAtCompile() {
        // Arrange
        var value = Wrap((object)42);
        var node = new TypeAs(value, TypeReference.To<int>());

        // Act & Assert - a lifted `as` to a non-nullable value type cannot be represented, so the VM refuses it
        await Assert.That(() => Interpreter.Compile(node)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task TypeOperations_Extensions_IsAndAs_CreateExpectedNodes() {
        // Arrange
        var operand = Wrap((object)"hello");

        // Act
        var isNode = operand.Is(TypeReference.To<string>());
        var asNode = operand.As(TypeReference.To<string>());

        // Assert
        await Assert.That(isNode.TargetTypeReference).IsTypeOf<TypeReference>();
        await Assert.That(asNode.TargetTypeReference).IsTypeOf<TypeReference>();
    }
}