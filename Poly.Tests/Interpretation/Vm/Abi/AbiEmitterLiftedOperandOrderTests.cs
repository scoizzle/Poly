using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;

namespace Poly.Tests.Interpretation.Vm.Abi;

/// <summary>
/// A lifted operand is emitted exactly once, left then right (#330 review). The value form
/// of a node is its generating code, so a shape that reads an operand's value in several
/// places re-runs a call and reorders a mixed numeric comparison's frame slots. A counting
/// method makes both defects visible where a parameter or constant hides them.
/// </summary>
public class AbiEmitterLiftedOperandOrderTests {
    private static ITypeDefinitionProvider Analyze(TypeDefinitionNode typeNode) {
        var analyzer = new TypeDefinitionNodeAnalyzer();
        var context = AnalysisContext.CreateDefault();
        analyzer.Analyze(context, typeNode);
        return context.TypeDefinitions;
    }

    /// <summary>A type whose counter field makes each call observable: <c>Next</c> answers
    /// the call's ordinal as <c>long?</c>, and <c>Right</c> answers its ordinal times ten,
    /// so one left-then-right evaluation of both is 1 and 20.</summary>
    private static ITypeDefinitionProvider CounterType() =>
        Analyze(new TypeDefinitionNode(
            "CountLot", "Branch",
            Fields: [new FieldDefinitionNode("n", TypeReference.To<long>(), new Constant(0L))],
            Constructors: [new ConstructorDefinitionNode([], new Block([]))],
            Methods: [
                CountingMethod("Next", 1L),
                CountingMethod("Right", 10L)
            ]));

    /// <summary>A method that increments <c>n</c> and answers <c>n * scale</c> as
    /// <c>long?</c>.</summary>
    private static MethodDefinitionNode CountingMethod(string name, long scale) =>
        new(
            name,
            new OptionalTypeReference(TypeReference.To<long>()),
            Parameters: [],
            Body: new Block([
                new Assignment(
                    new Member(new ThisReference(), "n"),
                    new Add(new Member(new ThisReference(), "n"), new Constant(1L))),
                new Return(new Multiply(new Member(new ThisReference(), "n"), new Constant(scale)))
            ]));

    private static Node Call(string method) =>
        new Invoke(new Member(new New(new TypeReference("Branch.CountLot")), method), []);

    private static ExecutionResult Execute(Node body, ITypeDefinitionProvider types) =>
        Interpreter.Execute(Interpreter.Compile(body, types));

    [Test, Timeout(10_000)]
    public async Task Add_NullableCallOperand_EvaluatesOnce(CancellationToken ct) {
        _ = ct;
        var types = CounterType();
        using var exec = Execute(new Add(Call("Next"), new Constant(1L)), types);
        await Assert.That(exec.Result.Value).IsEqualTo(2L)
            .Because("the one call answers 1, so one evaluation plus one is 2");
    }

    [Test, Timeout(10_000)]
    public async Task Add_TwoNullableCallOperands_EvaluateLeftThenRight(CancellationToken ct) {
        _ = ct;
        var types = CounterType();
        var lot = new Variable("lot");
        var body = new Block([
            new Assignment(lot, new New(new TypeReference("Branch.CountLot"))),
            new Add(
                new Invoke(new Member(lot, "Next"), []),
                new Invoke(new Member(lot, "Right"), [])),
        ], [lot]);
        using var exec = Execute(body, types);
        await Assert.That(exec.Result.Value).IsEqualTo(21L)
            .Because("on one instance, left answers the first ordinal and right the second one times ten");
    }

    [Test, Timeout(10_000)]
    public async Task Equal_NullableCallOperand_EvaluatesOnce(CancellationToken ct) {
        _ = ct;
        var types = CounterType();
        using var exec = Execute(
            new Equal(Call("Next"), new Constant(1L)), types);
        await Assert.That(exec.GetValue<bool>()).IsTrue()
            .Because("the single call answers 1; a second call would answer 2");
    }
}