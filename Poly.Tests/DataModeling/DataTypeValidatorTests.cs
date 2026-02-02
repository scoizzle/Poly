using Poly.DataModeling;
using Poly.DataModeling.Builders;
using Poly.Validation;
using Poly.Validation.Rules;

namespace Poly.Tests.DataModeling;

public class DataTypeValidatorTests {
    public record Person(string Name, int Age, string? Email);
    public record Product(string Name, decimal Price, string[] Tags);

    [Test]
    public async Task DataTypeValidator_WithNameLengthConstraint_ValidatesCorrectly()
    {
        var dataType = new DataTypeBuilder("Person")
            .AddProperty("Name", p => p.OfType<string>().WithConstraint(new LengthConstraint(1, 50)))
            .AddProperty("Age", p => p.OfType<int>())
            .AddProperty("Email", p => p.OfType<string>().Optional())
            .Build();

        var validator = DataTypeValidator.Create<Person>(dataType);

        var validPerson = new Person("Alice", 30, null);
        var invalidPerson = new Person("", 30, null);

        await Assert.That(validator.Validate(validPerson)).IsTrue();
        await Assert.That(validator.Validate(invalidPerson)).IsFalse();
    }

    [Test]
    public async Task DataTypeValidator_WithAgeRangeConstraint_ValidatesCorrectly()
    {
        var dataType = new DataTypeBuilder("Person")
            .AddProperty("Name", p => p.OfType<string>())
            .AddProperty("Age", p => p.OfType<int>().WithConstraint(new RangeConstraint(0, 150)))
            .AddProperty("Email", p => p.OfType<string>().Optional())
            .Build();

        var validator = DataTypeValidator.Create<Person>(dataType);

        var validPerson = new Person("Alice", 30, null);
        var tooOldPerson = new Person("Bob", 200, null);
        var negativePerson = new Person("Charlie", -5, null);

        await Assert.That(validator.Validate(validPerson)).IsTrue();
        await Assert.That(validator.Validate(tooOldPerson)).IsFalse();
        await Assert.That(validator.Validate(negativePerson)).IsFalse();
    }

    [Test]
    public async Task DataTypeValidator_WithMultipleConstraintsOnProperty_ValidatesAll()
    {
        var dataType = new DataTypeBuilder("Person")
            .AddProperty("Name", p => p.OfType<string>()
                .WithConstraint(new NotNullConstraint())
                .WithConstraint(new LengthConstraint(1, 50)))
            .AddProperty("Age", p => p.OfType<int>())
            .AddProperty("Email", p => p.OfType<string>().Optional())
            .Build();

        var validator = DataTypeValidator.Create<Person>(dataType);

        var validPerson = new Person("Alice", 30, null);
        var emptyNamePerson = new Person("", 30, null);
        var tooLongNamePerson = new Person(new string('A', 100), 30, null);

        await Assert.That(validator.Validate(validPerson)).IsTrue();
        await Assert.That(validator.Validate(emptyNamePerson)).IsFalse();
        await Assert.That(validator.Validate(tooLongNamePerson)).IsFalse();
    }

    [Test]
    public async Task DataTypeValidator_WithMultiplePropertyConstraints_ValidatesAll()
    {
        var dataType = new DataTypeBuilder("Person")
            .AddProperty("Name", p => p.OfType<string>().WithConstraint(new LengthConstraint(1, 50)))
            .AddProperty("Age", p => p.OfType<int>().WithConstraint(new RangeConstraint(0, 150)))
            .AddProperty("Email", p => p.OfType<string>().Optional())
            .Build();

        var validator = DataTypeValidator.Create<Person>(dataType);

        var validPerson = new Person("Alice", 30, null);
        var invalidNamePerson = new Person("", 30, null);
        var invalidAgePerson = new Person("Bob", 200, null);
        var bothInvalidPerson = new Person("", 200, null);

        await Assert.That(validator.Validate(validPerson)).IsTrue();
        await Assert.That(validator.Validate(invalidNamePerson)).IsFalse();
        await Assert.That(validator.Validate(invalidAgePerson)).IsFalse();
        await Assert.That(validator.Validate(bothInvalidPerson)).IsFalse();
    }

    [Test]
    public async Task DataTypeValidator_WithDecimalPrice_ValidatesRangeCorrectly()
    {
        var dataType = new DataTypeBuilder("Product")
            .AddProperty("Name", p => p.OfType<string>())
            .AddProperty("Price", p => p.OfType<decimal>().WithConstraint(new RangeConstraint(0.01m, 10000m)))
            .AddProperty("Tags", p => p.OfType<string>().AsArray())
            .Build();

        var validator = DataTypeValidator.Create<Product>(dataType);

        var validProduct = new Product("Widget", 9.99m, []);
        var freeProduct = new Product("Freebie", 0m, []);
        var expensiveProduct = new Product("Luxury", 50000m, []);

        await Assert.That(validator.Validate(validProduct)).IsTrue();
        await Assert.That(validator.Validate(freeProduct)).IsFalse();
        await Assert.That(validator.Validate(expensiveProduct)).IsFalse();
    }

    [Test]
    public async Task DataTypeValidator_WithNoConstraints_AlwaysPasses()
    {
        var dataType = new DataTypeBuilder("Person")
            .AddProperty("Name", p => p.OfType<string>())
            .AddProperty("Age", p => p.OfType<int>())
            .AddProperty("Email", p => p.OfType<string>().Optional())
            .Build();

        var validator = DataTypeValidator.Create<Person>(dataType);

        var person1 = new Person("Alice", 30, null);
        var person2 = new Person("", -100, "invalid");

        // Without constraints, all instances should be valid
        await Assert.That(validator.Validate(person1)).IsTrue();
        await Assert.That(validator.Validate(person2)).IsTrue();
    }

    [Test]
    public async Task DataTypeValidator_ExposesRuleInterpretation()
    {
        var dataType = new DataTypeBuilder("Person")
            .AddProperty("Name", p => p.OfType<string>().WithConstraint(new LengthConstraint(1, 50)))
            .AddProperty("Age", p => p.OfType<int>())
            .AddProperty("Email", p => p.OfType<string>().Optional())
            .Build();

        var validator = DataTypeValidator.Create<Person>(dataType);

        await Assert.That(validator.RuleInterpretation).IsNotNull();
        await Assert.That(validator.CombinedRule).IsNotNull();
        await Assert.That(validator.ExpressionTree).IsNotNull();
        await Assert.That(validator.Predicate).IsNotNull();
    }

    [Test]
    public async Task DataTypeValidator_WithTypeLevelRules_ValidatesCorrectly()
    {
        // Type-level rule: Age must be greater than MinAge threshold
        // Using RangeConstraint on Age property to require age > 18
        var dataType = new DataTypeBuilder("Person")
            .AddProperty("Name", p => p.OfType<string>())
            .AddProperty("Age", p => p.OfType<int>().WithConstraint(new RangeConstraint(19, null))) // MinValue 19 = greater than 18
            .AddProperty("Email", p => p.OfType<string>().Optional())
            .Build();

        var validator = DataTypeValidator.Create<Person>(dataType);

        var adultPerson = new Person("Alice", 30, null);
        var minorPerson = new Person("Bob", 15, null);
        var exactlyEighteen = new Person("Charlie", 18, null);
        var justNineteen = new Person("Diana", 19, null);

        await Assert.That(validator.Validate(adultPerson)).IsTrue();
        await Assert.That(validator.Validate(minorPerson)).IsFalse();
        await Assert.That(validator.Validate(exactlyEighteen)).IsFalse();
        await Assert.That(validator.Validate(justNineteen)).IsTrue();
    }

    [Test]
    public async Task DataTypeValidator_ToString_DescribesRules()
    {
        var dataType = new DataTypeBuilder("Person")
            .AddProperty("Name", p => p.OfType<string>().WithConstraint(new LengthConstraint(1, 50)))
            .AddProperty("Age", p => p.OfType<int>())
            .AddProperty("Email", p => p.OfType<string>().Optional())
            .Build();

        var validator = DataTypeValidator.Create<Person>(dataType);

        var description = validator.ToString();

        await Assert.That(description).Contains("Name");
    }
}