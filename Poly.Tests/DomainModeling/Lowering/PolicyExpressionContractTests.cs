using Poly.DomainModeling;
using Poly.DomainModeling.Lowering;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// Tests for <see cref="PolicyExpressionContract"/> and <see cref="PolicyExpressionParser"/>.
/// Proves that constrained contracts produce correct <see cref="DomainExpression"/> trees
/// and invalid contracts are rejected with clear errors.
/// </summary>
public class PolicyExpressionContractTests {
    private sealed record Person(int Age, string Name = "");

    [Test]
    public async Task Parse_PropertyComparison_GreaterThanOrEqual_EvaluatesCorrectly() {
        var contract = new PolicyExpressionContract {
            Property = "Age",
            Op = ">=",
            Value = 18
        };
        var expr = PolicyExpressionParser.Parse(contract);
        var policy = new Policy("Adult", expr);

        await Assert.That(policy.Evaluate(new Person(25))).IsTrue();
        await Assert.That(policy.Evaluate(new Person(15))).IsFalse();
        await Assert.That(policy.Evaluate(new Person(18))).IsTrue();
    }

    [Test]
    public async Task Parse_PropertyComparison_Equal_OnString_EvaluatesCorrectly() {
        var contract = new PolicyExpressionContract {
            Property = "Name",
            Op = "==",
            Value = "Alice"
        };
        var policy = new Policy("IsAlice", PolicyExpressionParser.Parse(contract));

        await Assert.That(policy.Evaluate(new Person(25, "Alice"))).IsTrue();
        await Assert.That(policy.Evaluate(new Person(30, "Bob"))).IsFalse();
    }

    [Test]
    public async Task Parse_PropertyComparison_NotEqual_OnString_EvaluatesCorrectly() {
        var contract = new PolicyExpressionContract {
            Property = "Name",
            Op = "!=",
            Value = ""
        };
        var policy = new Policy("NotEmpty", PolicyExpressionParser.Parse(contract));

        await Assert.That(policy.Evaluate(new Person(17, "Alice"))).IsTrue();
        await Assert.That(policy.Evaluate(new Person(18, ""))).IsFalse();
    }

    [Test]
    public async Task Parse_PropertyComparison_LessThan_EvaluatesCorrectly() {
        var contract = new PolicyExpressionContract {
            Property = "Age",
            Op = "<",
            Value = 18
        };
        var policy = new Policy("Minor", PolicyExpressionParser.Parse(contract));

        await Assert.That(policy.Evaluate(new Person(12))).IsTrue();
        await Assert.That(policy.Evaluate(new Person(18))).IsFalse();
    }

    [Test]
    public async Task Parse_AndComposite_EvaluatesCorrectly() {
        var contract = new PolicyExpressionContract {
            And =
            [
                new() { Property = "Age", Op = ">=", Value = 18 },
                new() { Property = "Age", Op = "<", Value = 65 },
            ]
        };
        var policy = new Policy("WorkingAge", PolicyExpressionParser.Parse(contract));

        await Assert.That(policy.Evaluate(new Person(30))).IsTrue();
        await Assert.That(policy.Evaluate(new Person(17))).IsFalse();
        await Assert.That(policy.Evaluate(new Person(65))).IsFalse();
    }

    [Test]
    public async Task Parse_OrComposite_EvaluatesCorrectly() {
        var contract = new PolicyExpressionContract {
            Or =
            [
                new() { Property = "Age", Op = "<", Value = 13 },
                new() { Property = "Age", Op = ">=", Value = 65 },
            ]
        };
        var policy = new Policy("ChildOrSenior", PolicyExpressionParser.Parse(contract));

        await Assert.That(policy.Evaluate(new Person(10))).IsTrue();
        await Assert.That(policy.Evaluate(new Person(70))).IsTrue();
        await Assert.That(policy.Evaluate(new Person(30))).IsFalse();
    }

    [Test]
    public async Task Parse_NotComposite_FlipsResult() {
        var contract = new PolicyExpressionContract {
            Not = new() { Property = "Age", Op = ">=", Value = 18 }
        };
        var policy = new Policy("NotAdult", PolicyExpressionParser.Parse(contract));

        await Assert.That(policy.Evaluate(new Person(15))).IsTrue();
        await Assert.That(policy.Evaluate(new Person(18))).IsFalse();
    }

    [Test]
    public async Task Parse_LiteralTrue_AlwaysPasses() {
        var contract = new PolicyExpressionContract {
            Literal = true
        };
        var policy = new Policy("Always", PolicyExpressionParser.Parse(contract));

        await Assert.That(policy.Evaluate(new Person(0))).IsTrue();
        await Assert.That(policy.Evaluate(new Person(99))).IsTrue();
    }

    // ── Invalid contracts ─────────────────────────────────────

    [Test]
    public async Task Parse_EmptyContract_Throws() {
        var contract = new PolicyExpressionContract();

        await Assert.That(() => PolicyExpressionParser.Parse(contract))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Parse_MultipleBranches_Throws() {
        var contract = new PolicyExpressionContract {
            Property = "Age",
            Op = ">=",
            Value = 18,
            Literal = true
        };

        await Assert.That(() => PolicyExpressionParser.Parse(contract))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Parse_UnknownOperator_Throws() {
        var contract = new PolicyExpressionContract {
            Property = "Age",
            Op = "??",
            Value = 42
        };

        await Assert.That(() => PolicyExpressionParser.Parse(contract))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Parse_AndWithSingleOperand_Throws() {
        var contract = new PolicyExpressionContract {
            And = [new() { Property = "Age", Op = ">=", Value = 18 }]
        };

        await Assert.That(() => PolicyExpressionParser.Parse(contract))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Parse_EmptyPropertyName_Throws() {
        var contract = new PolicyExpressionContract {
            Property = "",
            Op = ">=",
            Value = 18
        };

        await Assert.That(() => PolicyExpressionParser.Parse(contract))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Parse_MissingOperator_Throws() {
        var contract = new PolicyExpressionContract {
            Property = "Age",
            Value = 18
        };

        await Assert.That(() => PolicyExpressionParser.Parse(contract))
            .Throws<ArgumentException>();
    }
}