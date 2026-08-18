using System.Linq;

using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.ContractFill;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Libraries.Temporal;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.LinqExpressions;
using Poly.Interpretation.Vm;

using SN = Poly.Ast.Nodes;

namespace Poly.Tests.DomainModeling.Lowering;

public record PersonRecord(string Name, int Age);

/// <summary>
/// End-to-end tests: DomainExpression → lowering pass → Syntax AST
/// → VM analysis pipeline → µops → Interpreter.Execute.
/// </summary>
public class DomainExpressionVmExecutionTests {
    // Instance fields: TUnit creates a fresh class instance per test, and
    // DomainExpressionLoweringPass carries mutable _currentSubject state — a shared
    // static instance raced under parallel execution (Exists_NonNullValue_ReturnsTrue flake).
    private readonly DomainExpressionLoweringPass Pass = new(
        new LoweringContext(new SN.Parameter("entity"), Meaning: ExtensionCatalog.Core.Language.Meaning));
    private readonly ParameterReference Subject = new();

    private static AnalysisResult Analyze(Node node) =>
        Interpreter.Analyzer.Analyze(node);

    private static InterpreterResult Execute(Node node) {
        using var exec = Interpreter.Execute(Interpreter.Compile(node, CompilationMode.Normal));
        return exec.Result;
    }

    private static (InterpreterResult Result, VmState State) ExecuteWithState(Node node) {
        var program = Interpreter.Compile(node, CompilationMode.Normal);
        var exec = Interpreter.Execute(program, s => s.MaxLoopIterations = 100_000_000);
        return (exec.Result, exec.State);
    }

    private InterpreterResult ExecuteDomain(DomainExpression expr) {
        var node = Pass.Lower(expr, Subject);
        return Execute(node);
    }

    private InterpreterResult ExecuteDomain(DomainExpression expr, Node subject) {
        var node = Pass.Lower(expr, subject);
        return Execute(node);
    }

    [Test]
    public async Task Literal_42_Returns42() {
        var result = ExecuteDomain(DomainExpression.Literal(42));

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That((long)result.Value!).IsEqualTo(42L);
    }

    [Test]
    public async Task Literal_Negative_ReturnsNegative() {
        var result = ExecuteDomain(DomainExpression.Literal(-7));

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That((long)result.Value!).IsEqualTo(-7L);
    }

    [Test]
    public async Task Add_1Plus2_Returns3() {
        var result = ExecuteDomain(
            DomainExpression.Add(DomainExpression.Literal(1), DomainExpression.Literal(2)));

        await Assert.That((long)result.Value!).IsEqualTo(3L);
    }

    [Test]
    public async Task Subtract_10Minus3_Returns7() {
        var result = ExecuteDomain(
            DomainExpression.Subtract(DomainExpression.Literal(10), DomainExpression.Literal(3)));

        await Assert.That((long)result.Value!).IsEqualTo(7L);
    }

    [Test]
    public async Task Multiply_4Times5_Returns20() {
        var result = ExecuteDomain(
            DomainExpression.Multiply(DomainExpression.Literal(4), DomainExpression.Literal(5)));

        await Assert.That((long)result.Value!).IsEqualTo(20L);
    }

    [Test]
    public async Task Divide_10DividedBy2_Returns5() {
        var result = ExecuteDomain(
            DomainExpression.Divide(DomainExpression.Literal(10), DomainExpression.Literal(2)));

        await Assert.That((long)result.Value!).IsEqualTo(5L);
    }

    [Test]
    public async Task CompoundArithmetic_MultiplyBeforeAdd() {
        var result = ExecuteDomain(
            DomainExpression.Add(
                DomainExpression.Multiply(DomainExpression.Literal(2), DomainExpression.Literal(3)),
                DomainExpression.Literal(1)));

        await Assert.That((long)result.Value!).IsEqualTo(7L);
    }

    [Test]
    public async Task Equal_True() {
        var result = ExecuteDomain(
            DomainExpression.Equal(DomainExpression.Literal(5), DomainExpression.Literal(5)));

        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }

    [Test]
    public async Task Equal_False() {
        var result = ExecuteDomain(
            DomainExpression.Equal(DomainExpression.Literal(5), DomainExpression.Literal(3)));

        await Assert.That((long)result.Value!).IsEqualTo(0L);
    }

    [Test]
    public async Task NotEqual_True() {
        var result = ExecuteDomain(
            DomainExpression.NotEqual(DomainExpression.Literal(5), DomainExpression.Literal(3)));

        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }

    [Test]
    public async Task LessThan_True() {
        var result = ExecuteDomain(
            DomainExpression.LessThan(DomainExpression.Literal(3), DomainExpression.Literal(5)));

        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }

    [Test]
    public async Task LessThan_False() {
        var result = ExecuteDomain(
            DomainExpression.LessThan(DomainExpression.Literal(5), DomainExpression.Literal(3)));

        await Assert.That((long)result.Value!).IsEqualTo(0L);
    }

    [Test]
    public async Task GreaterThan_True() {
        var result = ExecuteDomain(
            DomainExpression.GreaterThan(DomainExpression.Literal(5), DomainExpression.Literal(3)));

        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }

    [Test]
    public async Task GreaterThanOrEqual_Equal_True() {
        var result = ExecuteDomain(
            DomainExpression.GreaterThanOrEqual(DomainExpression.Literal(5), DomainExpression.Literal(5)));

        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }

    [Test]
    public async Task GreaterThanOrEqual_Greater_True() {
        var result = ExecuteDomain(
            DomainExpression.GreaterThanOrEqual(DomainExpression.Literal(7), DomainExpression.Literal(5)));

        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }

    [Test]
    public async Task LessThanOrEqual_Equal_True() {
        var result = ExecuteDomain(
            DomainExpression.LessThanOrEqual(DomainExpression.Literal(3), DomainExpression.Literal(3)));

        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }

    [Test]
    public async Task LessThanOrEqual_Less_True() {
        var result = ExecuteDomain(
            DomainExpression.LessThanOrEqual(DomainExpression.Literal(3), DomainExpression.Literal(5)));

        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }

    [Test]
    public async Task And_BothTrue_ReturnsTrue() {
        var result = ExecuteDomain(
            DomainExpression.And(
                DomainExpression.Equal(DomainExpression.Literal(1), DomainExpression.Literal(1)),
                DomainExpression.Equal(DomainExpression.Literal(2), DomainExpression.Literal(2))));

        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }

    [Test]
    public async Task And_OneFalse_ReturnsFalse() {
        var result = ExecuteDomain(
            DomainExpression.And(
                DomainExpression.Equal(DomainExpression.Literal(1), DomainExpression.Literal(1)),
                DomainExpression.Equal(DomainExpression.Literal(2), DomainExpression.Literal(3))));

        await Assert.That((long)result.Value!).IsEqualTo(0L);
    }

    [Test]
    public async Task Or_OneTrue_ReturnsTrue() {
        var result = ExecuteDomain(
            DomainExpression.Or(
                DomainExpression.Equal(DomainExpression.Literal(1), DomainExpression.Literal(2)),
                DomainExpression.Equal(DomainExpression.Literal(2), DomainExpression.Literal(2))));

        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }

    [Test]
    public async Task Not_True_ReturnsFalse() {
        var result = ExecuteDomain(
            DomainExpression.Not(DomainExpression.Equal(DomainExpression.Literal(1), DomainExpression.Literal(1))));

        await Assert.That((long)result.Value!).IsEqualTo(0L);
    }

    [Test]
    public async Task Not_False_ReturnsTrue() {
        var result = ExecuteDomain(
            DomainExpression.Not(DomainExpression.Equal(DomainExpression.Literal(1), DomainExpression.Literal(2))));

        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }

    // ── PropertyAccess via LINQ path ──────────────────────────────────────
    // The lowering pass correctly maps PropertyAccess → Member(subject, name).
    // The LINQ expression generator compiles Member for any CLR type natively.
    // The VM µop path for heap-allocated objects is a gap (see next section).

    private static InterpreterResult ExecuteViaLinq(Node node) {
        var analysis = new AnalyzerBuilder()
            .UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseControlFlowAnalysis()
            .UseConstantFolding()
            .UseDefiniteAssignmentAnalysis()
            .Build()
            .Analyze(node);
        var generator = new LinqExpressionGenerator(analysis);
        var compiled = generator.Compile(node);
        var lambda = System.Linq.Expressions.Expression
            .Lambda<Func<object?>>(System.Linq.Expressions.Expression.Convert(compiled.Expression, typeof(object)))
            .Compile();
        var value = lambda();
        return InterpreterResult.FromValue(value);
    }

    private InterpreterResult ExecuteDomainViaLinq(DomainExpression expr, Node subject) {
        var node = Pass.Lower(expr, subject);
        return ExecuteViaLinq(node);
    }

    [Test]
    public async Task PropertyAccess_OnClrRecord_ReturnsPropertyValue() {
        var person = new PersonRecord("Alice", 30);
        var subject = new Constant(person);

        var node = Pass.Lower(DomainExpression.Property("Name"), subject);
        var result = ExecuteDomain(DomainExpression.Property("Name"), subject);

        // Name returns a string — InterpretResult dereferences heap refs automatically
        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo("Alice");
    }

    [Test]
    public async Task PropertyAccess_OnClrRecord_ReturnsNumericProperty() {
        var person = new PersonRecord("Bob", 42);
        var subject = new Constant(person);

        var result = ExecuteDomain(DomainExpression.Property("Age"), subject);

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo((long)42);
    }

    [Test]
    public async Task PropertyAccess_WithArithmetic_ComputesCorrectly() {
        var person = new PersonRecord("Charlie", 25);
        var subject = new Constant(person);

        var result = ExecuteDomain(
            DomainExpression.Add(DomainExpression.Property("Age"), DomainExpression.Literal(10)),
            subject);

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo((long)35);
    }

    [Test]
    public async Task PropertyAccess_WithComparison_ComputesCorrectly() {
        var person = new PersonRecord("Diana", 17);
        var subject = new Constant(person);

        var result = ExecuteDomain(
            DomainExpression.GreaterThan(DomainExpression.Property("Age"), DomainExpression.Literal(18)),
            subject);

        await Assert.That((long)result.Value!).IsEqualTo(0L);
    }

    [Test]
    public async Task DeeplyNestedExpression() {
        // ((2 + 3) * 4) > 15  →  (5 * 4) > 15  →  20 > 15  →  true (1)
        var result = ExecuteDomain(
            DomainExpression.GreaterThan(
                DomainExpression.Multiply(
                    DomainExpression.Add(DomainExpression.Literal(2), DomainExpression.Literal(3)),
                    DomainExpression.Literal(4)),
                DomainExpression.Literal(15)));

        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }

    // ── Policy evaluation ───────────────────────────────────────────

    [Test]
    public async Task Policy_AdultAge_AcceptsOver18() {
        var policy = new Policy("AdultAge",
            DomainExpression.GreaterThan(
                DomainExpression.Property(nameof(PersonRecord.Age)),
                DomainExpression.Literal(18)));

        var adult = policy.Evaluate(new PersonRecord("Alice", 25));
        var minor = policy.Evaluate(new PersonRecord("Bob", 15));

        await Assert.That(adult).IsTrue();
        await Assert.That(minor).IsFalse();
    }

    [Test]
    public async Task Policy_CompiledPredicate_FiltersCollection() {
        var policy = new Policy("VotingAge",
            DomainExpression.GreaterThanOrEqual(
                DomainExpression.Property(nameof(PersonRecord.Age)),
                DomainExpression.Literal(18)));

        var predicate = policy.CompileLinqPredicate<PersonRecord>();

        var people = new[] {
            new PersonRecord("Alice", 25),
            new PersonRecord("Bob", 15),
            new PersonRecord("Charlie", 18),
        };

        var eligible = people.Where(predicate).ToList();

        await Assert.That(eligible.Count).IsEqualTo(2);
        await Assert.That(eligible[0].Name).IsEqualTo("Alice");
        await Assert.That(eligible[1].Name).IsEqualTo("Charlie");
    }

    [Test]
    public async Task Policy_CompositeGuard_EvaluatesCorrectly() {
        var policy = new Policy("JuniorAdult",
            DomainExpression.And(
                DomainExpression.GreaterThanOrEqual(
                    DomainExpression.Property(nameof(PersonRecord.Age)),
                    DomainExpression.Literal(18)),
                DomainExpression.LessThan(
                    DomainExpression.Property(nameof(PersonRecord.Age)),
                    DomainExpression.Literal(21))));

        await Assert.That(policy.Evaluate(new PersonRecord("A", 17))).IsFalse();
        await Assert.That(policy.Evaluate(new PersonRecord("B", 18))).IsTrue();
        await Assert.That(policy.Evaluate(new PersonRecord("C", 20))).IsTrue();
        await Assert.That(policy.Evaluate(new PersonRecord("D", 21))).IsFalse();
    }

    [Test]
    public async Task Policy_NameBasedGuard_EvaluatesCorrectly() {
        var policy = new Policy("NameStartsWithA",
            DomainExpression.Equal(
                DomainExpression.Property(nameof(PersonRecord.Name)),
                DomainExpression.Literal("Alice")));

        var alice = policy.Evaluate(new PersonRecord("Alice", 30));
        var bob = policy.Evaluate(new PersonRecord("Bob", 25));

        await Assert.That(alice).IsTrue();
        await Assert.That(bob).IsFalse();
    }

    // ── LoadHeapConst ───────────────────────────────────────────

    [Test]
    public async Task LoadHeapConst_StringLiteral_AllocatesOnHeapAndReturnsHandle() {
        var (result, state) = ExecuteWithState(Pass.Lower(
            DomainExpression.Literal("hello"), Subject));

        await Assert.That(result.HasValue).IsTrue();

        // Interpreter.Execute auto-dereferences heap handles.  If the result was
        // already dereferenced, compare the string directly.
        object? obj;
        if (result.Value is long handle)
            obj = state.Heap.Get((int)handle);
        else
            obj = result.Value;

        await Assert.That(obj).IsNotNull();
        await Assert.That(obj).IsTypeOf<string>();
        await Assert.That((string)obj!).IsEqualTo("hello");
    }

    // ── Additional policy edge cases ────────────────────────────

    [Test]
    public async Task Policy_NotOfComparison_FlipsResult() {
        var policy = new Policy("NotAdult",
            DomainExpression.Not(
                DomainExpression.GreaterThanOrEqual(
                    DomainExpression.Property(nameof(PersonRecord.Age)),
                    DomainExpression.Literal(18))));

        await Assert.That(policy.Evaluate(new PersonRecord("Minor", 15))).IsTrue();
        await Assert.That(policy.Evaluate(new PersonRecord("Adult", 18))).IsFalse();
        await Assert.That(policy.Evaluate(new PersonRecord("Adult", 25))).IsFalse();
    }

    [Test]
    public async Task Policy_OrComposite_AcceptsEitherCondition() {
        // Can enter if age >= 65 OR name == "VIP"
        var policy = new Policy("VIPOrSenior",
            DomainExpression.Or(
                DomainExpression.GreaterThanOrEqual(
                    DomainExpression.Property(nameof(PersonRecord.Age)),
                    DomainExpression.Literal(65)),
                DomainExpression.Equal(
                    DomainExpression.Property(nameof(PersonRecord.Name)),
                    DomainExpression.Literal("VIP"))));

        await Assert.That(policy.Evaluate(new PersonRecord("Senior", 70))).IsTrue();
        await Assert.That(policy.Evaluate(new PersonRecord("VIP", 30))).IsTrue();
        await Assert.That(policy.Evaluate(new PersonRecord("Regular", 30))).IsFalse();
    }

    [Test]
    public async Task Policy_LiteralTrue_AlwaysPasses() {
        var policy = new Policy("Always",
            DomainExpression.Equal(DomainExpression.Literal(1), DomainExpression.Literal(1)));

        await Assert.That(policy.Evaluate(new PersonRecord("X", 0))).IsTrue();
        await Assert.That(policy.Evaluate(new PersonRecord("Y", 99))).IsTrue();
    }

    [Test]
    public async Task Policy_MultipleAndConditions_AllMustBeTrue() {
        // Must be adult AND name must start with 'A' (checking Age >= 18 AND Name == "Alice")
        var policy = new Policy("AdultAlice",
            DomainExpression.And(
                DomainExpression.GreaterThanOrEqual(
                    DomainExpression.Property(nameof(PersonRecord.Age)),
                    DomainExpression.Literal(18)),
                DomainExpression.Equal(
                    DomainExpression.Property(nameof(PersonRecord.Name)),
                    DomainExpression.Literal("Alice"))));

        await Assert.That(policy.Evaluate(new PersonRecord("Alice", 25))).IsTrue();
        await Assert.That(policy.Evaluate(new PersonRecord("Bob", 25))).IsFalse();
        await Assert.That(policy.Evaluate(new PersonRecord("Alice", 15))).IsFalse();
    }

    [Test]
    public async Task Policy_NegatedComposite_UsingNotAnd() {
        // Cannot enter if under 18 AND not VIP
        var policy = new Policy("NoMinorNonVip",
            DomainExpression.Not(
                DomainExpression.And(
                    DomainExpression.LessThan(
                        DomainExpression.Property(nameof(PersonRecord.Age)),
                        DomainExpression.Literal(18)),
                    DomainExpression.Not(
                        DomainExpression.Equal(
                            DomainExpression.Property(nameof(PersonRecord.Name)),
                            DomainExpression.Literal("VIP"))))));

        // Age 15, not VIP → under 18 AND not VIP → true → NOT → false
        await Assert.That(policy.Evaluate(new PersonRecord("Regular", 15))).IsFalse();
        // Age 15, VIP → under 18 AND (not VIP → false) → false → NOT → true
        await Assert.That(policy.Evaluate(new PersonRecord("VIP", 15))).IsTrue();
        // Age 25 → under 18 is false → AND short-circuits to false → NOT → true
        await Assert.That(policy.Evaluate(new PersonRecord("Adult", 25))).IsTrue();
    }

    /// <summary>
    /// Cross-check: VM and LINQ paths agree on all test cases above.
    /// This test verifies the invariant that PolicyEvaluator uses internally.
    /// </summary>
    [Test]
    public async Task Policy_LinqAndVmPaths_Agree() {
        var policies = new[] {
            new Policy("P1", DomainExpression.GreaterThan(DomainExpression.Property("Age"), DomainExpression.Literal(18))),
            new Policy("P2", DomainExpression.And(
                DomainExpression.GreaterThan(DomainExpression.Property("Age"), DomainExpression.Literal(10)),
                DomainExpression.LessThan(DomainExpression.Property("Age"), DomainExpression.Literal(20)))),
            new Policy("P3", DomainExpression.Or(
                DomainExpression.Equal(DomainExpression.Property("Name"), DomainExpression.Literal("Alice")),
                DomainExpression.Equal(DomainExpression.Property("Name"), DomainExpression.Literal("Bob")))),
        };

        var entities = new[] {
            new PersonRecord("Alice", 25),
            new PersonRecord("Bob", 15),
            new PersonRecord("Charlie", 30),
        };

        foreach (var policy in policies) {
            foreach (var entity in entities) {
                var result = policy.EvaluateWithDualOracle(entity);
                await Assert.That(result).IsAssignableTo<bool>();
            }
        }
    }

    // ── DE lower-smoke matrix: gap coverage ─────────────────────
    //
    // Inventory of all DomainExpression concrete subtypes and their
    // test coverage status (see ws8-README.md task #2).

    // ParameterAccess: standalone test (used via Policies above, but test explicitly)
    [Test]
    public async Task ParameterAccess_WithExplicitSubject_ResolvesParameter() {
        var param = new Parameter("x");
        var node = Pass.Lower(DomainExpression.Parameter("x"), new Constant(null));
        // ParameterAccess with no matching subject — resolves to fresh Parameter node
        // This tests lowering doesn't throw
        await Assert.That(node).IsNotNull();
    }

    // OwnedAccess: lowering produces Member chain; VM execution requires CLR object graph
    [Test]
    public async Task OwnedAccess_LowersWithoutThrowing() {
        var expr = DomainExpression.Owned("Address", DomainExpression.Property("City"));
        var subject = new Constant(new { Address = new { City = "Seattle" } });
        var node = Pass.Lower(expr, subject);

        await Assert.That(node).IsNotNull();
        // Full VM execution of Owned/Member chains requires heap objects — documented gap:
        // The VM µop path for nested CLR object member access works for simple cases
        // but OwnedAccess produces chained Member(Member(subject, "Address"), "City")
        // which requires heap dereference support. See DomainExpression docs.
    }

    // DateOperation: lowers to Invoke(Member(date, "AddDays"/"AddMonths"/"Subtract"), offset)
    [Test]
    public async Task DateOperation_LowersWithoutThrowing() {
        var expr = new DateOperation(
            DomainExpression.Property("BirthDate"),
            DomainExpression.Literal(1),
            DateOperationKind.AddDays);
        var subject = new Constant(new { BirthDate = DateTime.Now });
        var node = Pass.Lower(expr, subject);

        await Assert.That(node).IsNotNull();
        // VM execution of DateOperation requires heap object + Invoke support.
        // The lower-to-Invoke pattern is correct for LINQ path (tested indirectly);
        // VM path is a documented gap — DateOperation uses Invoke which the VM
        // supports for known methods but DateTime.AddDays is a value-type method call.
    }

    // RelationshipNavigation: lowers to Member(Member(subject, relationshipName), targetProperty)
    [Test]
    public async Task RelationshipNavigation_LowersWithoutThrowing() {
        var expr = DomainExpression.RelationshipNav("Owner", DomainExpression.Property("Name"));
        var subject = new Constant(new { Owner = new { Name = "Alice" } });
        var node = Pass.Lower(expr, subject);

        await Assert.That(node).IsNotNull();
        // Full VM execution requires heap object graph navigation — same gap as OwnedAccess.
        // The lowering is structurally correct (nested Member chains).
    }

    // Exists/NotExists: lower to null comparisons — already tested via Policy age guards
    // But add explicit tests for documentation.
    [Test]
    public async Task Exists_NonNullValue_ReturnsTrue() {
        // Exists(Property("X")) with subject { X = "hello" } → NotEqual(Member(subject, "X"), null)
        var subject = new Constant(new { X = "hello" });
        var node = Pass.Lower(DomainExpression.Exists(DomainExpression.Property("X")), subject);

        var result = Execute(node);
        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }

    [Test]
    public async Task NotExists_NullValue_ReturnsTrue() {
        // NotExists(Property("X")) with subject { X = null } → Equal(Member(subject, "X"), null)
        var subject = new Constant(new { X = default(string) });
        var node = Pass.Lower(DomainExpression.NotExists(DomainExpression.Property("X")), subject);

        var result = Execute(node);
        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }
}