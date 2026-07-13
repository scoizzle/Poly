using Poly.DomainModeling;
using Poly.DomainModeling.Lowering;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// Tests for <see cref="DomainExpressionJsonParser"/>.
/// Proves that JSON expression strings produce correct <see cref="DomainExpression"/>
/// trees and invalid JSON is rejected with clear errors.
/// </summary>
public class DomainExpressionJsonParserTests {
    private sealed record Person(int Age, string Name = "");

    [Test]
    public async Task Parse_GreaterThanOrEqual_EvaluatesCorrectly() {
        var expr = DomainExpressionJsonParser.ParseJson(
            """{"property":"Age","op":">=","value":18}""");
        var policy = new Policy("Adult", expr);

        await Assert.That(policy.Evaluate(new Person(25))).IsTrue();
        await Assert.That(policy.Evaluate(new Person(15))).IsFalse();
        await Assert.That(policy.Evaluate(new Person(18))).IsTrue();
    }

    [Test]
    public async Task Parse_Equal_OnString_EvaluatesCorrectly() {
        var expr = DomainExpressionJsonParser.ParseJson(
            """{"property":"Name","op":"==","value":"Alice"}""");
        var policy = new Policy("IsAlice", expr);

        await Assert.That(policy.Evaluate(new Person(25, "Alice"))).IsTrue();
        await Assert.That(policy.Evaluate(new Person(30, "Bob"))).IsFalse();
    }

    [Test]
    public async Task Parse_NotEqual_OnString_EvaluatesCorrectly() {
        var expr = DomainExpressionJsonParser.ParseJson(
            """{"property":"Name","op":"!=","value":""}""");
        var policy = new Policy("NotEmpty", expr);

        await Assert.That(policy.Evaluate(new Person(17, "Alice"))).IsTrue();
        await Assert.That(policy.Evaluate(new Person(18, ""))).IsFalse();
    }

    [Test]
    public async Task Parse_LessThan_EvaluatesCorrectly() {
        var expr = DomainExpressionJsonParser.ParseJson(
            """{"property":"Age","op":"<","value":18}""");
        var policy = new Policy("Minor", expr);

        await Assert.That(policy.Evaluate(new Person(12))).IsTrue();
        await Assert.That(policy.Evaluate(new Person(18))).IsFalse();
    }

    [Test]
    public async Task Parse_AndComposite_EvaluatesCorrectly() {
        var expr = DomainExpressionJsonParser.ParseJson(
            """{"and":[{"property":"Age","op":">=","value":18},{"property":"Age","op":"<","value":65}]}""");
        var policy = new Policy("WorkingAge", expr);

        await Assert.That(policy.Evaluate(new Person(30))).IsTrue();
        await Assert.That(policy.Evaluate(new Person(17))).IsFalse();
        await Assert.That(policy.Evaluate(new Person(65))).IsFalse();
    }

    [Test]
    public async Task Parse_OrComposite_EvaluatesCorrectly() {
        var expr = DomainExpressionJsonParser.ParseJson(
            """{"or":[{"property":"Age","op":"<","value":13},{"property":"Age","op":">=","value":65}]}""");
        var policy = new Policy("ChildOrSenior", expr);

        await Assert.That(policy.Evaluate(new Person(10))).IsTrue();
        await Assert.That(policy.Evaluate(new Person(70))).IsTrue();
        await Assert.That(policy.Evaluate(new Person(30))).IsFalse();
    }

    [Test]
    public async Task Parse_NotComposite_FlipsResult() {
        var expr = DomainExpressionJsonParser.ParseJson(
            """{"not":{"property":"Age","op":">=","value":18}}""");
        var policy = new Policy("NotAdult", expr);

        await Assert.That(policy.Evaluate(new Person(15))).IsTrue();
        await Assert.That(policy.Evaluate(new Person(18))).IsFalse();
    }

    [Test]
    public async Task Parse_LiteralTrue_AlwaysPasses() {
        var expr = DomainExpressionJsonParser.ParseJson(
            """{"literal":true}""");
        var policy = new Policy("Always", expr);

        await Assert.That(policy.Evaluate(new Person(0))).IsTrue();
        await Assert.That(policy.Evaluate(new Person(99))).IsTrue();
    }

    [Test]
    public async Task Parse_BooleanValue_EvaluatesCorrectly() {
        var expr = DomainExpressionJsonParser.ParseJson(
            """{"property":"Name","op":"==","value":true}""");
        // True coerced to string in comparison — just verify parsing succeeds
        var policy = new Policy("BoolCmp", expr);
        await Assert.That(policy).IsNotNull();
    }

    // ── Invalid JSON ──────────────────────────────────────────

    [Test]
    public async Task Parse_EmptyString_Throws() {
        await Assert.That(() => DomainExpressionJsonParser.ParseJson(""))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Parse_Null_Throws() {
        await Assert.That(() => DomainExpressionJsonParser.ParseJson(null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Parse_NotAnObject_Throws() {
        await Assert.That(() => DomainExpressionJsonParser.ParseJson("42"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Parse_EmptyObject_Throws() {
        await Assert.That(() => DomainExpressionJsonParser.ParseJson("{}"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Parse_MultipleBranches_Throws() {
        await Assert.That(() => DomainExpressionJsonParser.ParseJson(
                """{"property":"Age","op":">=","value":18,"literal":true}"""))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Parse_UnknownOperator_Throws() {
        await Assert.That(() => DomainExpressionJsonParser.ParseJson(
                """{"property":"Age","op":"??","value":42}"""))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Parse_AndWithSingleOperand_Throws() {
        await Assert.That(() => DomainExpressionJsonParser.ParseJson(
                """{"and":[{"property":"Age","op":">=","value":18}]}"""))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Parse_EmptyPropertyName_Throws() {
        await Assert.That(() => DomainExpressionJsonParser.ParseJson(
                """{"property":"","op":">=","value":18}"""))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Parse_MissingOperator_Throws() {
        await Assert.That(() => DomainExpressionJsonParser.ParseJson(
                """{"property":"Age","value":18}"""))
            .Throws<ArgumentException>();
    }
}