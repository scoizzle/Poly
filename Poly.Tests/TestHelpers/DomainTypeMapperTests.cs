using System.ComponentModel.DataAnnotations;

using Poly.DomainModeling.Ontology;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// Tests for <see cref="DomainTypeMapper"/> — proves that CLR types can be
/// reflected into domain entity definitions and identifies gaps in the mapping.
/// </summary>
public class DomainTypeMapperTests {
    // ── Sample types for reflection ─────────────────────────────

    private sealed record Person(
        string Name,
        int Age,
        bool IsActive
    );

    private sealed record Product(
        string Sku,
        string Name,
        decimal Price,
        int Stock
    );

    private sealed record Order(
        string OrderId,
        DateTime OrderDate,
        decimal Total,
        string Status,
        Person Customer
    );

    private sealed record PersonWithConstraints(
        [Required] string Name,
        [Range(0, 150)] int Age,
        [StringLength(100)] string Email,
        [RegularExpression(@"\d{3}-\d{2}-\d{4}")] string Ssn
    );

    /// <summary>
    /// Same constraints as <see cref="PersonWithConstraints"/> but using
    /// explicit <c>[property: ...]</c> target syntax. Both must produce
    /// identical domain property definitions.
    /// </summary>
    private sealed record PersonWithExplicitPropertyAttributes(
        [property: Required] string Name,
        [property: Range(0, 150)] int Age,
        [property: StringLength(100)] string Email,
        [property: RegularExpression(@"\d{3}-\d{2}-\d{4}")] string Ssn
    );

    private sealed record WithNullable(
        int? Age,
        string? Name
    );

    // ── CLR → Domain type mapping ──────────────────────────────────

    [Test]
    public async Task Map_Int_ToNumber() {
        await Assert.That(DomainTypeMapper.ClrTypeToDomainName(typeof(int)))
            .IsEqualTo("Number");
    }

    [Test]
    public async Task Map_String_ToText() {
        await Assert.That(DomainTypeMapper.ClrTypeToDomainName(typeof(string)))
            .IsEqualTo("Text");
    }

    [Test]
    public async Task Map_Decimal_ToNumber() {
        await Assert.That(DomainTypeMapper.ClrTypeToDomainName(typeof(decimal)))
            .IsEqualTo("Number");
    }

    [Test]
    public async Task Map_Bool_ToBoolean() {
        await Assert.That(DomainTypeMapper.ClrTypeToDomainName(typeof(bool)))
            .IsEqualTo("Boolean");
    }

    [Test]
    public async Task Map_DateTime_ToDateTime() {
        await Assert.That(DomainTypeMapper.ClrTypeToDomainName(typeof(DateTime)))
            .IsEqualTo("DateTime");
    }

    [Test]
    public async Task Map_NullableInt_UnwrapsToNumber() {
        await Assert.That(DomainTypeMapper.ClrTypeToDomainName(typeof(int?)))
            .IsEqualTo("Number");
    }

    [Test]
    public async Task Map_NullableString_UnwrapsToText() {
        // Nullable<string> isn't valid C# for a value type constraint,
        // but the helper should handle reference types returning string
        await Assert.That(DomainTypeMapper.ClrTypeToDomainName(typeof(string)))
            .IsEqualTo("Text");
    }

    [Test]
    public async Task Map_UnknownType_ReturnsNull() {
        await Assert.That(DomainTypeMapper.ClrTypeToDomainName(typeof(DomainTypeMapper)))
            .IsNull();
    }

    // ── Attribute → Constraint mapping ──────────────────────────

    [Test]
    public async Task Map_RequiredAttribute_ToRequiredConstraint() {
        var attr = new RequiredAttribute();
        var constraint = DomainTypeMapper.ClrAttributeToConstraint(attr);
        await Assert.That(constraint).IsTypeOf<RequiredConstraint>();
    }

    [Test]
    public async Task Map_RangeAttribute_ToRangeConstraint() {
        var attr = new RangeAttribute(0, 100);
        var constraint = DomainTypeMapper.ClrAttributeToConstraint(attr);
        await Assert.That(constraint).IsTypeOf<RangeConstraint>();
        var range = (RangeConstraint)constraint!;
        await Assert.That(range.Minimum).IsEqualTo(0);
        await Assert.That(range.Maximum).IsEqualTo(100);
    }

    [Test]
    public async Task Map_StringLengthAttribute_ToLengthConstraint() {
        var attr = new StringLengthAttribute(50);
        var constraint = DomainTypeMapper.ClrAttributeToConstraint(attr);
        await Assert.That(constraint).IsTypeOf<LengthConstraint>();
        var len = (LengthConstraint)constraint!;
        await Assert.That(len.MinLength).IsEqualTo(0);
        await Assert.That(len.MaxLength).IsEqualTo(50);
    }

    [Test]
    public async Task Map_MinLengthAttribute_ToLengthConstraint() {
        var attr = new MinLengthAttribute(3);
        var constraint = DomainTypeMapper.ClrAttributeToConstraint(attr);
        await Assert.That(constraint).IsTypeOf<LengthConstraint>();
    }

    [Test]
    public async Task Map_MaxLengthAttribute_ToLengthConstraint() {
        var attr = new MaxLengthAttribute(200);
        var constraint = DomainTypeMapper.ClrAttributeToConstraint(attr);
        await Assert.That(constraint).IsTypeOf<LengthConstraint>();
    }

    [Test]
    public async Task Map_RegularExpressionAttribute_ToPatternConstraint() {
        var attr = new RegularExpressionAttribute(@"\d+");
        var constraint = DomainTypeMapper.ClrAttributeToConstraint(attr);
        await Assert.That(constraint).IsTypeOf<PatternConstraint>();
    }

    // ── Type → Properties ───────────────────────────────────────

    [Test]
    public async Task PersonType_ReflectsThreeProperties() {
        var props = DomainTypeMapper.ToProperties<Person>();
        await Assert.That(props.Count()).IsEqualTo(3);

        var name = props.Single(p => p.Name == "Name");
        await Assert.That(name.Type.TypeName).IsEqualTo("Text");

        var age = props.Single(p => p.Name == "Age");
        await Assert.That(age.Type.TypeName).IsEqualTo("Number");

        var active = props.Single(p => p.Name == "IsActive");
        await Assert.That(active.Type.TypeName).IsEqualTo("Boolean");
    }

    [Test]
    public async Task ProductType_ReflectsFourProperties() {
        var props = DomainTypeMapper.ToProperties<Product>();
        await Assert.That(props.Count()).IsEqualTo(4);
    }

    [Test]
    public async Task PersonWithConstraints_ReflectsConstraints() {
        var props = DomainTypeMapper.ToProperties<PersonWithConstraints>();

        var name = props.Single(p => p.Name == "Name");
        await Assert.That(name.Constraints.Count).IsEqualTo(1);
        await Assert.That(name.Constraints[0]).IsTypeOf<RequiredConstraint>();

        var age = props.Single(p => p.Name == "Age");
        await Assert.That(age.Constraints.Count).IsEqualTo(1);
        await Assert.That(age.Constraints[0]).IsTypeOf<RangeConstraint>();

        var email = props.Single(p => p.Name == "Email");
        await Assert.That(email.Constraints.Count).IsEqualTo(1);
        await Assert.That(email.Constraints[0]).IsTypeOf<LengthConstraint>();

        var ssn = props.Single(p => p.Name == "Ssn");
        await Assert.That(ssn.Constraints.Count).IsEqualTo(1);
        await Assert.That(ssn.Constraints[0]).IsTypeOf<PatternConstraint>();
    }

    [Test]
    public async Task PersonWithExplicitPropertyAttributes_ReflectsConstraints() {
        // [property: Attr] syntax places attributes directly on the property,
        // so GetCustomAttributes() on the PropertyInfo finds them.
        // The constructor-parameter fallback is not needed here — this proves
        // the primary path works for the explicit syntax.
        var props = DomainTypeMapper.ToProperties<PersonWithExplicitPropertyAttributes>();

        var name = props.Single(p => p.Name == "Name");
        await Assert.That(name.Constraints.Count).IsEqualTo(1);
        await Assert.That(name.Constraints[0]).IsTypeOf<RequiredConstraint>();

        var age = props.Single(p => p.Name == "Age");
        await Assert.That(age.Constraints.Count).IsEqualTo(1);
        await Assert.That(age.Constraints[0]).IsTypeOf<RangeConstraint>();

        var email = props.Single(p => p.Name == "Email");
        await Assert.That(email.Constraints.Count).IsEqualTo(1);
        await Assert.That(email.Constraints[0]).IsTypeOf<LengthConstraint>();

        var ssn = props.Single(p => p.Name == "Ssn");
        await Assert.That(ssn.Constraints.Count).IsEqualTo(1);
        await Assert.That(ssn.Constraints[0]).IsTypeOf<PatternConstraint>();
    }

    // ── Unknown type gap identification ─────────────────────────

    [Test]
    public async Task OrderType_WithCustomer_ThrowsForCustomType() {
        // Person is not a primitive; the mapper should throw
        await Assert.That(() => DomainTypeMapper.ToProperties<Order>())
            .Throws<NotSupportedException>();
    }

    // ── Entity-from-type integration ────────────────────────────

    [Test]
    public async Task EntityFromPerson_CreatesDomainWithCorrectProperties() {
        var domain = DomainTypeMapper.CreateDomainWithEntity<Person>("TestDomain");

        var entity = domain.Types.OfType<Entity>().Single();
        await Assert.That(entity.Name).IsEqualTo("Person");
        await Assert.That(entity.Properties.Count).IsEqualTo(3);
        await Assert.That(entity.Properties.Any(p => p.Name == "Name" && p.Type.TypeName == "Text")).IsTrue();
        await Assert.That(entity.Properties.Any(p => p.Name == "Age" && p.Type.TypeName == "Number")).IsTrue();
        await Assert.That(entity.Properties.Any(p => p.Name == "IsActive" && p.Type.TypeName == "Boolean")).IsTrue();
    }

    [Test]
    public async Task EntityFromPerson_CustomEntityName() {
        var domain = DomainTypeMapper.CreateDomainWithEntity<Person>("Test", "User");

        var entity = domain.Types.OfType<Entity>().Single();
        await Assert.That(entity.Name).IsEqualTo("User");
    }

    [Test]
    public async Task EntityFromPerson_WithConstraints_ReflectedOnProperty() {
        var domain = DomainTypeMapper.CreateDomainWithEntity<PersonWithConstraints>("Test");

        var entity = domain.Types.OfType<Entity>().Single();
        var nameProp = entity.Properties.Single(p => p.Name == "Name");
        await Assert.That(nameProp.Constraints.Count).IsEqualTo(1);
        await Assert.That(nameProp.Constraints[0]).IsTypeOf<RequiredConstraint>();
    }

    // ── Nullable support ────────────────────────────────────────

    [Test]
    public async Task NullableInt_ReflectsAsNumber() {
        var props = DomainTypeMapper.ToProperties<WithNullable>();
        var age = props.Single(p => p.Name == "Age");
        await Assert.That(age.Type.TypeName).IsEqualTo("Number");

        var name = props.Single(p => p.Name == "Name");
        await Assert.That(name.Type.TypeName).IsEqualTo("Text");
    }
}