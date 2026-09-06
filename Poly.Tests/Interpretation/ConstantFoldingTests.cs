using Poly.Interpretation;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Tests.Interpretation;

public class ConstantFoldingTests {
    [Test]
    public async Task AddConstants_FoldsToSum() {
        // Arrange: 1 + 2
        var ast = new Add(Wrap(1), Wrap(2));

        // Act
        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis().Build().Analyze(ast);

        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast)).IsEqualTo(3);

        var add = (Add)ast;
        await Assert.That(result.GetMetadata<ConstantValueMetadata>(add.LeftHandValue)).IsNull();
        await Assert.That(result.GetMetadata<ConstantValueMetadata>(add.RightHandValue)).IsNull();
        await Assert.That(result.GetMetadata<ConstantValueMetadata>(ast)).IsNotNull();
    }

    [Test]
    public async Task SubtractConstants_FoldsToDifference() {
        // Arrange: 10 - 3
        var ast = new Subtract(Wrap(10), Wrap(3));

        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis().Build().Analyze(ast);


        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast)).IsEqualTo(7);
    }

    [Test]
    public async Task MultiplyConstants_FoldsToProduct() {
        // Arrange: 4 * 5
        var ast = new Multiply(Wrap(4), Wrap(5));

        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis().Build().Analyze(ast);


        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast)).IsEqualTo(20);
    }

    [Test]
    public async Task DivideConstants_FoldsToQuotient() {
        // Arrange: 20 / 4
        var ast = new Divide(Wrap(20), Wrap(4));

        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis().Build().Analyze(ast);


        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast)).IsEqualTo(5);
    }

    [Test]
    public async Task ModuloConstants_FoldsToRemainder() {
        // Arrange: 17 % 5
        var ast = new Modulo(Wrap(17), Wrap(5));

        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis().Build().Analyze(ast);


        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast)).IsEqualTo(2);
    }

    [Test]
    public async Task UnaryMinus_FoldsToNegation() {
        // Arrange: -42
        var ast = new UnaryMinus(Wrap(42));

        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis().Build().Analyze(ast);


        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast)).IsEqualTo(-42);
    }

    [Test]
    public async Task NestedArithmetic_FoldsRecursively() {
        // Arrange: (1 + 2) * (3 + 4) = 3 * 7 = 21
        var left = new Add(Wrap(1), Wrap(2));
        var right = new Add(Wrap(3), Wrap(4));
        var ast = new Multiply(left, right);

        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis().Build().Analyze(ast);


        // Assert
        await Assert.That(result.IsConstant(left)).IsTrue();
        await Assert.That(result.GetConstantValue(left)).IsEqualTo(3);
        await Assert.That(result.IsConstant(right)).IsTrue();
        await Assert.That(result.GetConstantValue(right)).IsEqualTo(7);
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast)).IsEqualTo(21);
    }

    [Test]
    public async Task AndBoolean_FoldsCorrectly() {
        // Arrange: true && false
        var ast = new And(Wrap(true), Wrap(false));

        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis().Build().Analyze(ast);


        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That((bool?)result.GetConstantValue(ast)).IsFalse();
    }

    [Test]
    public async Task OrBoolean_FoldsCorrectly() {
        // Arrange: true || false
        var ast = new Or(Wrap(true), Wrap(false));

        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis().Build().Analyze(ast);


        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That((bool?)result.GetConstantValue(ast)).IsTrue();
    }

    [Test]
    public async Task NotBoolean_FoldsCorrectly() {
        // Arrange: !true
        var ast = new Not(Wrap(true));

        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis().Build().Analyze(ast);


        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That((bool?)result.GetConstantValue(ast)).IsFalse();
    }

    [Test]
    public async Task GreaterThan_FoldsCorrectly() {
        // Arrange: 5 > 3
        var ast = new GreaterThan(Wrap(5), Wrap(3));

        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis().Build().Analyze(ast);


        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That((bool?)result.GetConstantValue(ast)).IsTrue();
    }

    [Test]
    public async Task LessThanOrEqual_FoldsCorrectly() {
        // Arrange: 3 <= 3
        var ast = new LessThanOrEqual(Wrap(3), Wrap(3));

        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis().Build().Analyze(ast);


        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That((bool?)result.GetConstantValue(ast)).IsTrue();
    }

    [Test]
    public async Task Equal_FoldsCorrectly() {
        // Arrange: 42 == 42
        var ast = new Equal(Wrap(42), Wrap(42));

        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis().Build().Analyze(ast);


        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That((bool?)result.GetConstantValue(ast)).IsTrue();
    }

    [Test]
    public async Task NotEqual_FoldsCorrectly() {
        // Arrange: 42 != 43
        var ast = new NotEqual(Wrap(42), Wrap(43));

        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis().Build().Analyze(ast);


        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That((bool?)result.GetConstantValue(ast)).IsTrue();
    }

    [Test]
    public async Task ConditionalWithTrueCondition_FoldsToThenBranch() {
        // Arrange: true ? 1 : 2
        var ast = new Conditional(Wrap(true), Wrap(1), Wrap(2));

        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis().Build().Analyze(ast);


        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast)).IsEqualTo(1);
    }

    [Test]
    public async Task ConditionalWithFalseCondition_FoldsToElseBranch() {
        // Arrange: false ? 1 : 2
        var ast = new Conditional(Wrap(false), Wrap(1), Wrap(2));

        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis().Build().Analyze(ast);


        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast)).IsEqualTo(2);
    }

    [Test]
    public async Task NonConstantExpression_DoesNotFold() {
        // Arrange: x + 1 (where x is a variable)
        var variable = new Variable("x");
        var ast = new Add(variable, Wrap(1));

        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis().Build().Analyze(ast);


        // Assert
        await Assert.That(result.IsConstant(ast)).IsFalse();
        await Assert.That(result.IsConstant(variable)).IsFalse();
    }

    [Test]
    public async Task DivisionByZero_DoesNotFold() {
        // Arrange: 10 / 0
        var ast = new Divide(Wrap(10), Wrap(0));

        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis().Build().Analyze(ast);


        // Assert - division by zero should not fold
        await Assert.That(result.IsConstant(ast)).IsFalse();
    }

    [Test]
    public async Task FloatingPointArithmetic_FoldsCorrectly() {
        // Arrange: 3.5 + 2.5
        var ast = new Add(Wrap(3.5), Wrap(2.5));

        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis().Build().Analyze(ast);


        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast)).IsEqualTo(6.0);
    }

    [Test]
    public async Task And_WithConstantFalseLeft_FoldsToFalse() {
        var parameter = new Parameter("flag", TypeReference.To<bool>());
        var ast = new And(Wrap(false), parameter);

        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis().Build().Analyze(ast);


        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That((bool?)result.GetConstantValue(ast)).IsFalse();
    }

    [Test]
    public async Task Or_WithConstantTrueLeft_FoldsToTrue() {
        var parameter = new Parameter("flag", TypeReference.To<bool>());
        var ast = new Or(Wrap(true), parameter);

        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis().Build().Analyze(ast);


        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That((bool?)result.GetConstantValue(ast)).IsTrue();
    }

    [Test]
    public async Task StringConcatenation_FoldsCorrectly() {
        // Arrange: "Hello" + " World"
        var ast = new Add(Wrap("Hello"), Wrap(" World"));

        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis().Build().Analyze(ast);


        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast)).IsEqualTo("Hello World");
    }

    [Test]
    public async Task Coalesce_WithNonNullLeft_FoldsToLeft() {
        // Arrange: "value" ?? "default"
        var ast = new Coalesce(Wrap("value"), Wrap("default"));

        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis().Build().Analyze(ast);


        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast)).IsEqualTo("value");
    }

    [Test]
    public async Task InvokeLambda_WithConstantArguments_FoldsToInvokedResult() {
        var parameter = new Parameter("x", TypeReference.To<int>());
        var lambda = new Lambda([parameter], new Add(parameter, Wrap(10)));
        var ast = new Invoke(lambda, Wrap(5));

        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis().Build().Analyze(ast);


        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast)).IsEqualTo(15);
    }

    [Test]
    public async Task InvokeLambda_WithCapturedConstantArguments_FoldsNestedExpression() {
        var outer = new Parameter("outer", TypeReference.To<int>());
        var inner = new Parameter("inner", TypeReference.To<int>());
        var lambda = new Lambda([inner], new Add(inner, new Multiply(outer, Wrap(2))));
        var ast = new Invoke(new Lambda([outer], new Invoke(lambda, Wrap(5))), Wrap(3));

        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis().Build().Analyze(ast);


        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast)).IsEqualTo(11);
    }

    [Test]
    public async Task ComplexExpression_FoldsCompletely() {
        // Arrange: ((2 + 3) * 4 - 5) / 3 = (5 * 4 - 5) / 3 = (20 - 5) / 3 = 15 / 3 = 5
        var add = new Add(Wrap(2), Wrap(3));
        var mul = new Multiply(add, Wrap(4));
        var sub = new Subtract(mul, Wrap(5));
        var ast = new Divide(sub, Wrap(3));

        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis().Build().Analyze(ast);


        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast)).IsEqualTo(5);
    }

    [Test]
    public async Task FoldedAdd_CompileExecute_UsesReplacementValue() {
        var add = new Add(Wrap(2), Wrap(3));
        var analysis = new AnalyzerBuilder()
            .UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis()
            .Build()
            .Analyze(add);
        await Assert.That(analysis.GetConstantValue(add)).IsEqualTo(5);
        var replacement = analysis.GetNodeReplacement(add);
        await Assert.That(replacement).IsNotNull();
        await Assert.That(replacement).IsTypeOf<Constant>();
        await Assert.That(((Constant)replacement!).Value).IsEqualTo(5);

        // Emitter compiles GetNodeReplacement — execute must yield the folded constant.
        using var exec = Interpreter.Execute(Interpreter.Compile(add));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(5L);
    }
}
