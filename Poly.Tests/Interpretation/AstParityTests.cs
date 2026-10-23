using Poly.Interpretation;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.Interpretation;

/// <summary>
/// The writer, checked by Roslyn, over the AST corpus the suite already carries.
/// <para>
/// The corpus is the existing programs: every program in <c>Poly.Tests/Integration</c> and
/// <c>Poly.Tests/Interpretation</c> that the suite already asserted, rebuilt here one row per
/// program so the differential can run without the LINQ oracle. The
/// <c>ArithmeticParserEvaluatorTests</c> rows are the expressions that file's own parser consumes —
/// its parser is private, so the same text is read with <see cref="CSharpLanguage.Read"/>.
/// </para>
/// <para>
/// Each row runs both sides (<see cref="AstParity.Compare"/>). A program the two sides dispose of
/// differently must be a <see cref="KnownDivergence"/>: the row still runs, both disposals are
/// pinned, and the entry is the finding. Nothing is skipped and nothing is trusted.
/// </para>
/// </summary>
public class AstParityTests {
    /// <summary>A node the test file defines for its program: the shape the existing test builds, with the same constructors.</summary>
    public sealed class ParityWidget {
        public ParityWidget() {
        }

        public ParityWidget(string name, int count = 0) {
            Name = name;
            Count = count;
        }

        public string Name { get; } = "";
        public int Count { get; }
    }

    /// <summary>One corpus program, labelled with the test that builds it.</summary>
    public sealed record CorpusCase(string Label, Node Program);

    /// <summary>The existing programs, one row each, in the order the test files declare them.</summary>
    public static IEnumerable<CorpusCase> Corpus() => [
        new("MiddlewareInterpreterIntegrationTests.ConstantFoldingMiddleware_SimpleBinary_FoldsToConstantExpression",
            new Add(Wrap(10), Wrap(20))),
        new("MiddlewareInterpreterIntegrationTests.ConstantFoldingMiddleware_NestedOperations_ComputesCorrectly",
            new Add((new Add(Wrap(10), Wrap(20))), (new Add(Wrap(5), Wrap(3))))),
        new("MiddlewareInterpreterIntegrationTests.IncompatibleTypes_IntPlusString_ThrowsDuringCodeGeneration",
            new Add(Wrap(5), Wrap("hello"))),
        new("MiddlewareInterpreterIntegrationTests.NumericTypePromotion_IntPlusDouble_ProducesCorrectResult",
            new Add(Wrap(10), Wrap(5.5))),
        new("MiddlewareInterpreterIntegrationTests.NumericTypePromotion_IntTimesDouble_ProducesCorrectResult",
            new Multiply(Wrap(10), Wrap(2.5))),
        new("MiddlewareInterpreterIntegrationTests.UnaryMinus_WithPositiveValue_ReturnsNegated",
            new UnaryMinus(Wrap(42))),
        new("MiddlewareInterpreterIntegrationTests.TypeCast_IntToDouble_CastsCorrectly",
            new TypeCast(Wrap(42), TypeReference.To<double>())),
        new("MiddlewareInterpreterIntegrationTests.Block_MultipleStatements_ExecutesAllAndReturnsLast",
            new Block( Wrap(10), Wrap(20), Wrap(30))),
        new("MiddlewareInterpreterIntegrationTests.Modulo_TenModThree_ReturnsOne",
            new Modulo(Wrap(10), Wrap(3))),
        new("ArithmeticNodeTests.Add_TwoIntegers_ReturnsSum",
            new Add(new Constant(5), new Constant(3))),
        new("ArithmeticNodeTests.Add_TwoDoubles_ReturnsSum",
            new Add(new Constant(3.5), new Constant(2.5))),
        new("ArithmeticNodeTests.Add_IntAndDouble_PromotesToDouble",
            new Add(new Constant(5), new Constant(3.5))),
        new("ArithmeticNodeTests.Subtract_TwoIntegers_ReturnsDifference",
            new Subtract(new Constant(10), new Constant(3))),
        new("ArithmeticNodeTests.Subtract_ResultingInNegative_ReturnsNegativeNumber",
            new Subtract(new Constant(5), new Constant(10))),
        new("ArithmeticNodeTests.Multiply_TwoIntegers_ReturnsProduct",
            new Multiply(new Constant(6), new Constant(7))),
        new("ArithmeticNodeTests.Multiply_IntAndDouble_PromotesToDouble",
            new Multiply(new Constant(4), new Constant(2.5))),
        new("ArithmeticNodeTests.Divide_TwoIntegers_ReturnsQuotient",
            new Divide(new Constant(20), new Constant(4))),
        new("ArithmeticNodeTests.Divide_IntegerDivision_TruncatesResult",
            new Divide(new Constant(7), new Constant(2))),
        new("ArithmeticNodeTests.Divide_TwoDoubles_ReturnsDecimalQuotient",
            new Divide(new Constant(7.0), new Constant(2.0))),
        new("ArithmeticNodeTests.Modulo_TwoIntegers_ReturnsRemainder",
            new Modulo(new Constant(10), new Constant(3))),
        new("ArithmeticNodeTests.Modulo_ExactDivision_ReturnsZero",
            new Modulo(new Constant(15), new Constant(5))),
        new("ArithmeticNodeTests.NestedOperations_AddAndMultiply_EvaluatesCorrectly",
            new Multiply((new Add(new Constant(5), new Constant(3))), new Constant(2))),
        new("ArithmeticNodeTests.NestedOperations_MultiplyAndAdd_EvaluatesCorrectly",
            new Add((new Multiply(new Constant(2), new Constant(3))), new Constant(4))),
        new("ArithmeticNodeTests.ComplexExpression_MultipleOperations_EvaluatesCorrectly",
            new Subtract((new Multiply((new Add(new Constant(10), new Constant(5))), new Constant(2))), new Constant(3))),
        new("BlockTests.Block_WithSingleExpression_ReturnsValue",
            new Block(Wrap(42))),
        new("BlockTests.Block_WithMultipleExpressions_ReturnsLastValue",
            new Block(Wrap(10), Wrap(20), Wrap(99))),
        new("BlockTests.Block_WithArithmeticSequence_EvaluatesCorrectly",
            new Block( Wrap(10), new Add(Wrap(5), Wrap(3)), Wrap(100) )),
        new("BlockTests.Block_WithConditionalInside_WorksCorrectly",
            new Block((new Conditional(new Constant(true), Wrap(55), Wrap(0))))),
        new("BlockTests.Block_WithDifferentTypes_ReturnsLastExpressionType",
            new Block( Wrap("hello"), Wrap(42) )),
        new("CoalesceTests.Coalesce_WithNullLeft_ReturnsRightValue",
            new Coalesce(Wrap(null as int?), Wrap(42))),
        new("CoalesceTests.Coalesce_WithNonNullLeft_ReturnsLeftValue",
            new Coalesce(Wrap(100 as int?), Wrap(42))),
        new("CoalesceTests.Coalesce_ChainedOperators_WorksCorrectly",
            new Coalesce( Wrap(null as int?), new Coalesce(Wrap(null as int?), Wrap(10)) )),
        new("CoalesceTests.Coalesce_WithObjects_WorksCorrectly",
            new Coalesce(Wrap(null as string), Wrap("default"))),
        new("ConstantNodeTests.Constant_IntegerValue_CompilesAndExecutes",
            new Constant(42)),
        new("ConstantNodeTests.Constant_StringValue_CompilesAndExecutes",
            new Constant("hello")),
        new("ConstantNodeTests.Constant_DoubleValue_CompilesAndExecutes",
            new Constant(3.14)),
        new("ConstantNodeTests.Constant_BooleanTrue_CompilesAndExecutes",
            new Constant(true)),
        new("ConstantNodeTests.Constant_BooleanFalse_CompilesAndExecutes",
            new Constant(false)),
        new("ConstantNodeTests.Constant_NullValue_CompilesAndExecutes",
            new Constant(null)),
        new("ConstantNodeTests.Constant_DecimalValue_CompilesAndExecutes",
            new Constant(99.99m)),
        new("ConstantNodeTests.Constant_DateTime_CompilesAndExecutes",
            new Constant((new DateTime(2024, 1, 15, 10, 30, 0)))),
        new("ConstantNodeTests.Constant_ComplexObject_CompilesAndExecutes",
            new Constant((new List<int> { 1, 2, 3 }))),
        new("InterpreterIntegrationTests.ComplexExpression_MultipleNestedOperations_EvaluatesCorrectly",
            new Add((new Subtract((new Multiply((new Add(new Constant(10), new Constant(5))), new Constant(2))), (new Divide(new Constant(8), new Constant(2))))), new Constant(1))),
        new("InterpreterIntegrationTests.TypePromotion_IntAndDouble_PromotesCorrectly",
            new Multiply( new Add(new Constant(5), new Constant(2.5)), new Constant(2))),
        new("InterpreterIntegrationTests.UnaryMinusInComplexExpression_NegatesCorrectly",
            new Multiply((new Subtract(new Constant(10), (new UnaryMinus(new Constant(5))))), new Constant(2))),
        new("InterpreterIntegrationTests.BlockExpression_ReturnsLastValue",
            new Block( new Constant(10), new Constant(20), new Constant(30))),
        new("InterpreterIntegrationTests.BlockWithArithmetic_EvaluatesAllAndReturnsLast",
            new Block( new Add(new Constant(5), new Constant(3)), new Multiply(new Constant(10), new Constant(2)), new Divide(new Constant(100), new Constant(4)))),
        new("InterpreterIntegrationTests.StringConcatenation_AddingStrings_ConcatenatesCorrectly",
            new Add(new Constant("Hello "), new Constant("World"))),
        new("LambdaInvokeTests.Lambda_WithNoParameters_CompilesAndReturnsConstant",
            new Lambda([], Wrap(42))),
        new("ModuloTests.Modulo_WithIntegers_ReturnsRemainder",
            new Modulo(Wrap(17), Wrap(5))),
        new("ModuloTests.Modulo_WithExactDivision_ReturnsZero",
            new Modulo(Wrap(20), Wrap(5))),
        new("ModuloTests.Modulo_WithDoubles_ReturnsRemainder",
            new Modulo(Wrap(5.5), Wrap(2.0))),
        new("ModuloTests.Modulo_WithNegativeNumbers_ReturnsCorrectRemainder",
            new Modulo(Wrap(-17), Wrap(5))),
        new("NewNodeTests.New_ParameterlessConstructor_CompilesAndCreatesInstance",
            new New(TypeReference.To<ParityWidget>())),
        new("NewNodeTests.New_WithOptionalParameter_UsesDefaultValueWhenOmitted",
            new New(TypeReference.To<ParityWidget>(), Wrap("alpha"))),
        new("NewNodeTests.New_WithExplicitArguments_BindsAllConstructorParameters",
            new New(TypeReference.To<ParityWidget>(), Wrap("beta"), Wrap(42))),
        new("NumericTypePromotionTests.NumericTypePromotion_Add_IntAndDouble_ReturnsDouble",
            new Add(Wrap(10), Wrap(3.14))),
        new("NumericTypePromotionTests.NumericTypePromotion_Multiply_FloatAndInt_ReturnsFloat",
            new Multiply(Wrap(2.5f), Wrap(4))),
        new("NumericTypePromotionTests.NumericTypePromotion_Subtract_LongAndInt_ReturnsLong",
            new Subtract(Wrap(100L), Wrap(30))),
        new("NumericTypePromotionTests.NumericTypePromotion_Divide_DecimalAndInt_ReturnsDecimal",
            new Divide(Wrap(100m), Wrap(4))),
        new("NumericTypePromotionTests.NumericTypePromotion_Modulo_DoubleAndFloat_ReturnsDouble",
            new Modulo(Wrap(10.0), Wrap(3.0f))),
        new("NumericTypePromotionTests.NumericTypePromotion_Add_TwoInts_ReturnsInt",
            new Add(Wrap(10), Wrap(20))),
        new("NumericTypePromotionTests.NumericTypePromotion_Add_ByteAndShort_ReturnsInt",
            new Add(Wrap((byte)5), Wrap((short)10))),
        new("NumericTypePromotionTests.NumericTypePromotion_Multiply_UIntAndLong_ReturnsLong",
            new Multiply(Wrap(5u), Wrap(10L))),
        new("NumericTypePromotionTests.NumericTypePromotion_Add_ULongAndInt_ReturnsULong",
            new Add(Wrap(100UL), Wrap(50))),
        new("TypeCastTests.TypeCast_IntToDouble_ReturnsDouble",
            new TypeCast(Wrap(42), TypeReference.To<double>())),
        new("TypeCastTests.TypeCast_DoubleToInt_ReturnsInt",
            new TypeCast(Wrap(3.14), TypeReference.To<int>())),
        new("TypeCastTests.TypeCast_LongToInt_ReturnsInt",
            new TypeCast(Wrap(9999L), TypeReference.To<int>())),
        new("TypeCastTests.TypeCast_StringToObject_WorksCorrectly",
            new TypeCast(Wrap("hello"), TypeReference.To<object>())),
        new("TypeCastTests.TypeCast_ObjectToString_WorksCorrectly",
            new TypeCast(Wrap(((object)"world")), TypeReference.To<string>())),
        new("TypeCastTests.TypeCast_NullableToNonNullable_WorksCorrectly",
            new TypeCast(Wrap(42 as int?), TypeReference.To<int>())),
        new("TypeCastTests.TypeCast_NonNullableToNullable_WorksCorrectly",
            new TypeCast(Wrap(42), TypeReference.To<int?>())),
        new("TypeCastTests.TypeIs_ObjectAgainstString_ReturnsTrue",
            new TypeIs((Wrap((object)"hello")), TypeReference.To<string>())),
        new("TypeCastTests.TypeIs_ObjectAgainstInt_ReturnsFalse",
            new TypeIs((Wrap((object)"hello")), TypeReference.To<int>())),
        new("TypeCastTests.TypeAs_ObjectToString_ReturnsStringValue",
            new TypeAs((Wrap((object)"hello")), TypeReference.To<string>())),
        new("TypeCastTests.TypeAs_ObjectToString_WithIncompatibleValue_ReturnsNull",
            new TypeAs((Wrap((object)42)), TypeReference.To<string>())),
        new("TypeCastTests.TypeAs_ObjectToNonNullableValueType_ReturnsNullableValue",
            new TypeAs((Wrap((object)42)), TypeReference.To<int>())),
        new("UnaryMinusTests.UnaryMinus_WithPositiveInteger_ReturnsNegative",
            new UnaryMinus(Wrap(42))),
        new("UnaryMinusTests.UnaryMinus_WithNegativeInteger_ReturnsPositive",
            new UnaryMinus(Wrap(-99))),
        new("UnaryMinusTests.UnaryMinus_WithZero_ReturnsZero",
            new UnaryMinus(Wrap(0))),
        new("UnaryMinusTests.UnaryMinus_WithDouble_NegatesCorrectly",
            new UnaryMinus(Wrap(3.14))),
        new("ArithmeticParserEvaluatorTests.SimpleAddition_TwoPlusThree_ReturnsFive",
            Program("2 + 3;")),
        new("ArithmeticParserEvaluatorTests.SimpleSubtraction_TenMinusFour_ReturnsSix",
            Program("10 - 4;")),
        new("ArithmeticParserEvaluatorTests.SimpleMultiplication_ThreeTimesFour_ReturnsTwelve",
            Program("3 * 4;")),
        new("ArithmeticParserEvaluatorTests.SimpleDivision_TwentyDividedByFive_ReturnsFour",
            Program("20 / 5;")),
        new("ArithmeticParserEvaluatorTests.SimpleModulo_TenModThree_ReturnsOne",
            Program("10 % 3;")),
        new("ArithmeticParserEvaluatorTests.OperatorPrecedence_AdditionAndMultiplication_MultipliesFirst",
            Program("2 + 3 * 4;")),
        new("ArithmeticParserEvaluatorTests.OperatorPrecedence_SubtractionAndDivision_DividesFirst",
            Program("20 - 10 / 2;")),
        new("ArithmeticParserEvaluatorTests.Parentheses_OverridesPrecedence_AddsFirst",
            Program("(2 + 3) * 4;")),
        new("ArithmeticParserEvaluatorTests.NestedParentheses_ComplexExpression_EvaluatesCorrectly",
            Program("((2 + 3) * (4 + 1)) - 10;")),
        new("ArithmeticParserEvaluatorTests.ComplexExpression_MultipleOperators_EvaluatesCorrectly",
            Program("10 + 5 * 2 - 8 / 4;")),
        new("ArithmeticParserEvaluatorTests.UnaryMinus_NegativeNumber_ReturnsNegative",
            Program("-5;")),
        new("ArithmeticParserEvaluatorTests.UnaryMinus_InExpression_EvaluatesCorrectly",
            Program("10 + -5;")),
        new("ArithmeticParserEvaluatorTests.UnaryMinus_WithParentheses_EvaluatesCorrectly",
            Program("-(10 + 5);")),
        new("ArithmeticParserEvaluatorTests.DecimalNumbers_Addition_ReturnsDouble",
            Program("2.5 + 3.7;")),
        new("ArithmeticParserEvaluatorTests.MixedTypes_IntAndDouble_PromotesToDouble",
            Program("10 + 2.5;")),
        new("ArithmeticParserEvaluatorTests.MixedTypes_ComplexExpression_PromotesCorrectly",
            Program("10 * 2.5 - 5 / 2.0;")),
        new("ArithmeticParserEvaluatorTests.LongExpression_ManyOperations_EvaluatesCorrectly",
            Program("1 + 2 * 3 - 4 / 2 + 5 * (6 - 2);")),
        new("ArithmeticParserEvaluatorTests.WhitespaceHandling_VariousSpacing_ParsesCorrectly",
            Program("2+3*4;")),
        new("ArithmeticParserEvaluatorTests.WhitespaceHandling_VariousSpacing_ParsesCorrectly",
            Program("2 + 3 * 4;")),
        new("ArithmeticParserEvaluatorTests.WhitespaceHandling_VariousSpacing_ParsesCorrectly",
            Program("  2  +  3  *  4  ;")),
        new("ArithmeticParserEvaluatorTests.InvalidOperation_DivideByZero_ThrowsException",
            Program("2 / 0;")),
        new("ArithmeticParserEvaluatorTests.ZeroValues_Operations_HandlesCorrectly",
            Program("0 + 5;")),
        new("ArithmeticParserEvaluatorTests.ZeroValues_Operations_HandlesCorrectly",
            Program("5 - 0;")),
        new("ArithmeticParserEvaluatorTests.ZeroValues_Operations_HandlesCorrectly",
            Program("0 * 5;")),
        new("ArithmeticParserEvaluatorTests.ZeroValues_Operations_HandlesCorrectly",
            Program("0 / 5;")),
        new("ArithmeticParserEvaluatorTests.LargeNumbers_Addition_HandlesCorrectly",
            Program("1000000 + 2000000;")),
        new("ArithmeticParserEvaluatorTests.ChainedOperations_LeftToRight_EvaluatesCorrectly",
            Program("10 - 5 - 2;")),
        new("ArithmeticParserEvaluatorTests.ChainedMultiplication_LeftToRight_EvaluatesCorrectly",
            Program("2 * 3 * 4;")),
        new("InterpreterLanguageGotchaTests.Equal_NaN_IsFalse",
            new Equal(new Constant(double.NaN), new Constant(double.NaN))),
        new("InterpreterLanguageGotchaTests.NotEqual_NaN_IsTrue",
            new NotEqual(new Constant(double.NaN), new Constant(double.NaN))),
        new("InterpreterLanguageGotchaTests.LessThan_NaN_IsFalse",
            new LessThan(new Constant(double.NaN), new Constant(0.0))),
        new("InterpreterLanguageGotchaTests.Divide_NegativeInts_TruncatesTowardZero",
            new Divide(new Constant(-7L), new Constant(2L))),
        new("InterpreterLanguageGotchaTests.Add_LongOverflow_WrapsUnchecked",
            new Add(new Constant(long.MaxValue), new Constant(1L))),
        .. ScopedPrograms(),
    ];

    /// <summary>
    /// Programs whose meaning depends on node identity: a local is declared once and reused, and a
    /// shadowing loop variable is a second instance of the same name. Rebuilding them here keeps the
    /// instances the existing test builds.
    /// </summary>
    static IEnumerable<CorpusCase> ScopedPrograms() {
        var x = new Variable("x");
        yield return new("BlockScopeTests.BlockScope_CreatesNewScope_VariablesNotVisibleOutside",
            new Block([new Assignment(x, Wrap(100)), new Block([new Assignment(x, Wrap(50)), x], [x])], [x]));
        yield return new("BlockScopeTests.BlockScope_NestedScopes_InnerShadowsOuter",
            new Block([new Assignment(x, Wrap(100)), new Block([new Assignment(x, Wrap(50)), x], [x])], [x]));
        yield return new("BlockScopeTests.BlockScope_CanAccessOuterScope_Variables",
            new Block([new Assignment(x, Wrap(100)), x.Add(Wrap(50))], [x]));

        var a = new Variable("a");
        var b = new Variable("b");
        yield return new("BlockScopeTests.BlockScope_ExecutesExpressions_InSequence",
            new Block([new Assignment(a, Wrap(10)), new Assignment(b, Wrap(20)), b], [a, b]));

        var first = new Variable("x");
        var second = new Variable("x");
        yield return new("BlockScopeTests.BlockScope_MultipleBlocks_IndependentScopes",
            new Block(
                new Block([new Assignment(first, Wrap(10)), first], [first]),
                new Block([new Assignment(second, Wrap(20)), second], [second])));

        var declared = new Variable("x");
        yield return new("BlockTests.Block_WithVariableDeclaration_WorksCorrectly",
            new Block([new Assignment(declared, Wrap(50)), declared], [declared]));

        var sum = new Variable("sum");
        var item = new Variable("item");
        yield return new("ForEachLoopTests.ForEachLoop_UsesLoopVariableInsideBody",
            new Block([
                new Assignment(sum, new Constant(0)),
                new ForEachLoop(item, new Constant(new[] { 1, 2, 3, 4 }), new Assignment(sum, new Add(sum, item))),
                sum,
            ], [sum]));
        yield return new("ForEachLoopTests.ForEachLoop_EmptyCollection_DoesNotExecuteBody",
            new Block([
                new Assignment(sum, new Constant(5)),
                new ForEachLoop(item, new Constant(Array.Empty<int>()), new Assignment(sum, new Add(sum, item))),
                sum,
            ], [sum]));
        yield return new("ForEachLoopTests.ForEachLoop_Continue_SkipsRemainingBodyForCurrentIteration",
            new Block([
                new Assignment(sum, new Constant(0)),
                new ForEachLoop(item, new Constant(new[] { 1, 2, 3, 4 }), new Block(
                    new IfStatement(
                        new Equal(new Modulo(item, new Constant(2)), new Constant(0)),
                        new ContinueStatement()),
                    new Assignment(sum, new Add(sum, item)))),
                sum,
            ], [sum]));
        yield return new("ForEachLoopTests.ForEachLoop_Break_StopsIterating",
            new Block([
                new Assignment(sum, new Constant(0)),
                new ForEachLoop(item, new Constant(new[] { 1, 2, 3, 4, 5 }), new Block(
                    new IfStatement(new GreaterThan(item, new Constant(3)), new BreakStatement()),
                    new Assignment(sum, new Add(sum, item)))),
                sum,
            ], [sum]));

        var outerItem = new Variable("item");
        var loopItem = new Variable("item");
        var lastSeen = new Variable("lastSeen");
        yield return new("ForEachLoopTests.ForEachLoop_ShadowsOuterVariable_WithoutLeakingValue",
            new Block([
                new Assignment(outerItem, new Constant(100)),
                new Assignment(lastSeen, new Constant(0)),
                new ForEachLoop(loopItem, new Constant(new[] { 1, 2, 3 }), new Assignment(lastSeen, loopItem)),
                new Add(outerItem, lastSeen),
            ], [outerItem, lastSeen]));

        var leaked = new Variable("item");
        yield return new("ForEachLoopTests.ForEachLoop_LoopVariableDoesNotLeakOutsideLoopScope",
            new Block(new ForEachLoop(leaked, new Constant(new[] { 1 }), new Constant(0)), leaked));

        var v = new Parameter("v", TypeReference.To<int>());
        yield return new("LambdaInvokeTests.Invoke_WithLambdaNode_ExecutesBody",
            new Invoke(new Lambda([v], new Add(v, Wrap(10))), Wrap(5)));
        var w = new Parameter("v", TypeReference.To<int>());
        yield return new("LambdaInvokeTests.InvokeWith_ExtensionMethod_BuildsInvokeNode",
            new Lambda([w], new Add(w, Wrap(100))).InvokeWith(Wrap(7)));
    }

    static IReadOnlyList<CorpusCase> Programs { get; } = [.. Corpus()];

    /// <summary>The statement a C# fragment is, as the writer's unit entry expects a node, not a unit.</summary>
    static Node Program(string text) {
        var unit = CSharpLanguage.Read(text);
        var statements = unit.TopLevelStatements;
        return statements is { Count: > 0 } ? statements[0] : unit;
    }

    [Test]
    [MethodDataSource(nameof(Corpus))]
    public async Task Corpus_PrintedCSharp_AgreesWithTheVm(CorpusCase corpusCase) {
        var row = AstParity.Compare(corpusCase.Label, corpusCase.Program);
        if (row.Agrees)
            return;
        var listed = AstParity.KnownDivergences.Any(d => d.Matches(row));
        await Assert.That(listed).IsTrue().Because(row.Describe());
    }

    [Test]
    public async Task PrintedText_HasZeroRoslynDiagnostics() {
        var refused = new List<string>();
        var syntaxErrors = new List<string>();
        foreach (var corpusCase in Programs) {
            string text;
            try {
                text = AstParity.Print(corpusCase.Program);
            }
            catch (InvalidOperationException ex) {
                refused.Add($"{corpusCase.Label}: {ex.Message}");
                continue;
            }
            var errors = AstParity.SyntaxErrors(text);
            if (errors.Count == 0)
                continue;
            // A program the VM also rejects is a recorded pair: both sides refused it, so the
            // printed refusal's form (syntax or compile) says nothing about the writer's claims.
            if (AstParity.RunVm(corpusCase.Program).Disposal == ParityDisposal.Rejected)
                continue;
            var listed = AstParity.KnownDivergences.Any(d =>
                d.Where == corpusCase.Label && d.PrintedOutcomePrefix.StartsWith("Roslyn syntax errors:", StringComparison.Ordinal));
            if (!listed)
                syntaxErrors.Add($"{corpusCase.Label}: {string.Join("; ", errors)}\n{text}");
        }
        await Assert.That(refused).IsEmpty()
            .Because("every corpus program prints; the writer refusing one is a writer defect, not a skip");
        await Assert.That(syntaxErrors).IsEmpty()
            .Because("printed text must parse; a program that does not is a listed defect");
    }

    [Test]
    public async Task VmRejects_AndPrintedExecutes_IsAFailure() {
        // The shape the old harness returned early on. No corpus program has it today, so the
        // decision itself is pinned: the same program refused by one side and run by the other is
        // not agreement, and the message names both sides plus the emitted source.
        var row = new ParityRow(
            "VM refused, printed ran",
            ParityDisposal.Rejected, "InvalidOperationException: VM compile rejected: variable 'x' is not declared",
            ParityDisposal.Executed, "long:7",
            "class PolyParityHarness { public static object Run() { return 7; } }");
        await Assert.That(row.Agrees).IsFalse();
        await Assert.That(row.RejectedOnBothSides).IsFalse();
        await Assert.That(row.Describe()).Contains("VM compile rejected");
        await Assert.That(row.Describe()).Contains("long:7");
        await Assert.That(row.Describe()).Contains("public static object Run()");
    }

    [Test]
    public async Task VmExecutes_AndWriterRejects_IsAFailure() {
        var row = AstParity.Compare("PopCount", new PopCount(new Constant(7L)));
        await Assert.That(row.Vm).IsEqualTo(ParityDisposal.Executed);
        await Assert.That(row.Printed).IsEqualTo(ParityDisposal.Rejected);
        await Assert.That(row.PrintedOutcome).Contains(nameof(PopCount));
        await Assert.That(row.Agrees).IsFalse();
    }

    [Test]
    public async Task RejectionOnBothSides_IsARecordedRow() {
        var row = AstParity.Compare(
            "arithmetic on a string",
            new Subtract(new Constant(1L), new Constant("a")));
        await Assert.That(row.Vm).IsEqualTo(ParityDisposal.Rejected);
        await Assert.That(row.Printed).IsEqualTo(ParityDisposal.Rejected);
        await Assert.That(row.RejectedOnBothSides).IsTrue();
        await Assert.That(row.Agrees).IsTrue();
        await Assert.That(row.Source).IsNotNull()
            .Because("a Roslyn refusal still reports the text it refused");
    }

    /// <summary>
    /// A kind the VM honours and the writer cannot print is a row with both disposals asserted, never
    /// a skip. The sample table must cover exactly the kinds <see cref="NodeFacts"/> claims for the VM
    /// and not for the writer, so a new such kind cannot escape the list.
    /// </summary>
    [Test]
    public async Task UnsupportedKinds_AreListedNotSkipped() {
        var unprintable = NodeFacts.All
            .Where(kv => kv.Value.Producers.HasFlag(Producers.Vm) && !kv.Value.Producers.HasFlag(Producers.Writer))
            .Select(kv => kv.Key)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();
        var samples = Samples().Keys.OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();
        await Assert.That(samples).IsEquivalentTo(unprintable)
            .Because("a kind the VM honours and the writer cannot print needs a listed row");

        var failures = new List<string>();
        foreach (var (kind, sample) in Samples()) {
            var row = AstParity.Compare(kind.Name, Sample(kind));
            void Expect(bool condition, string expected, string actual) {
                if (!condition)
                    failures.Add($"{kind.Name}: expected {expected}, got {actual}");
            }
            Expect(row.Vm == sample.Vm, $"VM {sample.Vm}", row.VmOutcome);
            Expect(row.VmOutcome == sample.VmOutcome, $"VM outcome '{sample.VmOutcome}'", row.VmOutcome);
            Expect(row.Printed == sample.Printed, $"printed {sample.Printed}", row.PrintedOutcome);
            Expect(row.PrintedOutcome.StartsWith(sample.PrintedOutcomePrefix, StringComparison.Ordinal),
                $"printed outcome starting '{sample.PrintedOutcomePrefix}'", row.PrintedOutcome);
            Expect(row.Agrees == sample.Agrees, $"agrees={sample.Agrees}", row.Agrees.ToString());
        }
        await Assert.That(failures).IsEmpty();
    }

    /// <summary>
    /// Every listed divergence is evidence from a corpus row: the program still exists, and it still
    /// disposes the way the entry records. An entry with no row is deleted, not left to rot.
    /// </summary>
    [Test]
    public async Task KnownDivergences_AreEvidenceFromTheCorpus() {
        var stale = new List<string>();
        foreach (var divergence in AstParity.KnownDivergences) {
            var rows = Programs
                .Where(c => c.Label == divergence.Where)
                .Select(c => AstParity.Compare(c.Label, c.Program))
                .ToArray();
            if (!rows.Any(divergence.Matches))
                stale.Add($"{divergence.Where}: {divergence.Finding} "
                    + $"(recorded {divergence.Vm} '{divergence.VmOutcome}' / {divergence.Printed} '{divergence.PrintedOutcomePrefix}', "
                    + $"found {(rows.Length == 0 ? "no corpus row" : string.Join(" | ", rows.Select(r => $"{r.Vm} '{r.VmOutcome}' / {r.Printed} '{r.PrintedOutcome}'")))}");
        }
        await Assert.That(stale).IsEmpty()
            .Because("a divergence with no contradicting row is a stale finding, not a skip");
    }

    /// <summary>The kinds the writer cannot print, with the disposal each side must have today.</summary>
    static Dictionary<Type, PrintDisposition> Samples() => new() {
        // The writer prints a comment, so both sides execute as void.
        [typeof(Comment)] = new(ParityDisposal.Executed, ParityDisposal.Executed, "void", "void", Agrees: true),
        // The writer refuses the kind by name (IS1c carries the missing spelling).
        [typeof(PopCount)] = new(ParityDisposal.Executed, ParityDisposal.Rejected, "long:3", "writer refused:", Agrees: false),
        // At the entry point both sides refuse it: the writer keeps the kind out of the reader's reach,
        // and the VM needs a declared array. Inside a block the writer prints it and both sides run it.
        [typeof(StridedSetBits)] = new(ParityDisposal.Rejected, ParityDisposal.Rejected,
            "InvalidOperationException: VM compile rejected: Variable 'bits' is not declared in this scope",
            "writer refused:", Agrees: true),
        // The writer erases the marker and prints its child, so the VM signals while the printed side returns.
        [typeof(SuspendNode)] = new(ParityDisposal.Executed, ParityDisposal.Executed, "suspend", "long:7", Agrees: false),
    };

    sealed record PrintDisposition(
        ParityDisposal Vm, ParityDisposal Printed, string VmOutcome, string PrintedOutcomePrefix, bool Agrees);

    static Node Sample(Type kind) => kind switch {
        _ when kind == typeof(Comment) => new Comment("note"),
        _ when kind == typeof(PopCount) => new PopCount(new Constant(7L)),
        _ when kind == typeof(StridedSetBits) => new StridedSetBits(
            new Variable("bits"), new Constant(0L), new Constant(1L), new Constant(63L)),
        _ when kind == typeof(SuspendNode) => new SuspendNode(new Constant(7L)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "no sample"),
    };
}