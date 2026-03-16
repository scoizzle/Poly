using Poly.DomainModeling.V2.Core;

namespace Poly.Tests.DomainModeling.V2;

public class TypeExpressionTests {
    [Test]
    public async Task TryParse_Primitives_Valid()
    {
        var ok = TypeExpression.TryParse("string", out var kind, out var referenced);

        await Assert.That(ok).IsTrue();
        await Assert.That(kind).IsEqualTo(TypeExpressionKind.Primitive);
        await Assert.That(referenced).IsNull();
    }

    [Test]
    public async Task TryParse_PrimitiveNullable_Valid()
    {
        var ok = TypeExpression.TryParse("int?", out var kind, out _);

        await Assert.That(ok).IsTrue();
        await Assert.That(kind).IsEqualTo(TypeExpressionKind.PrimitiveNullable);
    }

    [Test]
    public async Task TryParse_TypeReference_Valid()
    {
        var ok = TypeExpression.TryParse("Billing.Invoice", out var kind, out var referenced);

        await Assert.That(ok).IsTrue();
        await Assert.That(kind).IsEqualTo(TypeExpressionKind.TypeReference);
        await Assert.That(referenced).IsEqualTo("Billing.Invoice");
    }

    [Test]
    public async Task TryParse_TypeReferenceNullable_Valid()
    {
        var ok = TypeExpression.TryParse("Billing.Invoice?", out var kind, out var referenced);

        await Assert.That(ok).IsTrue();
        await Assert.That(kind).IsEqualTo(TypeExpressionKind.TypeReferenceNullable);
        await Assert.That(referenced).IsEqualTo("Billing.Invoice");
    }

    [Test]
    public async Task TryParse_ListVariants_Valid()
    {
        var primitiveListOk = TypeExpression.TryParse("string[]", out var primitiveListKind, out _);
        var typeListOk = TypeExpression.TryParse("Billing.Invoice[]", out var typeListKind, out var referenced);

        await Assert.That(primitiveListOk).IsTrue();
        await Assert.That(primitiveListKind).IsEqualTo(TypeExpressionKind.PrimitiveList);
        await Assert.That(typeListOk).IsTrue();
        await Assert.That(typeListKind).IsEqualTo(TypeExpressionKind.TypeReferenceList);
        await Assert.That(referenced).IsEqualTo("Billing.Invoice");
    }

    [Test]
    public async Task TryParse_InvalidInputs_False()
    {
        await Assert.That(TypeExpression.TryParse("", out _, out _)).IsFalse();
        await Assert.That(TypeExpression.TryParse(" ", out _, out _)).IsFalse();
        await Assert.That(TypeExpression.TryParse("unknown", out _, out _)).IsFalse();
        await Assert.That(TypeExpression.TryParse("Invoice", out _, out _)).IsFalse();
        await Assert.That(TypeExpression.TryParse("string[][]", out _, out _)).IsFalse();
        await Assert.That(TypeExpression.TryParse("string[]?", out _, out _)).IsFalse();
    }
}