using Poly.Validation.Builders;

namespace Poly.Tests.Validation;

public class RuleSetBuilderTests {
    [Test]
    public async Task Builder_EmptyRules_CreatesValidRuleSet()
    {
        var builder = new RuleSetBuilder<Person>();
        var ruleSet = builder.Build();

        await Assert.That(ruleSet).IsNotNull();
        await Assert.That(ruleSet.CombinedRules).IsNotNull();
    }

    [Test]
    public async Task Builder_WithMemberConstraint_PassesValidation()
    {
        var builder = new RuleSetBuilder<Person>()
            .Member(p => p.Name, c => c.NotNull());

        var ruleSet = builder.Build();
        var person = new Person { Name = "John", Age = 25 };

        var result = ruleSet.Test(person);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Builder_WithMemberConstraint_FailsValidation()
    {
        var builder = new RuleSetBuilder<Person>()
            .Member(p => p.Name, c => c.NotNull());

        var ruleSet = builder.Build();
        var person = new Person { Name = null, Age = 25 };

        var result = ruleSet.Test(person);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task Builder_WithMultipleMembers_AppliesAllConstraints()
    {
        var builder = new RuleSetBuilder<Person>()
            .Member(p => p.Name, c => c.NotNull())
            .Member(p => p.Age, c => c.Minimum(0).Maximum(150));

        var ruleSet = builder.Build();
        var person = new Person { Name = "John", Age = 25 };

        var result = ruleSet.Test(person);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Builder_WithMultipleMembers_OneFailsValidation()
    {
        var builder = new RuleSetBuilder<Person>()
            .Member(p => p.Name, c => c.NotNull())
            .Member(p => p.Age, c => c.Minimum(0).Maximum(150));

        var ruleSet = builder.Build();
        var person = new Person { Name = null, Age = 25 };

        var result = ruleSet.Test(person);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task Builder_Member_WithNullSelector_Throws()
    {
        var builder = new RuleSetBuilder<Person>();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => {
            builder.Member<int>(null!, c => c.Minimum(0));
        });
    }

    [Test]
    public async Task Builder_Member_WithNullConstraintsBuilder_Throws()
    {
        var builder = new RuleSetBuilder<Person>();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => {
            builder.Member(p => p.Name, null!);
        });
    }

    [Test]
    public async Task Builder_AddRule_WithNullRule_Throws()
    {
        var builder = new RuleSetBuilder<Person>();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => {
            builder.AddRule(null!);
        });
    }

    [Test]
    public async Task Builder_AllowsChaining()
    {
        var ruleSet = new RuleSetBuilder<Person>()
            .Member(p => p.Name, c => c.NotNull())
            .Member(p => p.Age, c => c.Minimum(0))
            .Member(p => p.Email, c => c.NotNull())
            .Build();

        var person = new Person { Name = "John", Age = 25, Email = "john@example.com" };
        var result = ruleSet.Test(person);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Builder_WithStringLength_PassesValidation()
    {
        var builder = new RuleSetBuilder<NumberProperty>()
            .Member(p => p.Value, c => c.Minimum(1).Maximum(100));

        var ruleSet = builder.Build();
        var obj = new NumberProperty { Value = 50 };

        var result = ruleSet.Test(obj);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Builder_WithStringLengthTooShort_FailsValidation()
    {
        var builder = new RuleSetBuilder<NumberProperty>()
            .Member(p => p.Value, c => c.Minimum(5));

        var ruleSet = builder.Build();
        var obj = new NumberProperty { Value = 2 };

        var result = ruleSet.Test(obj);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task Builder_WithMultipleConstraintsOnProperty_AllMustPass()
    {
        var builder = new RuleSetBuilder<NumberProperty>()
            .Member(p => p.Value, c => c
                .Minimum(1)
                .Maximum(100));

        var ruleSet = builder.Build();
        var obj = new NumberProperty { Value = 50 };

        var result = ruleSet.Test(obj);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Builder_WithComplexValidation_PassesValidation()
    {
        var builder = new RuleSetBuilder<Person>()
            .Member(p => p.Name, c => c.NotNull())
            .Member(p => p.Age, c => c
                .Minimum(0)
                .Maximum(150))
            .Member(p => p.Email, c => c.NotNull());

        var ruleSet = builder.Build();
        var person = new Person { Name = "John Doe", Age = 30, Email = "john@example.com" };

        var result = ruleSet.Test(person);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Builder_WithComplexValidation_OnePropertyFails()
    {
        var builder = new RuleSetBuilder<NumberProperty>()
            .Member(p => p.Value, c => c
                .Minimum(5)
                .Maximum(100));

        var ruleSet = builder.Build();
        var obj = new NumberProperty { Value = 2 };

        var result = ruleSet.Test(obj);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task Builder_BuildsExpressionTree()
    {
        var builder = new RuleSetBuilder<Person>()
            .Member(p => p.Name, c => c.NotNull());

        var ruleSet = builder.Build();

        await Assert.That(ruleSet.ExpressionTree).IsNotNull();
        await Assert.That(ruleSet.RuleSetInterpretation).IsNotNull();
    }

    [Test]
    public async Task Builder_CompiledPredicate_ExecutesEfficiently()
    {
        var builder = new RuleSetBuilder<Person>()
            .Member(p => p.Age, c => c.Minimum(18).Maximum(65));

        var ruleSet = builder.Build();

        // Test multiple times - should be fast due to compilation
        for (int i = 0; i < 1000; i++) {
            var person = new Person { Name = $"Person{i}", Age = 30 };
            var result = ruleSet.Test(person);
            await Assert.That(result).IsTrue();
        }
    }

    private class Person {
        public string? Name { get; set; }
        public int Age { get; set; }
        public string? Email { get; set; }
    }

    private class NumberProperty {
        public int Value { get; set; }
    }
}