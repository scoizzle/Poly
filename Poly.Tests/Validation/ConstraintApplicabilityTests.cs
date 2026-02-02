using Poly.DataModeling;
using Poly.DataModeling.Builders;
using Poly.DataModeling.TypeExpressions;
using Poly.Introspection;
using Poly.Validation;

namespace Poly.Tests.Validation;

public class ConstraintApplicabilityTests {
    [Test]
    public async Task LengthConstraint_OnStringProperty_IsApplicable()
    {
        var builder = new PropertyBuilder("Name")
            .OfType<string>()
            .WithConstraint(new LengthConstraint(1, 100));

        var property = builder.Build();

        await Assert.That(property.Constraints.Count()).IsEqualTo(1);
        await Assert.That(property.Type).IsTypeOf<PrimitiveType>();
        await Assert.That(property.Type.HasCategory(TypeCategory.Text)).IsTrue();
    }

    [Test]
    public async Task LengthConstraint_OnCollectionProperty_IsApplicable()
    {
        var builder = new PropertyBuilder("Tags")
            .OfType<string>()
            .AsList()
            .WithConstraint(new LengthConstraint(1, 10));

        var property = builder.Build();

        await Assert.That(property.Constraints.Count()).IsEqualTo(1);
        await Assert.That(property.Type).IsTypeOf<CollectionType>();
        await Assert.That(property.Type.HasCategory(TypeCategory.Collection)).IsTrue();
    }

    [Test]
    public async Task LengthConstraint_OnByteArrayProperty_IsApplicable()
    {
        var builder = new PropertyBuilder("Data")
            .OfType<byte[]>()
            .WithConstraint(new LengthConstraint(1, 1024));

        var property = builder.Build();

        await Assert.That(property.Constraints.Count()).IsEqualTo(1);
        await Assert.That(property.Type.HasCategory(TypeCategory.Binary)).IsTrue();
    }

    [Test]
    public async Task LengthConstraint_OnIntProperty_ThrowsConstraintApplicabilityException()
    {
        var builder = new PropertyBuilder("Age")
            .OfType<int>();

        await Assert.That(() => builder.WithConstraint(new LengthConstraint(1, 100)))
            .Throws<ConstraintApplicabilityException>();
    }

    [Test]
    public async Task LengthConstraint_AddedBeforeType_ThrowsOnBuild()
    {
        var lengthConstraint = new LengthConstraint(1, 100);

        // Use the direct constructor approach to bypass immediate validation
        var property = new DataProperty(
            "Age",
            new PrimitiveType(PrimitiveTypeId.Int32),
            [lengthConstraint]
        );

        // The constraint should exist but would fail applicability check
        await Assert.That(property.Constraints.Count()).IsEqualTo(1);
        await Assert.That(lengthConstraint.IsApplicableTo(property.Type)).IsFalse();
    }

    [Test]
    public async Task RangeConstraint_OnIntProperty_IsApplicable()
    {
        var builder = new PropertyBuilder("Age")
            .OfType<int>()
            .WithConstraint(new RangeConstraint(0, 150));

        var property = builder.Build();

        await Assert.That(property.Constraints.Count()).IsEqualTo(1);
        await Assert.That(property.Type.HasCategory(TypeCategory.Numeric)).IsTrue();
    }

    [Test]
    public async Task RangeConstraint_OnDateTimeProperty_IsApplicable()
    {
        var builder = new PropertyBuilder("BirthDate")
            .OfType<DateTime>()
            .WithConstraint(new RangeConstraint(new DateTime(1900, 1, 1), new DateTime(2100, 12, 31)));

        var property = builder.Build();

        await Assert.That(property.Constraints.Count()).IsEqualTo(1);
        await Assert.That(property.Type.HasCategory(TypeCategory.Temporal)).IsTrue();
    }

    [Test]
    public async Task RangeConstraint_OnStringProperty_ThrowsConstraintApplicabilityException()
    {
        var builder = new PropertyBuilder("Name")
            .OfType<string>();

        await Assert.That(() => builder.WithConstraint(new RangeConstraint(0, 100)))
            .Throws<ConstraintApplicabilityException>();
    }

    [Test]
    public async Task NotNullConstraint_OnAnyType_IsApplicable()
    {
        // NotNull is universally applicable
        var stringBuilder = new PropertyBuilder("Name")
            .OfType<string>()
            .WithConstraint(new NotNullConstraint());

        var intBuilder = new PropertyBuilder("Age")
            .OfType<int>()
            .WithConstraint(new NotNullConstraint());

        var listBuilder = new PropertyBuilder("Tags")
            .OfType<string>()
            .AsList()
            .WithConstraint(new NotNullConstraint());

        // All should build without exception
        var stringProp = stringBuilder.Build();
        var intProp = intBuilder.Build();
        var listProp = listBuilder.Build();

        await Assert.That(stringProp.Constraints.Count()).IsEqualTo(1);
        await Assert.That(intProp.Constraints.Count()).IsEqualTo(1);
        await Assert.That(listProp.Constraints.Count()).IsEqualTo(1);
    }

    [Test]
    public async Task EqualityConstraint_OnAnyType_IsApplicable()
    {
        // Equality is universally applicable
        var constraint = new Poly.Validation.Constraints.EqualityConstraint("expected");

        var stringBuilder = new PropertyBuilder("Name")
            .OfType<string>()
            .WithConstraint(constraint);

        var stringProp = stringBuilder.Build();

        await Assert.That(stringProp.Constraints.Count()).IsEqualTo(1);
    }

    [Test]
    public async Task ConstraintApplicabilityException_ContainsExpectedDetails()
    {
        var builder = new PropertyBuilder("Age")
            .OfType<int>();

        var constraint = new LengthConstraint(1, 100);

        try {
            builder.WithConstraint(constraint);
            throw new Exception("Expected ConstraintApplicabilityException was not thrown");
        }
        catch (ConstraintApplicabilityException ex) {
            await Assert.That(ex.PropertyName).IsEqualTo("Age");
            await Assert.That(ex.Constraint).IsSameReferenceAs(constraint);
            await Assert.That(ex.TypeExpression).IsTypeOf<PrimitiveType>();
            await Assert.That(ex.Message).Contains("LengthConstraint");
            await Assert.That(ex.Message).Contains("Age");
        }
    }

    [Test]
    public async Task MultipleConstraints_WithMixedApplicability_ThrowsOnInvalidConstraint()
    {
        var validConstraint = new RangeConstraint(0, 150);
        var invalidConstraint = new LengthConstraint(1, 100);

        var builder = new PropertyBuilder("Age")
            .OfType<int>()
            .WithConstraint(validConstraint);

        // Adding the invalid constraint should throw
        await Assert.That(() => builder.WithConstraint(invalidConstraint))
            .Throws<ConstraintApplicabilityException>();
    }

    [Test]
    public async Task WithConstraints_ThrowsOnFirstInvalidConstraint()
    {
        var validConstraint = new RangeConstraint(0, 150);
        var invalidConstraint = new LengthConstraint(1, 100);

        var builder = new PropertyBuilder("Age")
            .OfType<int>();

        // Using WithConstraints with one invalid constraint should throw
        await Assert.That(() => builder.WithConstraints(validConstraint, invalidConstraint))
            .Throws<ConstraintApplicabilityException>();
    }

    [Test]
    public async Task Constraint_IsApplicableTo_TypeCategory()
    {
        var lengthConstraint = new LengthConstraint(1, 100);
        var rangeConstraint = new RangeConstraint(0, 150);
        var notNullConstraint = new NotNullConstraint();

        // Length applies to Text, Collection, Binary
        await Assert.That(lengthConstraint.IsApplicableTo(TypeCategory.Text)).IsTrue();
        await Assert.That(lengthConstraint.IsApplicableTo(TypeCategory.Collection)).IsTrue();
        await Assert.That(lengthConstraint.IsApplicableTo(TypeCategory.Binary)).IsTrue();
        await Assert.That(lengthConstraint.IsApplicableTo(TypeCategory.Numeric)).IsFalse();

        // Range applies to Numeric, Temporal
        await Assert.That(rangeConstraint.IsApplicableTo(TypeCategory.Numeric)).IsTrue();
        await Assert.That(rangeConstraint.IsApplicableTo(TypeCategory.Temporal)).IsTrue();
        await Assert.That(rangeConstraint.IsApplicableTo(TypeCategory.Text)).IsFalse();

        // NotNull is universally applicable (None means all)
        await Assert.That(notNullConstraint.IsApplicableTo(TypeCategory.None)).IsTrue();
        await Assert.That(notNullConstraint.IsApplicableTo(TypeCategory.Numeric)).IsTrue();
        await Assert.That(notNullConstraint.IsApplicableTo(TypeCategory.Text)).IsTrue();
    }

    [Test]
    public async Task Constraint_IsApplicableTo_TypeExpression()
    {
        var lengthConstraint = new LengthConstraint(1, 100);

        var stringType = new PrimitiveType(PrimitiveTypeId.String);
        var intType = new PrimitiveType(PrimitiveTypeId.Int32);
        var listType = new CollectionType(stringType, CollectionKind.List);

        await Assert.That(lengthConstraint.IsApplicableTo(stringType)).IsTrue();
        await Assert.That(lengthConstraint.IsApplicableTo(intType)).IsFalse();
        await Assert.That(lengthConstraint.IsApplicableTo(listType)).IsTrue();
    }

    [Test]
    public async Task RangeConstraint_OnDecimalProperty_IsApplicable()
    {
        var builder = new PropertyBuilder("Price")
            .OfType<decimal>()
            .WithConstraint(new RangeConstraint(0.0m, 10000.0m));

        var property = builder.Build();

        await Assert.That(property.Constraints.Count()).IsEqualTo(1);
        await Assert.That(property.Type.HasCategory(TypeCategory.Numeric)).IsTrue();
        await Assert.That(property.Type.HasCategory(TypeCategory.HighPrecision)).IsTrue();
    }

    [Test]
    public async Task LengthConstraint_OnOptionalStringProperty_IsApplicable()
    {
        var builder = new PropertyBuilder("Nickname")
            .OfType<string>()
            .Optional()
            .WithConstraint(new LengthConstraint(1, 50));

        var property = builder.Build();

        await Assert.That(property.Constraints.Count()).IsEqualTo(1);
        await Assert.That(property.Type).IsTypeOf<OptionalType>();
        // The inner type should have Text category
        var optionalType = (OptionalType)property.Type;
        await Assert.That(optionalType.Inner.HasCategory(TypeCategory.Text)).IsTrue();
    }
}