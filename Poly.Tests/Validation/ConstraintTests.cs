using Poly.Validation.Rules;
using Poly.Validation;
using Poly.Validation.Constraints;

namespace Poly.Tests.Validation;

public class ConstraintTests {
    [Test]
    public async Task NotNullConstraint_WithNullValue_FailsValidation() {
        var ruleSet = new RuleSet<Person>([
            new PropertyConstraintRule("Name", new NotNullConstraint())
        ]);
        
        var person = new Person { Name = null, Age = 25 };
        var result = ruleSet.Test(person);
        
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task NotNullConstraint_WithValue_PassesValidation() {
        var ruleSet = new RuleSet<Person>([
            new PropertyConstraintRule("Name", new NotNullConstraint())
        ]);
        
        var person = new Person { Name = "John", Age = 25 };
        var result = ruleSet.Test(person);
        
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task RangeConstraint_WithinRange_PassesValidation() {
        var constraint = new RangeConstraint(0, 150);
        var ruleSet = new RuleSet<Person>([
            new PropertyConstraintRule("Age", constraint)
        ]);
        
        var person = new Person { Name = "John", Age = 25 };
        var result = ruleSet.Test(person);
        
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task RangeConstraint_BelowMin_FailsValidation() {
        var constraint = new RangeConstraint(0, 150);
        var ruleSet = new RuleSet<Person>([
            new PropertyConstraintRule("Age", constraint)
        ]);
        
        var person = new Person { Name = "John", Age = -1 };
        var result = ruleSet.Test(person);
        
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task RangeConstraint_AboveMax_FailsValidation() {
        var constraint = new RangeConstraint(0, 150);
        var ruleSet = new RuleSet<Person>([
            new PropertyConstraintRule("Age", constraint)
        ]);
        
        var person = new Person { Name = "John", Age = 200 };
        var result = ruleSet.Test(person);
        
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task RangeConstraint_AtMinBoundary_PassesValidation() {
        var constraint = new RangeConstraint(0, 150);
        var ruleSet = new RuleSet<Person>([
            new PropertyConstraintRule("Age", constraint)
        ]);
        
        var person = new Person { Name = "John", Age = 0 };
        var result = ruleSet.Test(person);
        
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task RangeConstraint_AtMaxBoundary_PassesValidation() {
        var constraint = new RangeConstraint(0, 150);
        var ruleSet = new RuleSet<Person>([
            new PropertyConstraintRule("Age", constraint)
        ]);
        
        var person = new Person { Name = "John", Age = 150 };
        var result = ruleSet.Test(person);
        
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task RangeConstraint_MinOnly_PassesValidation() {
        var constraint = new RangeConstraint(0, null);
        var ruleSet = new RuleSet<Person>([
            new PropertyConstraintRule("Age", constraint)
        ]);
        
        var person = new Person { Name = "John", Age = 1000 };
        var result = ruleSet.Test(person);
        
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task RangeConstraint_MaxOnly_PassesValidation() {
        var constraint = new RangeConstraint(null, 150);
        var ruleSet = new RuleSet<Person>([
            new PropertyConstraintRule("Age", constraint)
        ]);
        
        var person = new Person { Name = "John", Age = -100 };
        var result = ruleSet.Test(person);
        
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task LengthConstraint_WithinLength_PassesValidation() {
        var constraint = new LengthConstraint(1, 100);
        var ruleSet = new RuleSet<Person>([
            new PropertyConstraintRule("Name", constraint)
        ]);
        
        var person = new Person { Name = "John", Age = 25 };
        var result = ruleSet.Test(person);
        
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task LengthConstraint_TooShort_FailsValidation() {
        var constraint = new LengthConstraint(5, 100);
        var ruleSet = new RuleSet<Person>([
            new PropertyConstraintRule("Name", constraint)
        ]);
        
        var person = new Person { Name = "Jo", Age = 25 };
        var result = ruleSet.Test(person);
        
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task LengthConstraint_TooLong_FailsValidation() {
        var constraint = new LengthConstraint(1, 5);
        var ruleSet = new RuleSet<Person>([
            new PropertyConstraintRule("Name", constraint)
        ]);
        
        var person = new Person { Name = "VeryLongName", Age = 25 };
        var result = ruleSet.Test(person);
        
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task LengthConstraint_MinLength_PassesValidation() {
        var constraint = new LengthConstraint(3, null);
        var ruleSet = new RuleSet<Person>([
            new PropertyConstraintRule("Name", constraint)
        ]);
        
        var person = new Person { Name = "VeryLongNameWithoutLimit", Age = 25 };
        var result = ruleSet.Test(person);
        
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task LengthConstraint_MaxLength_PassesValidation() {
        var constraint = new LengthConstraint(null, 100);
        var ruleSet = new RuleSet<Person>([
            new PropertyConstraintRule("Name", constraint)
        ]);
        
        var person = new Person { Name = "X", Age = 25 };
        var result = ruleSet.Test(person);
        
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task EqualityConstraint_MatchingValue_PassesValidation() {
        var constraint = new EqualityConstraint(25);
        var ruleSet = new RuleSet<Person>([
            new PropertyConstraintRule("Age", constraint)
        ]);
        
        var person = new Person { Name = "John", Age = 25 };
        var result = ruleSet.Test(person);
        
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task EqualityConstraint_NonMatchingValue_FailsValidation() {
        var constraint = new EqualityConstraint(25);
        var ruleSet = new RuleSet<Person>([
            new PropertyConstraintRule("Age", constraint)
        ]);
        
        var person = new Person { Name = "John", Age = 30 };
        var result = ruleSet.Test(person);
        
        await Assert.That(result).IsFalse();
    }

    private class Person {
        public string? Name { get; set; }
        public int Age { get; set; }
    }
}
