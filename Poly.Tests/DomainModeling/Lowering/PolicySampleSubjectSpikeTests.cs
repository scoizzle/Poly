using Poly.DomainModeling;
using Poly.DomainModeling.Lowering;
using Poly.Interpretation;
using Poly.Interpretation.Vm;

using SN = Poly.Ast.Nodes;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// Spike tests: which CLR types work as the subject for
/// <c>DomainExpression.Property("Age")</c> → <c>Member(subject, "Age")</c>
/// on the VM and LINQ paths?
///
/// The goal is to find a mechanism for the MCP <c>evaluate_policy</c> tool
/// to build a subject from a JSON property bag without inventing domain opcodes
/// or changing the lowering pass.
/// </summary>
public class PolicySampleSubjectSpikeTests {
    private static readonly DomainExpressionLoweringPass Pass = new();

    private sealed record PersonRecord(string Name, int Age);

    // ── Approach 1: Anonymous type (baseline — known working) ─────

    [Test]
    public async Task AnonymousType_PropertyAccess_Works() {
        var subject = new Constant(new { Name = "Alice", Age = 25 });

        var node = Pass.Lower(
            DomainExpression.GreaterThanOrEqual(
                DomainExpression.Property("Age"), DomainExpression.Literal(18)),
            subject);

        var result = ExecuteOnVm(node);
        await Assert.That((long)result.Value!).IsEqualTo(1L);

        var minor = new Constant(new { Name = "Bob", Age = 15 });
        var minorNode = Pass.Lower(
            DomainExpression.GreaterThanOrEqual(
                DomainExpression.Property("Age"), DomainExpression.Literal(18)),
            minor);
        await Assert.That((long)ExecuteOnVm(minorNode).Value!).IsEqualTo(0L);
    }

    // ── Approach 2: Dictionary<string, object> ────────────────────
    // Result: compiles on VM but returns wrong values — Member node
    // doesn't resolve to dictionary keys. Correct Property("Age") on a bag
    // with Age=99999 should return 99999. Assert it does NOT (fail closed).

    [Test]
    public async Task DictionaryStringObject_GivesWrongResults() {
        var dict = new Dictionary<string, object> { ["Age"] = 99999 };
        var subject = new Constant(dict);

        var node = Pass.Lower(DomainExpression.Property("Age"), subject);

        var result = ExecuteOnVm(node);
        // Fail closed: assert the bag value is NOT returned, even accounting
        // for int→long or other numeric ABI conversions the VM may apply.
        await Assert.That(MatchNumeric(result.Value, 99999)).IsFalse();

        // Also: Age >= 18 with Age=99999 must NOT evaluate as adult-true.
        var guardNode = Pass.Lower(
            DomainExpression.GreaterThanOrEqual(
                DomainExpression.Property("Age"), DomainExpression.Literal(18)),
            subject);
        var guardResult = ExecuteOnVm(guardNode);
        bool isAdult = guardResult.Value is long l && l == 1L;
        await Assert.That(isAdult).IsFalse();
    }

    // ── Approach 3: ExpandoObject ─────────────────────────────────
    // Result: same as Dictionary — compiles but wrong results.

    [Test]
    public async Task ExpandoObject_GivesWrongResults() {
        dynamic expando = new System.Dynamic.ExpandoObject();
        expando.Age = 99999;
        var subject = new Constant((object)expando);

        var node = Pass.Lower(DomainExpression.Property("Age"), subject);

        // ExpandoObject's Age is a dynamic property — CLR reflection won't find it.
        var result = ExecuteOnVm(node);
        // Fail closed: assert the bag value is NOT returned (int/long aware).
        await Assert.That(MatchNumeric(result.Value, 99999)).IsFalse();

        // Also: Age >= 18 with Age=99999 must NOT evaluate as adult-true.
        var guardNode = Pass.Lower(
            DomainExpression.GreaterThanOrEqual(
                DomainExpression.Property("Age"), DomainExpression.Literal(18)),
            subject);
        var guardResult = ExecuteOnVm(guardNode);
        bool isAdult = guardResult.Value is long l && l == 1L;
        await Assert.That(isAdult).IsFalse();
    }

    // ── Approach 4: Custom sealed record (baseline — known working) ─

    [Test]
    public async Task CustomRecord_PropertyAccess_Works() {
        var person = new PersonRecord("Alice", 25);
        var subject = new Constant(person);

        var node = Pass.Lower(
            DomainExpression.GreaterThanOrEqual(
                DomainExpression.Property("Age"), DomainExpression.Literal(18)),
            subject);

        var result = ExecuteOnVm(node);
        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }

    // ── Approach 5: PropBag — typed record with nullable properties ─
    // Tests a simple reusable container with known CLR property names.

    private sealed record PropBag(string? Name, int? Age, string? Status, decimal? Total);

    [Test]
    public async Task PropBag_WithNonNullValue_Works() {
        var bag = new PropBag(Age: 25, Status: "Active", Total: 100, Name: null);
        var subject = new Constant(bag);

        var node = Pass.Lower(
            DomainExpression.GreaterThanOrEqual(
                DomainExpression.Property("Age"), DomainExpression.Literal(18)),
            subject);

        var result = ExecuteOnVm(node);
        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }

    [Test]
    public async Task PropBag_WithNullValue_FailsOnVm() {
        var bag = new PropBag(Age: null, Total: 100, Status: null, Name: "Test");
        var subject = new Constant(bag);

        // Accessing a nullable int? property with null value on VM throws
        // because the VM tries to unbox the nullable struct and fails.
        var node = Pass.Lower(DomainExpression.Property("Age"), subject);

        await Assert.That(() => ExecuteOnVm(node)).Throws<Exception>();
    }

    // ── Approach 6: PropBag with non-nullable value types ──────────
    // Avoids nullable issues by using non-nullable int (default 0 for absent).

    private sealed record StrictBag(int Age, string Name, string Status, decimal Total);

    [Test]
    public async Task StrictBag_PropertyAccess_Works() {
        var bag = new StrictBag(Age: 25, Name: "Alice", Status: "", Total: 0);
        var subject = new Constant(bag);

        var node = Pass.Lower(
            DomainExpression.GreaterThanOrEqual(
                DomainExpression.Property("Age"), DomainExpression.Literal(18)),
            subject);

        var result = ExecuteOnVm(node);
        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }

    // ── Approach 7: Via PolicyEvaluator with PropBag ──────────────

    [Test]
    public async Task PolicyEvaluator_WithPropBag_Works() {
        var policy = new Policy("Adult",
            DomainExpression.GreaterThanOrEqual(
                DomainExpression.Property("Age"), DomainExpression.Literal(18)));

        var adult = policy.CompileVMPredicate<PropBag>()(new PropBag(Age: 25, Name: "A", Status: "", Total: 0));
        var minor = policy.CompileVMPredicate<PropBag>()(new PropBag(Age: 15, Name: "B", Status: "", Total: 0));

        await Assert.That(adult).IsTrue();
        await Assert.That(minor).IsFalse();
    }

    // ── Helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if <paramref name="v"/> numerically equals <paramref name="expected"/>,
    /// accounting for common ABI conversions (int→long, short→long, etc.).
    /// </summary>
    private static bool MatchNumeric(object? v, long expected) {
        if (v is null) return false;
        var actual = v switch {
            long l => l,
            int i => i,
            short s => s,
            byte b => b,
            uint ui => ui,
            ushort us => us,
            _ => long.MinValue // sentinel — won't match expected
        };
        return actual == expected;
    }

    private static InterpreterResult ExecuteOnVm(Node node) {
        using var exec = Interpreter.Execute(Interpreter.Compile(node, CompilationMode.Normal));
        return exec.Result;
    }
}