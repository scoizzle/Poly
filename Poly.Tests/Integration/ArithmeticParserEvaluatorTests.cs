using System.Linq.Expressions;
using System.Text.RegularExpressions;

using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.AbstractSyntaxTree.Arithmetic;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Integration;

/// <summary>
/// Integration tests for a complete arithmetic expression lexer/parser/evaluator pipeline.
/// Demonstrates the full flow: text -> tokens -> AST -> evaluation.
/// </summary>
public class ArithmeticParserEvaluatorTests {
    /// <summary>
    /// Simple token types for arithmetic expressions.
    /// </summary>
    private enum TokenType {
        Number,
        Plus,
        Minus,
        Multiply,
        Divide,
        Modulo,
        LeftParen,
        RightParen,
        End
    }

    /// <summary>
    /// Represents a single token in the input stream.
    /// </summary>
    private record Token(TokenType Type, string Value, int Position);

    /// <summary>
    /// Simple lexer for arithmetic expressions.
    /// Converts input text into a stream of tokens.
    /// </summary>
    private class ArithmeticLexer {
        private readonly string _input;
        private int _position;

        public ArithmeticLexer(string input)
        {
            _input = input;
            _position = 0;
        }

        public List<Token> Tokenize()
        {
            var tokens = new List<Token>();

            while (_position < _input.Length) {
                // Skip whitespace
                if (char.IsWhiteSpace(_input[_position])) {
                    _position++;
                    continue;
                }

                // Numbers (including decimals)
                if (char.IsDigit(_input[_position]) || _input[_position] == '.') {
                    var start = _position;
                    while (_position < _input.Length &&
                           (char.IsDigit(_input[_position]) || _input[_position] == '.')) {
                        _position++;
                    }
                    tokens.Add(new Token(TokenType.Number, _input[start.._position], start));
                    continue;
                }

                // Operators and parentheses
                var tokenType = _input[_position] switch {
                    '+' => TokenType.Plus,
                    '-' => TokenType.Minus,
                    '*' => TokenType.Multiply,
                    '/' => TokenType.Divide,
                    '%' => TokenType.Modulo,
                    '(' => TokenType.LeftParen,
                    ')' => TokenType.RightParen,
                    _ => throw new InvalidOperationException($"Unexpected character '{_input[_position]}' at position {_position}")
                };

                tokens.Add(new Token(tokenType, _input[_position].ToString(), _position));
                _position++;
            }

            tokens.Add(new Token(TokenType.End, string.Empty, _position));
            return tokens;
        }
    }

    /// <summary>
    /// Recursive descent parser for arithmetic expressions.
    /// Builds an AST from tokens with proper operator precedence.
    /// 
    /// Grammar:
    /// expression := term (('+' | '-') term)*
    /// term       := factor (('*' | '/' | '%') factor)*
    /// factor     := number | '(' expression ')'
    /// </summary>
    private class ArithmeticParser {
        private readonly List<Token> _tokens;
        private int _current;

        public ArithmeticParser(List<Token> tokens)
        {
            _tokens = tokens;
            _current = 0;
        }

        public Node Parse()
        {
            var result = ParseExpression();

            if (_tokens[_current].Type != TokenType.End) {
                throw new InvalidOperationException($"Unexpected token '{_tokens[_current].Value}' at position {_tokens[_current].Position}");
            }

            return result;
        }

        private Node ParseExpression()
        {
            var left = ParseTerm();

            while (_tokens[_current].Type is TokenType.Plus or TokenType.Minus) {
                var op = _tokens[_current].Type;
                _current++;
                var right = ParseTerm();

                left = op == TokenType.Plus
                    ? new Add(left, right)
                    : new Subtract(left, right);
            }

            return left;
        }

        private Node ParseTerm()
        {
            var left = ParseFactor();

            while (_tokens[_current].Type is TokenType.Multiply or TokenType.Divide or TokenType.Modulo) {
                var op = _tokens[_current].Type;
                _current++;
                var right = ParseFactor();

                left = op switch {
                    TokenType.Multiply => new Multiply(left, right),
                    TokenType.Divide => new Divide(left, right),
                    TokenType.Modulo => new Modulo(left, right),
                    _ => throw new InvalidOperationException($"Unexpected operator {op}")
                };
            }

            return left;
        }

        private Node ParseFactor()
        {
            var token = _tokens[_current];

            // Handle parentheses
            if (token.Type == TokenType.LeftParen) {
                _current++; // consume '('
                var expr = ParseExpression();

                if (_tokens[_current].Type != TokenType.RightParen) {
                    throw new InvalidOperationException($"Expected ')' at position {_tokens[_current].Position}");
                }

                _current++; // consume ')'
                return expr;
            }

            // Handle numbers
            if (token.Type == TokenType.Number) {
                _current++;

                // Parse as double if it contains a decimal point, otherwise int
                if (token.Value.Contains('.')) {
                    return new Constant(double.Parse(token.Value));
                }
                else {
                    return new Constant(int.Parse(token.Value));
                }
            }

            // Handle unary minus
            if (token.Type == TokenType.Minus) {
                _current++;
                return new UnaryMinus(ParseFactor());
            }

            throw new InvalidOperationException($"Unexpected token '{token.Value}' at position {token.Position}");
        }
    }

    /// <summary>
    /// Helper method to evaluate an arithmetic expression string.
    /// </summary>
    private static T Evaluate<T>(string expression)
    {
        // Lex
        var lexer = new ArithmeticLexer(expression);
        var tokens = lexer.Tokenize();

        // Parse
        var parser = new ArithmeticParser(tokens);
        var ast = parser.Parse();

        // Compile and evaluate
        var expr = ast.BuildExpression();
        var lambda = Expression.Lambda<Func<T>>(expr);
        return lambda.Compile()();
    }

    [Test]
    public async Task SimpleAddition_TwoPlusThree_ReturnsFive()
    {
        var result = Evaluate<int>("2 + 3");
        await Assert.That(result).IsEqualTo(5);
    }

    [Test]
    public async Task SimpleSubtraction_TenMinusFour_ReturnsSix()
    {
        var result = Evaluate<int>("10 - 4");
        await Assert.That(result).IsEqualTo(6);
    }

    [Test]
    public async Task SimpleMultiplication_ThreeTimesFour_ReturnsTwelve()
    {
        var result = Evaluate<int>("3 * 4");
        await Assert.That(result).IsEqualTo(12);
    }

    [Test]
    public async Task SimpleDivision_TwentyDividedByFive_ReturnsFour()
    {
        var result = Evaluate<int>("20 / 5");
        await Assert.That(result).IsEqualTo(4);
    }

    [Test]
    public async Task SimpleModulo_TenModThree_ReturnsOne()
    {
        var result = Evaluate<int>("10 % 3");
        await Assert.That(result).IsEqualTo(1);
    }

    [Test]
    public async Task OperatorPrecedence_AdditionAndMultiplication_MultipliesFirst()
    {
        // 2 + 3 * 4 = 2 + 12 = 14
        var result = Evaluate<int>("2 + 3 * 4");
        await Assert.That(result).IsEqualTo(14);
    }

    [Test]
    public async Task OperatorPrecedence_SubtractionAndDivision_DividesFirst()
    {
        // 20 - 10 / 2 = 20 - 5 = 15
        var result = Evaluate<int>("20 - 10 / 2");
        await Assert.That(result).IsEqualTo(15);
    }

    [Test]
    public async Task Parentheses_OverridesPrecedence_AddsFirst()
    {
        // (2 + 3) * 4 = 5 * 4 = 20
        var result = Evaluate<int>("(2 + 3) * 4");
        await Assert.That(result).IsEqualTo(20);
    }

    [Test]
    public async Task NestedParentheses_ComplexExpression_EvaluatesCorrectly()
    {
        // ((2 + 3) * (4 + 1)) - 10 = (5 * 5) - 10 = 25 - 10 = 15
        var result = Evaluate<int>("((2 + 3) * (4 + 1)) - 10");
        await Assert.That(result).IsEqualTo(15);
    }

    [Test]
    public async Task ComplexExpression_MultipleOperators_EvaluatesCorrectly()
    {
        // 10 + 5 * 2 - 8 / 4 = 10 + 10 - 2 = 18
        var result = Evaluate<int>("10 + 5 * 2 - 8 / 4");
        await Assert.That(result).IsEqualTo(18);
    }

    [Test]
    public async Task UnaryMinus_NegativeNumber_ReturnsNegative()
    {
        var result = Evaluate<int>("-5");
        await Assert.That(result).IsEqualTo(-5);
    }

    [Test]
    public async Task UnaryMinus_InExpression_EvaluatesCorrectly()
    {
        // 10 + -5 = 10 + (-5) = 5
        var result = Evaluate<int>("10 + -5");
        await Assert.That(result).IsEqualTo(5);
    }

    [Test]
    public async Task UnaryMinus_WithParentheses_EvaluatesCorrectly()
    {
        // -(10 + 5) = -(15) = -15
        var result = Evaluate<int>("-(10 + 5)");
        await Assert.That(result).IsEqualTo(-15);
    }

    [Test]
    public async Task DecimalNumbers_Addition_ReturnsDouble()
    {
        var result = Evaluate<double>("2.5 + 3.7");
        await Assert.That(result).IsEqualTo(6.2);
    }

    [Test]
    public async Task MixedTypes_IntAndDouble_PromotesToDouble()
    {
        // 10 + 2.5 = 12.5 (type promotion handled by code generator)
        var result = Evaluate<double>("10 + 2.5");
        await Assert.That(result).IsEqualTo(12.5);
    }

    [Test]
    public async Task MixedTypes_ComplexExpression_PromotesCorrectly()
    {
        // 10 * 2.5 - 5 / 2.0 = 25.0 - 2.5 = 22.5
        var result = Evaluate<double>("10 * 2.5 - 5 / 2.0");
        await Assert.That(result).IsEqualTo(22.5);
    }

    [Test]
    public async Task LongExpression_ManyOperations_EvaluatesCorrectly()
    {
        // 1 + 2 * 3 - 4 / 2 + 5 * (6 - 2) = 1 + 6 - 2 + 5 * 4 = 1 + 6 - 2 + 20 = 25
        var result = Evaluate<int>("1 + 2 * 3 - 4 / 2 + 5 * (6 - 2)");
        await Assert.That(result).IsEqualTo(25);
    }

    [Test]
    public async Task WhitespaceHandling_VariousSpacing_ParsesCorrectly()
    {
        var result1 = Evaluate<int>("2+3*4");
        var result2 = Evaluate<int>("2 + 3 * 4");
        var result3 = Evaluate<int>("  2  +  3  *  4  ");

        await Assert.That(result1).IsEqualTo(14);
        await Assert.That(result2).IsEqualTo(14);
        await Assert.That(result3).IsEqualTo(14);
    }

    [Test]
    public async Task InvalidSyntax_MissingRightParen_ThrowsException()
    {
        await Assert.That(() => Evaluate<int>("(2 + 3"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task InvalidSyntax_UnexpectedToken_ThrowsException()
    {
        await Assert.That(() => Evaluate<int>("2 + + 3"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task InvalidSyntax_InvalidCharacter_ThrowsException()
    {
        await Assert.That(() => Evaluate<int>("2 + @ 3"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ZeroValues_Operations_HandlesCorrectly()
    {
        await Assert.That(Evaluate<int>("0 + 5")).IsEqualTo(5);
        await Assert.That(Evaluate<int>("5 - 0")).IsEqualTo(5);
        await Assert.That(Evaluate<int>("0 * 5")).IsEqualTo(0);
        await Assert.That(Evaluate<int>("0 / 5")).IsEqualTo(0);
    }

    [Test]
    public async Task LargeNumbers_Addition_HandlesCorrectly()
    {
        var result = Evaluate<int>("1000000 + 2000000");
        await Assert.That(result).IsEqualTo(3000000);
    }

    [Test]
    public async Task ChainedOperations_LeftToRight_EvaluatesCorrectly()
    {
        // 10 - 5 - 2 = (10 - 5) - 2 = 5 - 2 = 3
        var result = Evaluate<int>("10 - 5 - 2");
        await Assert.That(result).IsEqualTo(3);
    }

    [Test]
    public async Task ChainedMultiplication_LeftToRight_EvaluatesCorrectly()
    {
        // 2 * 3 * 4 = (2 * 3) * 4 = 6 * 4 = 24
        var result = Evaluate<int>("2 * 3 * 4");
        await Assert.That(result).IsEqualTo(24);
    }
}