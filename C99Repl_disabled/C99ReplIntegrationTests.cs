using Poly.Interpretation;

namespace Poly.Tests.Interpretation.C99Repl;

public class C99ReplIntegrationTests
{
    // ===== BASIC ARITHMETIC =====

    [Test]
    public async Task Evaluate_SimpleLiteral_ReturnsValue()
    {
        var repl = new C99ReplEngine();
        var result = repl.Evaluate<int>("42;");
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Evaluate_SimpleAddition_ReturnsSum()
    {
        var repl = new C99ReplEngine();
        var result = repl.Evaluate<int>("10 + 20;");
        await Assert.That(result).IsEqualTo(30);
    }

    [Test]
    public async Task Evaluate_SimpleSubtraction_ReturnsDifference()
    {
        var repl = new C99ReplEngine();
        var result = repl.Evaluate<int>("50 - 15;");
        await Assert.That(result).IsEqualTo(35);
    }

    [Test]
    public async Task Evaluate_SimpleMultiplication_ReturnsProduct()
    {
        var repl = new C99ReplEngine();
        var result = repl.Evaluate<int>("6 * 7;");
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Evaluate_SimpleDivision_ReturnsQuotient()
    {
        var repl = new C99ReplEngine();
        var result = repl.Evaluate<int>("100 / 4;");
        await Assert.That(result).IsEqualTo(25);
    }

    [Test]
    public async Task Evaluate_ModuloOperation_ReturnRemainder()
    {
        var repl = new C99ReplEngine();
        var result = repl.Evaluate<int>("17 % 5;");
        await Assert.That(result).IsEqualTo(2);
    }

    [Test]
    public async Task Evaluate_ComplexArithmetic_ReturnsCorrectResult()
    {
        var repl = new C99ReplEngine();
        var result = repl.Evaluate<int>("2 + 3 * 4 - 1;");  // 2 + 12 - 1 = 13
        await Assert.That(result).IsEqualTo(13);
    }

    [Test]
    public async Task Evaluate_ParenthesizedExpression_ReturnsCorrectResult()
    {
        var repl = new C99ReplEngine();
        var result = repl.Evaluate<int>("(2 + 3) * 4;");  // 5 * 4 = 20
        await Assert.That(result).IsEqualTo(20);
    }

    [Test]
    public async Task Evaluate_UnaryMinus_ReturnsNegation()
    {
        var repl = new C99ReplEngine();
        var result = repl.Evaluate<int>("-42;");
        await Assert.That(result).IsEqualTo(-42);
    }

    // ===== VARIABLES AND ASSIGNMENT =====

    [Test]
    public async Task Evaluate_IntVariableDeclaration_ReturnsInitialValue()
    {
        var repl = new C99ReplEngine();
        var code = @"
            int x = 42;
            x;
        ";
        var result = repl.Evaluate<int>(code);
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Evaluate_MultipleVariables_ReturnsLastValue()
    {
        var repl = new C99ReplEngine();
        var code = @"
            int x = 10;
            int y = 20;
            y;
        ";
        var result = repl.Evaluate<int>(code);
        await Assert.That(result).IsEqualTo(20);
    }

    [Test]
    public async Task Evaluate_VariableAssignment_UpdatesValue()
    {
        var repl = new C99ReplEngine();
        var code = @"
            int x = 10;
            x = 25;
            x;
        ";
        var result = repl.Evaluate<int>(code);
        await Assert.That(result).IsEqualTo(25);
    }

    [Test]
    public async Task Evaluate_VariableInExpression_ReturnsCorrectResult()
    {
        var repl = new C99ReplEngine();
        var code = @"
            int x = 10;
            int y = 20;
            x + y;
        ";
        var result = repl.Evaluate<int>(code);
        await Assert.That(result).IsEqualTo(30);
    }

    [Test]
    public async Task Evaluate_VariableReassignment_ReturnsUpdatedValue()
    {
        var repl = new C99ReplEngine();
        var code = @"
            int x = 10;
            x = x + 5;
            x = x * 2;
            x;
        ";
        var result = repl.Evaluate<int>(code);
        await Assert.That(result).IsEqualTo(30);  // (10 + 5) * 2 = 30
    }

    // ===== COMPARISON OPERATIONS =====

    [Test]
    public async Task Evaluate_EqualComparison_ReturnsTrue()
    {
        var repl = new C99ReplEngine();
        var result = repl.Evaluate<bool>("10 == 10;");
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Evaluate_EqualComparison_ReturnsFalse()
    {
        var repl = new C99ReplEngine();
        var result = repl.Evaluate<bool>("10 == 20;");
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task Evaluate_NotEqualComparison_ReturnsTrue()
    {
        var repl = new C99ReplEngine();
        var result = repl.Evaluate<bool>("10 != 20;");
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Evaluate_LessThanComparison_ReturnsTrue()
    {
        var repl = new C99ReplEngine();
        var result = repl.Evaluate<bool>("10 < 20;");
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Evaluate_GreaterThanComparison_ReturnsTrue()
    {
        var repl = new C99ReplEngine();
        var result = repl.Evaluate<bool>("20 > 10;");
        await Assert.That(result).IsTrue();
    }

    // ===== LOGICAL OPERATIONS =====

    [Test]
    public async Task Evaluate_LogicalAnd_ReturnsTrue()
    {
        var repl = new C99ReplEngine();
        var result = repl.Evaluate<bool>("1 && 1;");
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Evaluate_LogicalAnd_ReturnsFalse()
    {
        var repl = new C99ReplEngine();
        var result = repl.Evaluate<bool>("1 && 0;");
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task Evaluate_LogicalOr_ReturnsTrue()
    {
        var repl = new C99ReplEngine();
        var result = repl.Evaluate<bool>("1 || 0;");
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Evaluate_LogicalNot_ReturnsTrue()
    {
        var repl = new C99ReplEngine();
        var result = repl.Evaluate<bool>("!0;");
        await Assert.That(result).IsTrue();
    }

    // ===== CONDITIONAL OPERATOR (TERNARY) =====

    [Test]
    public async Task Evaluate_TernaryOperator_ReturnsIfTrueValue()
    {
        var repl = new C99ReplEngine();
        var result = repl.Evaluate<int>("1 ? 42 : 0;");
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Evaluate_TernaryOperator_ReturnsIfFalseValue()
    {
        var repl = new C99ReplEngine();
        var result = repl.Evaluate<int>("0 ? 42 : 99;");
        await Assert.That(result).IsEqualTo(99);
    }

    [Test]
    public async Task Evaluate_TernaryWithComparison_ReturnsCorrectValue()
    {
        var repl = new C99ReplEngine();
        var result = repl.Evaluate<int>("10 > 5 ? 100 : 200;");
        await Assert.That(result).IsEqualTo(100);
    }

    // ===== IF-ELSE STATEMENTS =====

    [Test]
    public async Task Evaluate_IfStatementTrue_ExecutesThenBranch()
    {
        var repl = new C99ReplEngine();
        var code = @"
            int x = 0;
            if (1) {
                x = 42;
            }
            x;
        ";
        var result = repl.Evaluate<int>(code);
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Evaluate_IfStatementFalse_SkipsThenBranch()
    {
        var repl = new C99ReplEngine();
        var code = @"
            int x = 42;
            if (0) {
                x = 99;
            }
            x;
        ";
        var result = repl.Evaluate<int>(code);
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Evaluate_IfElseStatementTrue_ExecutesThenBranch()
    {
        var repl = new C99ReplEngine();
        var code = @"
            int x = 0;
            if (1) {
                x = 42;
            } else {
                x = 99;
            }
            x;
        ";
        var result = repl.Evaluate<int>(code);
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Evaluate_IfElseStatementFalse_ExecutesElseBranch()
    {
        var repl = new C99ReplEngine();
        var code = @"
            int x = 0;
            if (0) {
                x = 42;
            } else {
                x = 99;
            }
            x;
        ";
        var result = repl.Evaluate<int>(code);
        await Assert.That(result).IsEqualTo(99);
    }

    [Test]
    public async Task Evaluate_NestedIfElse_ReturnsCorrectValue()
    {
        var repl = new C99ReplEngine();
        var code = @"
            int x = 10;
            int result = 0;
            if (x > 20) {
                result = 1;
            } else {
                if (x > 5) {
                    result = 2;
                } else {
                    result = 3;
                }
            }
            result;
        ";
        var result = repl.Evaluate<int>(code);
        await Assert.That(result).IsEqualTo(2);
    }

    // ===== LEXER TESTS =====

    [Test]
    public async Task Lexer_SimpleExpression_ProducesCorrectTokens()
    {
        var repl = new C99ReplEngine();
        var tokens = repl.Tokenize("10 + 20;");
        
        await Assert.That(tokens.Count()).IsGreaterThan(0);
        await Assert.That(tokens[0].Type).IsEqualTo(TokenType.IntLiteral);
        await Assert.That(tokens[0].Value).IsEqualTo("10");
        await Assert.That(tokens[1].Type).IsEqualTo(TokenType.Plus);
    }

    [Test]
    public async Task Lexer_WithComments_SkipsComments()
    {
        var repl = new C99ReplEngine();
        var code = @"
            // This is a comment
            int x = 42; // end of line comment
            /* multi-line
               comment */
            x;
        ";
        var tokens = repl.Tokenize(code);
        var nonEofTokens = tokens.TakeWhile(t => t.Type != TokenType.EOF).ToList();
        await Assert.That(nonEofTokens.Any(t => t.Value.Contains("comment"))).IsFalse();
    }

    // ===== PARSER TESTS =====

    [Test]
    public async Task Parser_SimpleExpression_ProducesAst()
    {
        var repl = new C99ReplEngine();
        var ast = repl.Parse("10 + 20;");
        
        await Assert.That(ast).IsNotNull();
        await Assert.That(ast.Statements.Count()).IsGreaterThan(0);
    }

    [Test]
    public async Task Parser_VariableDeclaration_CreatesCorrectAst()
    {
        var repl = new C99ReplEngine();
        var ast = repl.Parse("int x = 42;");
        
        var stmt = ast.Statements[0];
        await Assert.That(stmt is VariableDeclaration).IsTrue();
        var varDecl = (VariableDeclaration)stmt;
        await Assert.That(varDecl.Name).IsEqualTo("x");
        await Assert.That(varDecl.Type).IsEqualTo("int");
    }
}
