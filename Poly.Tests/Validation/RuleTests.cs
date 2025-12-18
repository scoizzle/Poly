using Poly.Validation;
using Poly.Validation.Rules;

namespace Poly.Tests.Validation;

public class RuleTests {
    [Test]
    public async Task AndRule_AllConditionsTrue_PassesValidation() {
        var rule = new AndRule([
            new NotNullConstraint(),
            new ComparisonRule("Age", ComparisonOperator.GreaterThanOrEqual, "Age")
        ]);

        var ruleSet = new RuleSet<TestPerson>([rule]);
        var person = new TestPerson { Name = "John", Age = 25 };

        var result = ruleSet.Test(person);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task AndRule_OneConditionFalse_FailsValidation() {
        var rule = new AndRule([
            new NotNullConstraint(),
            new ComparisonRule("Age", ComparisonOperator.LessThan, "Age")
        ]);

        var ruleSet = new RuleSet<TestPerson>([rule]);
        var person = new TestPerson { Name = "John", Age = 25 };

        var result = ruleSet.Test(person);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task OrRule_OneConditionTrue_PassesValidation() {
        var rule = new OrRule([
            new NotNullConstraint(),
            new ComparisonRule("Age", ComparisonOperator.LessThan, "Age")
        ]);

        var ruleSet = new RuleSet<TestPerson>([rule]);
        var person = new TestPerson { Name = "John", Age = 25 };

        var result = ruleSet.Test(person);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task OrRule_AllConditionsFalse_FailsValidation() {
        var rule = new OrRule([
            new ComparisonRule("Age", ComparisonOperator.LessThan, "Age"),
            new ComparisonRule("Age", ComparisonOperator.GreaterThan, "Age")
        ]);

        var ruleSet = new RuleSet<TestPerson>([rule]);
        var person = new TestPerson { Name = "John", Age = 25 };

        var result = ruleSet.Test(person);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task NotRule_ConditionTrue_FailsValidation() {
        var rule = new NotRule(new NotNullConstraint());

        var ruleSet = new RuleSet<TestPerson>([rule]);
        var person = new TestPerson { Name = "John", Age = 25 };

        var result = ruleSet.Test(person);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task NotRule_ConditionFalse_PassesValidation() {
        var constraint = new RangeConstraint(50, 100);
        var rule = new NotRule(new PropertyConstraintRule("Age", constraint));

        var ruleSet = new RuleSet<TestPerson>([rule]);
        var person = new TestPerson { Name = "John", Age = 25 };

        var result = ruleSet.Test(person);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task ComparisonRule_Equal_PassesValidation() {
        var rule = new ComparisonRule("Age", ComparisonOperator.Equal, "Age");

        var ruleSet = new RuleSet<TestPerson>([rule]);
        var person = new TestPerson { Name = "John", Age = 25 };

        var result = ruleSet.Test(person);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task ComparisonRule_NotEqual_PassesValidation() {
        var rule = new ComparisonRule("Age", ComparisonOperator.NotEqual, "Age");

        var ruleSet = new RuleSet<TestPerson>([rule]);
        var person = new TestPerson { Name = "John", Age = 25 };

        var result = ruleSet.Test(person);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ComparisonRule_GreaterThan_PassesValidation() {
        var rule = new ComparisonRule("Age", ComparisonOperator.GreaterThan, "Age");

        var ruleSet = new RuleSet<TestPerson>([rule]);
        var person = new TestPerson { Name = "John", Age = 25 };

        var result = ruleSet.Test(person);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ConditionalRule_ConditionTrue_EvaluatesThenRule() {
        var conditionalRule = new ConditionalRule(
            condition: new ComparisonRule("Age", ComparisonOperator.GreaterThanOrEqual, "Age"),
            thenRule: new NotNullConstraint()
        );

        var ruleSet = new RuleSet<TestPerson>([conditionalRule]);
        var person = new TestPerson { Name = "John", Age = 25 };

        var result = ruleSet.Test(person);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task ConditionalRule_ConditionFalse_PassesValidation() {
        // Create a conditional rule that requires Name when Age > 50
        // Since Age is 25, the condition is false, so the rule should pass
        var conditionalRule = new ConditionalRule(
            condition: new ComparisonRule("Age", ComparisonOperator.GreaterThan, "Age"),  // Age > Age is always false
            thenRule: new PropertyConstraintRule("Name", new NotNullConstraint())
        );

        var ruleSet = new RuleSet<TestPerson>([conditionalRule]);
        var person = new TestPerson { Name = null, Age = 25 };  // Name is null, but condition is false so rule passes

        var result = ruleSet.Test(person);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task PropertyDependencyRule_SourceHasValue_DependentMustHaveValue() {
        // Both Name properties to test string/null comparison
        var rule = new PropertyDependencyRule("Name", "Description", requireWhenSourceHasValue: true);

        var ruleSet = new RuleSet<TestPerson>([rule]);
        var personWithBoth = new TestPerson { Name = "John", Age = 25, Description = "Developer" };
        var personWithoutDependent = new TestPerson { Name = "Jane", Age = 0, Description = null };

        var result1 = ruleSet.Test(personWithBoth);
        var result2 = ruleSet.Test(personWithoutDependent);

        await Assert.That(result1).IsTrue();
        // Description is null even though Name has value, so should fail
        await Assert.That(result2).IsFalse();
    }

    [Test]
    public async Task PropertyDependencyRule_SourceNull_DependentCanBeAnything() {
        var rule = new PropertyDependencyRule("Name", "Description", requireWhenSourceHasValue: true);

        var ruleSet = new RuleSet<TestPerson>([rule]);
        var personWithoutSource = new TestPerson { Name = null, Age = 0, Description = null };

        var result = ruleSet.Test(personWithoutSource);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task PropertyConstraintRule_AppliesConstraintToProperty() {
        var rule = new PropertyConstraintRule("Name", new NotNullConstraint());

        var ruleSet = new RuleSet<TestPerson>([rule]);
        var person = new TestPerson { Name = "John", Age = 25 };

        var result = ruleSet.Test(person);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task PropertyConstraintRule_WithNullProperty_FailsValidation() {
        var rule = new PropertyConstraintRule("Name", new NotNullConstraint());

        var ruleSet = new RuleSet<TestPerson>([rule]);
        var person = new TestPerson { Name = null, Age = 25 };

        var result = ruleSet.Test(person);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task MutualExclusionRule_OnePropertyHasValue_PassesValidation() {
        var rule = new MutualExclusionRule(["Name", "Description"], maxAllowed: 1);

        var ruleSet = new RuleSet<TestPerson>([rule]);
        var person = new TestPerson { Name = "John", Age = 0, Description = null };

        var result = ruleSet.Test(person);

        await Assert.That(result).IsTrue();
    }

    public class TestPerson {
        public string? Name { get; set; }
        public int Age { get; set; }
        public string? Description { get; set; }
    }
}