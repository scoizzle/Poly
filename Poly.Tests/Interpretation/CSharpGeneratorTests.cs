using Poly.Interpretation.CSharp;
using Poly.Introspection;

namespace Poly.Tests.Interpretation;

public class CSharpGeneratorTests {
    [Test]
    public async Task Generate_ConstantInt_ProducesLiteral() {
        var node = new Constant(42);
        var result = new CSharpGenerator().Generate(node);
        await Assert.That(result).IsEqualTo("42;");
    }

    [Test]
    public async Task Generate_ConstantString_ProducesQuotedString() {
        var node = new Constant("hello");
        var result = new CSharpGenerator().Generate(node);
        await Assert.That(result).IsEqualTo("\"hello\";");
    }

    [Test]
    public async Task Generate_ConstantNull_ProducesNull() {
        var node = new Constant(null);
        var result = new CSharpGenerator().Generate(node);
        await Assert.That(result).IsEqualTo("null;");
    }

    [Test]
    public async Task Generate_ConstantBool_ProducesTrueFalse() {
        var trueResult = new CSharpGenerator().Generate(new Constant(true));
        var falseResult = new CSharpGenerator().Generate(new Constant(false));
        await Assert.That(trueResult).IsEqualTo("true;");
        await Assert.That(falseResult).IsEqualTo("false;");
    }

    [Test]
    public async Task Generate_Variable_ProducesVariableName() {
        var node = new Variable("x");
        var result = new CSharpGenerator().Generate(node);
        await Assert.That(result).IsEqualTo("var x;");
    }

    [Test]
    public async Task Generate_Parameter_ProducesParameterName() {
        var node = new Parameter("y");
        var result = new CSharpGenerator().Generate(node);
        await Assert.That(result).IsEqualTo("y;");
    }

    [Test]
    public async Task Generate_ThisReference_ProducesThis() {
        var result = new CSharpGenerator().Generate(new ThisReference());
        await Assert.That(result).IsEqualTo("this;");
    }

    [Test]
    public async Task Generate_AddExpression_ProducesInfix() {
        var node = new Add(new Constant(1), new Constant(2));
        var result = new CSharpGenerator().Generate(node);
        await Assert.That(result).IsEqualTo("1 + 2;");
    }

    [Test]
    public async Task Generate_SubtractExpression_ProducesInfix() {
        var node = new Subtract(new Constant(5), new Constant(3));
        var result = new CSharpGenerator().Generate(node);
        await Assert.That(result).IsEqualTo("5 - 3;");
    }

    [Test]
    public async Task Generate_MultiplyExpression_ProducesInfix() {
        var node = new Multiply(new Constant(4), new Constant(2));
        var result = new CSharpGenerator().Generate(node);
        await Assert.That(result).IsEqualTo("4 * 2;");
    }

    [Test]
    public async Task Generate_DivideExpression_ProducesInfix() {
        var node = new Divide(new Constant(10), new Constant(2));
        var result = new CSharpGenerator().Generate(node);
        await Assert.That(result).IsEqualTo("10 / 2;");
    }

    [Test]
    public async Task Generate_NestedArithmetic_ProducesCorrectOrder() {
        var node = new Add(new Multiply(new Constant(3), new Constant(4)), new Constant(5));
        var result = new CSharpGenerator().Generate(node);
        await Assert.That(result).IsEqualTo("3 * 4 + 5;");
    }

    [Test]
    public async Task Generate_Equality_ProducesDoubleEquals() {
        var node = new Equal(new Constant(1), new Constant(2));
        var result = new CSharpGenerator().Generate(node);
        await Assert.That(result).IsEqualTo("1 == 2;");
    }

    [Test]
    public async Task Generate_NotEqual_ProducesBangEquals() {
        var node = new NotEqual(new Constant(1), new Constant(2));
        var result = new CSharpGenerator().Generate(node);
        await Assert.That(result).IsEqualTo("1 != 2;");
    }

    [Test]
    public async Task Generate_Comparison_ProducesCorrectOperator() {
        var lt = new CSharpGenerator().Generate(new LessThan(new Constant(1), new Constant(2)));
        var lte = new CSharpGenerator().Generate(new LessThanOrEqual(new Constant(1), new Constant(2)));
        var gt = new CSharpGenerator().Generate(new GreaterThan(new Constant(1), new Constant(2)));
        var gte = new CSharpGenerator().Generate(new GreaterThanOrEqual(new Constant(1), new Constant(2)));
        await Assert.That(lt).IsEqualTo("1 < 2;");
        await Assert.That(lte).IsEqualTo("1 <= 2;");
        await Assert.That(gt).IsEqualTo("1 > 2;");
        await Assert.That(gte).IsEqualTo("1 >= 2;");
    }

    [Test]
    public async Task Generate_AndOr_ProducesCorrectOperators() {
        var and = new CSharpGenerator().Generate(new And(new Constant(true), new Constant(false)));
        var or = new CSharpGenerator().Generate(new Or(new Constant(true), new Constant(false)));
        await Assert.That(and).IsEqualTo("true && false;");
        await Assert.That(or).IsEqualTo("true || false;");
    }

    [Test]
    public async Task Generate_Not_ProducesBangPrefix() {
        var result = new CSharpGenerator().Generate(new Not(new Constant(true)));
        await Assert.That(result).IsEqualTo("!true;");
    }

    [Test]
    public async Task Generate_UnaryMinus_ProducesNegativePrefix() {
        var result = new CSharpGenerator().Generate(new UnaryMinus(new Constant(5)));
        await Assert.That(result).IsEqualTo("-5;");
    }

    [Test]
    public async Task Generate_MemberAccess_ProducesDotNotation() {
        var node = new Member(new Variable("person"), "Name");
        var result = new CSharpGenerator().Generate(node);
        await Assert.That(result).IsEqualTo("person.Name;");
    }

    [Test]
    public async Task Generate_IndexAccess_ProducesBrackets() {
        var node = new IndexAccess(new Variable("arr"), new Constant(0));
        var result = new CSharpGenerator().Generate(node);
        await Assert.That(result).IsEqualTo("arr[0];");
    }

    [Test]
    public async Task Generate_Invoke_ProducesMethodCall() {
        var node = new Invoke(new Member(new Variable("obj"), "DoSomething"), new Constant(1), new Constant(2));
        var result = new CSharpGenerator().Generate(node);
        await Assert.That(result).IsEqualTo("obj.DoSomething(1, 2);");
    }

    [Test]
    public async Task Generate_New_ProducesConstructorCall() {
        var node = new New(new TypeReference("System.String"), new Constant("hello"));
        var result = new CSharpGenerator().Generate(node);
        await Assert.That(result).IsEqualTo("new System.String(\"hello\");");
    }

    [Test]
    public async Task Generate_Conditional_ProducesTernary() {
        var node = new Conditional(new Constant(true), new Constant(1), new Constant(2));
        var result = new CSharpGenerator().Generate(node);
        await Assert.That(result).IsEqualTo("(true ? 1 : 2);");
    }

    [Test]
    public async Task Generate_Coalesce_ProducesDoubleQuestion() {
        var node = new Coalesce(new Variable("a"), new Constant(0));
        var result = new CSharpGenerator().Generate(node);
        await Assert.That(result).IsEqualTo("a ?? 0;");
    }

    [Test]
    public async Task Generate_Assignment_ProducesEquals() {
        var node = new Assignment(new Variable("x"), new Constant(10));
        var result = new CSharpGenerator().Generate(node);
        await Assert.That(result).IsEqualTo("x = 10;");
    }

    [Test]
    public async Task Generate_TypeCast_ProducesCastExpression() {
        var node = new TypeCast(new Variable("x"), new TypeReference("System.Int32"));
        var result = new CSharpGenerator().Generate(node);
        await Assert.That(result).IsEqualTo("(System.Int32)x;");
    }

    [Test]
    public async Task Generate_TypeIs_ProducesIsExpression() {
        var node = new TypeIs(new Variable("x"), new TypeReference("System.String"));
        var result = new CSharpGenerator().Generate(node);
        await Assert.That(result).IsEqualTo("x is System.String;");
    }

    [Test]
    public async Task Generate_TypeAs_ProducesAsExpression() {
        var node = new TypeAs(new Variable("x"), new TypeReference("System.String"));
        var result = new CSharpGenerator().Generate(node);
        await Assert.That(result).IsEqualTo("x as System.String;");
    }

    [Test]
    public async Task Generate_Lambda_SingleParameter_ProducesLambdaExpression() {
        var param = new Parameter("x");
        var node = new Lambda([param], new Add(param, new Constant(1)));
        var result = new CSharpGenerator().Generate(node);
        await Assert.That(result).IsEqualTo("x => x + 1;");
    }

    [Test]
    public async Task Generate_Lambda_MultipleParameters_ProducesParenthesizedParams() {
        var x = new Parameter("x");
        var y = new Parameter("y");
        var node = new Lambda([x, y], new Add(x, y));
        var result = new CSharpGenerator().Generate(node);
        await Assert.That(result).IsEqualTo("(x, y) => x + y;");
    }

    [Test]
    public async Task Generate_Block_ProducesBracesWithStatements() {
        var node = new Block(new Constant(1), new Constant(2));
        var result = new CSharpGenerator().Generate(node);
        var expected = "{" + Environment.NewLine +
                       "    1;" + Environment.NewLine +
                       "    2;" + Environment.NewLine +
                       "}";
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task Generate_BlockWithVariables_DeclaresInsideBlock() {
        var v = new Variable("x");
        var node = new Block([new Assignment(v, new Constant(5))], [v]);
        var result = new CSharpGenerator().Generate(node);
        var expected = "{" + Environment.NewLine +
                       "    var x;" + Environment.NewLine +
                       "    x = 5;" + Environment.NewLine +
                       "}";
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task Generate_IfStatement_ProducesIfElseStructure() {
        var node = new IfStatement(
            new Equal(new Variable("x"), new Constant(0)),
            new Block(new Constant(1)),
            new Block(new Constant(2)));
        var result = new CSharpGenerator().Generate(node);
        var expected = "if (x == 0)" + Environment.NewLine +
                       "{" + Environment.NewLine +
                       "    1;" + Environment.NewLine +
                       "}" + Environment.NewLine +
                       "else" + Environment.NewLine +
                       "{" + Environment.NewLine +
                       "    2;" + Environment.NewLine +
                       "}";
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task Generate_IfStatementWithoutElse_ProducesIfOnly() {
        var node = new IfStatement(
            new Equal(new Variable("x"), new Constant(0)),
            new Block(new Constant(1)));
        var result = new CSharpGenerator().Generate(node);
        var expected = "if (x == 0)" + Environment.NewLine +
                       "{" + Environment.NewLine +
                       "    1;" + Environment.NewLine +
                       "}";
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task Generate_WhileLoop_ProducesWhileStructure() {
        var node = new WhileLoop(
            new LessThan(new Variable("i"), new Constant(10)),
            new Block(new Assignment(new Variable("i"), new Add(new Variable("i"), new Constant(1)))));
        var result = new CSharpGenerator().Generate(node);
        var expected = "while (i < 10)" + Environment.NewLine +
                       "{" + Environment.NewLine +
                       "    i = i + 1;" + Environment.NewLine +
                       "}";
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task Generate_DoWhileLoop_ProducesDoWhileStructure() {
        var node = new DoWhileLoop(
            new Block(new Assignment(new Variable("x"), new Add(new Variable("x"), new Constant(1)))),
            new LessThan(new Variable("x"), new Constant(5)));
        var result = new CSharpGenerator().Generate(node);
        var expected = "do" + Environment.NewLine +
                       "{" + Environment.NewLine +
                       "    x = x + 1;" + Environment.NewLine +
                       "}" + Environment.NewLine +
                       "while (x < 5);";
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task Generate_ForLoop_ProducesForStructure() {
        var node = new ForLoop(
            new Assignment(new Variable("i"), new Constant(0)),
            new LessThan(new Variable("i"), new Constant(10)),
            new Assignment(new Variable("i"), new Add(new Variable("i"), new Constant(1))),
            new Block(new Constant(42)));
        var result = new CSharpGenerator().Generate(node);
        var expected = "for (i = 0; i < 10; i = i + 1)" + Environment.NewLine +
                       "{" + Environment.NewLine +
                       "    42;" + Environment.NewLine +
                       "}";
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task Generate_ForLoopWithNullParts_OmitsEmptyParts() {
        var node = new ForLoop(null, null, null, new Block(new Constant(42)));
        var result = new CSharpGenerator().Generate(node);
        var expected = "for (; ; )" + Environment.NewLine +
                       "{" + Environment.NewLine +
                       "    42;" + Environment.NewLine +
                       "}";
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task Generate_ForEachLoop_ProducesForeachStructure() {
        var item = new Variable("item");
        var node = new ForEachLoop(item, new Variable("items"), new Block(new Constant(1)));
        var result = new CSharpGenerator().Generate(node);
        var expected = "foreach (var item in items)" + Environment.NewLine +
                       "{" + Environment.NewLine +
                       "    1;" + Environment.NewLine +
                       "}";
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task Generate_BreakStatement_ProducesBreak() {
        var result = new CSharpGenerator().Generate(new BreakStatement());
        await Assert.That(result).IsEqualTo("break;");
    }

    [Test]
    public async Task Generate_ContinueStatement_ProducesContinue() {
        var result = new CSharpGenerator().Generate(new ContinueStatement());
        await Assert.That(result).IsEqualTo("continue;");
    }

    [Test]
    public async Task Generate_ReturnWithValue_ProducesReturn() {
        var result = new CSharpGenerator().Generate(new Return(new Constant(42)));
        await Assert.That(result).IsEqualTo("return 42;");
    }

    [Test]
    public async Task Generate_ReturnVoid_ProducesReturnSemicolon() {
        var result = new CSharpGenerator().Generate(Return.Void);
        await Assert.That(result).IsEqualTo("return;");
    }

    [Test]
    public async Task Generate_Throw_ProducesThrow() {
        var result = new CSharpGenerator().Generate(new ThrowStatement(new New(new TypeReference("System.Exception"))));
        await Assert.That(result).IsEqualTo("throw new System.Exception();");
    }

    [Test]
    public async Task Generate_TryCatchFinally_ProducesFullStructure() {
        var node = new TryCatchFinally(
            new Block(new Constant(1)),
            [new CatchClause(new TypeReference("System.Exception"), "ex", new Block(new Constant(2)))],
            new Block(new Constant(3)));
        var result = new CSharpGenerator().Generate(node);
        var expected = "try" + Environment.NewLine +
                       "{" + Environment.NewLine +
                       "    1;" + Environment.NewLine +
                       "}" + Environment.NewLine +
                       "catch (System.Exception ex)" + Environment.NewLine +
                       "{" + Environment.NewLine +
                       "    2;" + Environment.NewLine +
                       "}" + Environment.NewLine +
                       "finally" + Environment.NewLine +
                       "{" + Environment.NewLine +
                       "    3;" + Environment.NewLine +
                       "}";
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task Generate_TryFinally_WithoutCatch_ProducesTryFinally() {
        var node = new TryCatchFinally(
            new Block(new Constant(1)),
            FinallyBlock: new Block(new Constant(2)));
        var result = new CSharpGenerator().Generate(node);
        var expected = "try" + Environment.NewLine +
                       "{" + Environment.NewLine +
                       "    1;" + Environment.NewLine +
                       "}" + Environment.NewLine +
                       "finally" + Environment.NewLine +
                       "{" + Environment.NewLine +
                       "    2;" + Environment.NewLine +
                       "}";
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task Generate_UsingStatement_ProducesUsing() {
        var node = new UsingStatement(
            new Variable("resource"),
            new Block(new Constant(1)));
        var result = new CSharpGenerator().Generate(node);
        var expected = "using (resource)" + Environment.NewLine +
                       "{" + Environment.NewLine +
                       "    1;" + Environment.NewLine +
                       "}";
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task Generate_SwitchStatement_ProducesSwitchStructure() {
        var node = new SwitchStatement(
            new Variable("x"),
            [new SwitchCase(new Constant(1), new Block(new Return(new Constant(10)))),
             new SwitchCase(new Constant(2), new Block(new Return(new Constant(20))))],
            new Block(new Return(new Constant(0))));
        var result = new CSharpGenerator().Generate(node);
        var expected = "switch (x)" + Environment.NewLine +
                       "{" + Environment.NewLine +
                       "    case 1:" + Environment.NewLine +
                       "    {" + Environment.NewLine +
                       "        return 10;" + Environment.NewLine +
                       "    }" + Environment.NewLine +
                       "    case 2:" + Environment.NewLine +
                       "    {" + Environment.NewLine +
                       "        return 20;" + Environment.NewLine +
                       "    }" + Environment.NewLine +
                       "    default:" + Environment.NewLine +
                       "    {" + Environment.NewLine +
                       "        return 0;" + Environment.NewLine +
                       "    }" + Environment.NewLine +
                       "}";
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task Generate_GotoStatement_ProducesGoto() {
        var result = new CSharpGenerator().Generate(new GotoStatement("exit"));
        await Assert.That(result).IsEqualTo("goto exit;");
    }

    [Test]
    public async Task Generate_LabelDeclaration_ProducesLabel() {
        var node = new LabelDeclaration("exit", new Return(new Constant(0)));
        var result = new CSharpGenerator().Generate(node);
        var expected = "exit:" + Environment.NewLine +
                       "return 0;";
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task Generate_Modulo_ProducesPercent() {
        var result = new CSharpGenerator().Generate(new Modulo(new Constant(10), new Constant(3)));
        await Assert.That(result).IsEqualTo("10 % 3;");
    }

    [Test]
    public async Task Generate_TypeDefinition_WithBaseTypeAndInterfaces_WritesFullLineage() {
        var node = new TypeDefinitionNode(
            Name: "Widget",
            BaseType: new NamedTypeReference("BaseWidget"),
            Interfaces: [
                new NamedTypeReference("IDisposable"),
                new NamedTypeReference("IComparable", TypeArguments: [new NamedTypeReference("Widget")])
            ]);

        var result = new CSharpGenerator().Generate(node);

        await Assert.That(result).IsEqualTo(
            "public class Widget : BaseWidget, IDisposable, IComparable<Widget>" + Environment.NewLine +
            "{" + Environment.NewLine +
            "}");
    }

    [Test]
    public async Task Generate_TypeDefinition_WithOnlyInterfaces_WritesInterfaceLineage() {
        var node = new TypeDefinitionNode(
            Name: "Widget",
            Interfaces: [
                new NamedTypeReference("IDisposable"),
                new NamedTypeReference("IEnumerable", TypeArguments: [new PrimitiveTypeReference(PrimitiveType.String)])
            ]);

        var result = new CSharpGenerator().Generate(node);

        await Assert.That(result).IsEqualTo(
            "public class Widget : IDisposable, IEnumerable<String>" + Environment.NewLine +
            "{" + Environment.NewLine +
            "}");
    }

    [Test]
    public async Task Generate_NestedInvocation_ProducesChainedMethodCall() {
        var node = new Invoke(
            new Member(new Invoke(
                new Member(new Variable("obj"), "GetService"),
                new Constant("logger")), "Log"),
            new Constant("hello"));
        var result = new CSharpGenerator().Generate(node);
        await Assert.That(result).IsEqualTo("obj.GetService(\"logger\").Log(\"hello\");");
    }

    [Test]
    public async Task Generate_ComplexExpression_CombinesMultipleOperators() {
        var node = new Add(
            new Multiply(new Constant(3), new Constant(4)),
            new Divide(new Constant(10), new Constant(2)));
        var result = new CSharpGenerator().Generate(node);
        await Assert.That(result).IsEqualTo("3 * 4 + 10 / 2;");
    }

    [Test]
    public async Task Generate_EmptyGenerator_DoesNotThrow() {
        var generator = new CSharpGenerator();
        var result = generator.Generate(new Constant(1));
        await Assert.That(result).IsEqualTo("1;");
    }
}