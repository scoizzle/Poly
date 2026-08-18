using Poly.Grammar;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Integration;

/// <summary>
/// Integration tests for a complete arithmetic expression lexer/parser/evaluator pipeline.
/// Demonstrates the full flow: text -> tokens -> AST -> evaluation.
/// </summary>
public class ArithmeticParserEvaluatorTests {

    private readonly record struct EvaluationOutcome<T>(bool IsSuccess, T? Value, Exception? Exception);

    /// <summary>
    /// Token kinds for arithmetic expressions, used with the engine's
    /// <see cref="BufferedTokenReader{TToken,TTokenKind}"/>.
    /// </summary>
    private enum ArithKind {
        Number, Plus, Minus, Star, Slash, Percent, LParen, RParen, End
    }

    private readonly record struct ArithToken(ArithKind Kind, string Text) : IToken<ArithKind>;

    /// <summary>
    /// Token reader for arithmetic expressions, built on the buffered reader
    /// base with local char state (the engine owns buffering + position).
    /// </summary>
    private sealed class ArithTokenReader : BufferedTokenReader<ArithToken, ArithKind> {
        private readonly string _text;
        private int _pos;

        public ArithTokenReader(string text) => _text = text;

        public override bool EndOfStream(ArithKind kind) => kind == ArithKind.End;

        protected override ArithToken ScanNextToken() {
            SkipWhitespace();
            if (_pos >= _text.Length)
                return new ArithToken(ArithKind.End, "");

            var c = _text[_pos];
            if (char.IsDigit(c) || c == '.')
                return ScanNumber();

            _pos++;
            var kind = c switch {
                '+' => ArithKind.Plus,
                '-' => ArithKind.Minus,
                '*' => ArithKind.Star,
                '/' => ArithKind.Slash,
                '%' => ArithKind.Percent,
                '(' => ArithKind.LParen,
                ')' => ArithKind.RParen,
                _ => throw new InvalidOperationException($"Unexpected character '{c}'")
            };
            return new ArithToken(kind, c.ToString());
        }

        private ArithToken ScanNumber() {
            var start = _pos;
            while (_pos < _text.Length && (char.IsDigit(_text[_pos]) || _text[_pos] == '.'))
                _pos++;
            return new ArithToken(ArithKind.Number, _text[start.._pos]);
        }

        private void SkipWhitespace() {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
                _pos++;
        }
    }

    /// <summary>
    /// Recursive descent parser for arithmetic expressions, consuming tokens
    /// from the reader (Peek(0) = head; Next() = consume one).
    /// 
    /// Grammar:
    /// expression := term (('+' | '-') term)*
    /// term       := factor (('*' | '/' | '%') factor)*
    /// factor     := number | '(' expression ')' | '-' factor
    /// </summary>
    private sealed class ArithmeticParser {
        private readonly ArithTokenReader _reader;

        public ArithmeticParser(ArithTokenReader reader) {
            _reader = reader;
        }

        private ArithToken Next() {
            var t = _reader.Peek(0);
            _reader.Consume(1);
            return t;
        }

        public Node Parse() {
            var result = ParseExpression();

            var end = _reader.Peek(0);
            if (end.Kind != ArithKind.End)
                throw new InvalidOperationException($"Unexpected token '{end.Text}'");

            return result;
        }

        private Node ParseExpression() {
            var left = ParseTerm();

            while (_reader.Peek(0).Kind is ArithKind.Plus or ArithKind.Minus) {
                var op = Next().Kind;
                var right = ParseTerm();
                left = op == ArithKind.Plus
                    ? new Add(left, right)
                    : new Subtract(left, right);
            }

            return left;
        }

        private Node ParseTerm() {
            var left = ParseFactor();

            while (_reader.Peek(0).Kind is ArithKind.Star or ArithKind.Slash or ArithKind.Percent) {
                var op = Next().Kind;
                var right = ParseFactor();
                left = op switch {
                    ArithKind.Star => new Multiply(left, right),
                    ArithKind.Slash => new Divide(left, right),
                    ArithKind.Percent => new Modulo(left, right),
                    _ => throw new InvalidOperationException($"Unexpected operator {op}")
                };
            }

            return left;
        }

        private Node ParseFactor() {
            var token = _reader.Peek(0);

            // Handle parentheses
            if (token.Kind == ArithKind.LParen) {
                Next(); // consume '('
                var expr = ParseExpression();

                var close = Next();
                if (close.Kind != ArithKind.RParen)
                    throw new InvalidOperationException($"Expected ')'");

                return expr;
            }

            // Handle numbers
            if (token.Kind == ArithKind.Number) {
                Next();

                // Parse as double if it contains a decimal point, otherwise int
                if (token.Text.Contains('.'))
                    return new Constant(double.Parse(token.Text));
                return new Constant(int.Parse(token.Text));
            }

            // Handle unary minus
            if (token.Kind == ArithKind.Minus) {
                Next();
                return new UnaryMinus(ParseFactor());
            }

            throw new InvalidOperationException($"Unexpected token '{token.Text}'");
        }
    }

    /// <summary>
    /// Helper method to evaluate an arithmetic expression string.
    /// </summary>
    private static T Evaluate<T>(string expression) {
        var ast = ParseAst(expression);
        return EvaluateWithLinq<T>(ast);
    }

    private static T EvaluateWithLinq<T>(Node ast) {
        var expr = ast.BuildExpression();
        var lambda = Expression.Lambda<Func<T>>(expr);
        return lambda.Compile()();
    }

    private static Node ParseAst(string expression) {
        var reader = new ArithTokenReader(expression);
        var parser = new ArithmeticParser(reader);
        return parser.Parse();
    }

    [Test]
    public async Task SimpleAddition_TwoPlusThree_ReturnsFive() {
        var result = Evaluate<int>("2 + 3");
        await Assert.That(result).IsEqualTo(5);
    }

    [Test]
    public async Task SimpleSubtraction_TenMinusFour_ReturnsSix() {
        var result = Evaluate<int>("10 - 4");
        await Assert.That(result).IsEqualTo(6);
    }

    [Test]
    public async Task SimpleMultiplication_ThreeTimesFour_ReturnsTwelve() {
        var result = Evaluate<int>("3 * 4");
        await Assert.That(result).IsEqualTo(12);
    }

    [Test]
    public async Task SimpleDivision_TwentyDividedByFive_ReturnsFour() {
        var result = Evaluate<int>("20 / 5");
        await Assert.That(result).IsEqualTo(4);
    }

    [Test]
    public async Task SimpleModulo_TenModThree_ReturnsOne() {
        var result = Evaluate<int>("10 % 3");
        await Assert.That(result).IsEqualTo(1);
    }

    [Test]
    public async Task OperatorPrecedence_AdditionAndMultiplication_MultipliesFirst() {
        // 2 + 3 * 4 = 2 + 12 = 14
        var result = Evaluate<int>("2 + 3 * 4");
        await Assert.That(result).IsEqualTo(14);
    }

    [Test]
    public async Task OperatorPrecedence_SubtractionAndDivision_DividesFirst() {
        // 20 - 10 / 2 = 20 - 5 = 15
        var result = Evaluate<int>("20 - 10 / 2");
        await Assert.That(result).IsEqualTo(15);
    }

    [Test]
    public async Task Parentheses_OverridesPrecedence_AddsFirst() {
        // (2 + 3) * 4 = 5 * 4 = 20
        var result = Evaluate<int>("(2 + 3) * 4");
        await Assert.That(result).IsEqualTo(20);
    }

    [Test]
    public async Task NestedParentheses_ComplexExpression_EvaluatesCorrectly() {
        // ((2 + 3) * (4 + 1)) - 10 = (5 * 5) - 10 = 25 - 10 = 15
        var result = Evaluate<int>("((2 + 3) * (4 + 1)) - 10");
        await Assert.That(result).IsEqualTo(15);
    }

    [Test]
    public async Task ComplexExpression_MultipleOperators_EvaluatesCorrectly() {
        // 10 + 5 * 2 - 8 / 4 = 10 + 10 - 2 = 18
        var result = Evaluate<int>("10 + 5 * 2 - 8 / 4");
        await Assert.That(result).IsEqualTo(18);
    }

    [Test]
    public async Task UnaryMinus_NegativeNumber_ReturnsNegative() {
        var result = Evaluate<int>("-5");
        await Assert.That(result).IsEqualTo(-5);
    }

    [Test]
    public async Task UnaryMinus_InExpression_EvaluatesCorrectly() {
        // 10 + -5 = 10 + (-5) = 5
        var result = Evaluate<int>("10 + -5");
        await Assert.That(result).IsEqualTo(5);
    }

    [Test]
    public async Task UnaryMinus_WithParentheses_EvaluatesCorrectly() {
        // -(10 + 5) = -(15) = -15
        var result = Evaluate<int>("-(10 + 5)");
        await Assert.That(result).IsEqualTo(-15);
    }

    [Test]
    public async Task DecimalNumbers_Addition_ReturnsDouble() {
        var result = Evaluate<double>("2.5 + 3.7");
        await Assert.That(result).IsEqualTo(6.2);
    }

    [Test]
    public async Task MixedTypes_IntAndDouble_PromotesToDouble() {
        // 10 + 2.5 = 12.5 (type promotion handled by code generator)
        var result = Evaluate<double>("10 + 2.5");
        await Assert.That(result).IsEqualTo(12.5);
    }

    [Test]
    public async Task MixedTypes_ComplexExpression_PromotesCorrectly() {
        // 10 * 2.5 - 5 / 2.0 = 25.0 - 2.5 = 22.5
        var result = Evaluate<double>("10 * 2.5 - 5 / 2.0");
        await Assert.That(result).IsEqualTo(22.5);
    }

    [Test]
    public async Task LongExpression_ManyOperations_EvaluatesCorrectly() {
        // 1 + 2 * 3 - 4 / 2 + 5 * (6 - 2) = 1 + 6 - 2 + 5 * 4 = 1 + 6 - 2 + 20 = 25
        var result = Evaluate<int>("1 + 2 * 3 - 4 / 2 + 5 * (6 - 2)");
        await Assert.That(result).IsEqualTo(25);
    }

    [Test]
    public async Task WhitespaceHandling_VariousSpacing_ParsesCorrectly() {
        var result1 = Evaluate<int>("2+3*4");
        var result2 = Evaluate<int>("2 + 3 * 4");
        var result3 = Evaluate<int>("  2  +  3  *  4  ");

        await Assert.That(result1).IsEqualTo(14);
        await Assert.That(result2).IsEqualTo(14);
        await Assert.That(result3).IsEqualTo(14);
    }

    [Test]
    public async Task InvalidSyntax_MissingRightParen_ThrowsException() {
        await Assert.That(() => Evaluate<int>("(2 + 3"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task InvalidSyntax_UnexpectedToken_ThrowsException() {
        await Assert.That(() => Evaluate<int>("2 + + 3"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task InvalidSyntax_InvalidCharacter_ThrowsException() {
        await Assert.That(() => Evaluate<int>("2 + @ 3"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task InvalidOperation_DivideByZero_ThrowsException() {
        await Assert.That(() => Evaluate<int>("2 / 0"))
            .Throws<DivideByZeroException>();
    }

    [Test]
    public async Task ZeroValues_Operations_HandlesCorrectly() {
        await Assert.That(Evaluate<int>("0 + 5")).IsEqualTo(5);
        await Assert.That(Evaluate<int>("5 - 0")).IsEqualTo(5);
        await Assert.That(Evaluate<int>("0 * 5")).IsEqualTo(0);
        await Assert.That(Evaluate<int>("0 / 5")).IsEqualTo(0);
    }

    [Test]
    public async Task LargeNumbers_Addition_HandlesCorrectly() {
        var result = Evaluate<int>("1000000 + 2000000");
        await Assert.That(result).IsEqualTo(3000000);
    }

    [Test]
    public async Task ChainedOperations_LeftToRight_EvaluatesCorrectly() {
        // 10 - 5 - 2 = (10 - 5) - 2 = 5 - 2 = 3
        var result = Evaluate<int>("10 - 5 - 2");
        await Assert.That(result).IsEqualTo(3);
    }

    [Test]
    public async Task ChainedMultiplication_LeftToRight_EvaluatesCorrectly() {
        // 2 * 3 * 4 = (2 * 3) * 4 = 6 * 4 = 24
        var result = Evaluate<int>("2 * 3 * 4");
        await Assert.That(result).IsEqualTo(24);
    }
}