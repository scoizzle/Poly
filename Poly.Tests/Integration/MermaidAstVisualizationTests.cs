using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.AbstractSyntaxTree.Arithmetic;
using Poly.Interpretation.Mermaid;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Integration;

/// <summary>
/// Integration tests demonstrating Mermaid AST visualization.
/// Shows how to generate Mermaid diagrams from parsed expressions.
/// </summary>
public class MermaidAstVisualizationTests {
    private static Node Wrap(object? value) => new Constant(value);

    /// <summary>
    /// Test simple arithmetic expression visualization.
    /// </summary>
    [Test]
    public async Task SimpleExpression_TwoPlusThree_GeneratesMermaidDiagram()
    {
        // Arrange
        var ast = new Add(Wrap(2), Wrap(3));
        var generator = new MermaidAstGenerator();

        // Act
        var mermaid = generator.Generate(ast);

        // Assert
        await Assert.That(mermaid).IsNotNull();
        await Assert.That(mermaid).Contains("graph TB");
        await Assert.That(mermaid).Contains("Add (+)");
        await Assert.That(mermaid).Contains("Constant");
    }

    /// <summary>
    /// Test complex nested expression visualization.
    /// </summary>
    [Test]
    public async Task ComplexExpression_NestedOperations_ShowsStructure()
    {
        // Arrange: (2 + 3) * 4
        var add = new Add(Wrap(2), Wrap(3));
        var multiply = new Multiply(add, Wrap(4));
        var generator = new MermaidAstGenerator();

        // Act
        var mermaid = generator.Generate(multiply);

        // Assert
        await Assert.That(mermaid).Contains("Multiply (*)");
        await Assert.That(mermaid).Contains("Add (+)");
        await Assert.That(mermaid).Contains("left");
        await Assert.That(mermaid).Contains("right");
    }

    /// <summary>
    /// Test visualization with semantic analysis metadata.
    /// </summary>
    [Test]
    public async Task WithAnalysis_GeneratesValidDiagram()
    {
        // Arrange
        var ast = new Add(Wrap(2), Wrap(3));
        var analysisResult = ast.AnalyzeNode();
        var generator = new MermaidAstGenerator(analysisResult);

        // Act
        var mermaid = generator.Generate(ast);

        // Assert - Should generate valid diagram even with analysis
        await Assert.That(mermaid).Contains("graph TB");
        await Assert.That(mermaid).Contains("Add (+)");
    }

    /// <summary>
    /// Test parameter visualization in expression.
    /// </summary>
    [Test]
    public async Task WithParameter_ShowsParameterNode()
    {
        // Arrange
        var x = new Parameter("x", TypeReference.To<int>());
        var ast = new Add(x, Wrap(10));
        var generator = new MermaidAstGenerator();

        // Act
        var mermaid = generator.Generate(ast);

        // Assert
        await Assert.That(mermaid).Contains("Parameter");
        await Assert.That(mermaid).Contains("x");
    }

    /// <summary>
    /// Test unary operation visualization.
    /// </summary>
    [Test]
    public async Task UnaryOperation_NegateExpression_ShowsCorrectly()
    {
        // Arrange: -(2 + 3)
        var add = new Add(Wrap(2), Wrap(3));
        var negate = new UnaryMinus(add);
        var generator = new MermaidAstGenerator();

        // Act
        var mermaid = generator.Generate(negate);

        // Assert
        await Assert.That(mermaid).Contains("Negate (-)");
        await Assert.That(mermaid).Contains("Add (+)");
    }

    /// <summary>
    /// Test conditional expression visualization.
    /// </summary>
    [Test]
    public async Task Conditional_TernaryOperator_ShowsThreeBranches()
    {
        // Arrange: true ? 42 : 0
        var ast = new Conditional(Wrap(true), Wrap(42), Wrap(0));
        var generator = new MermaidAstGenerator();

        // Act
        var mermaid = generator.Generate(ast);

        // Assert
        await Assert.That(mermaid).Contains("Conditional (?:)");
        await Assert.That(mermaid).Contains("condition");
        await Assert.That(mermaid).Contains("true");
        await Assert.That(mermaid).Contains("false");
    }

    /// <summary>
    /// Test left-to-right flow direction.
    /// </summary>
    [Test]
    public async Task DirectionParameter_LeftToRight_GeneratesLRGraph()
    {
        // Arrange
        var ast = new Add(Wrap(2), Wrap(3));
        var generator = new MermaidAstGenerator();

        // Act
        var mermaid = generator.Generate(ast, direction: "LR");

        // Assert
        await Assert.That(mermaid).Contains("graph LR");
    }

    /// <summary>
    /// Test complex arithmetic parser expression with visualization.
    /// Demonstrates end-to-end: text -> tokens -> AST -> Mermaid diagram.
    /// </summary>
    [Test]
    public async Task ArithmeticParser_ComplexExpression_GeneratesCompleteDiagram()
    {
        // Arrange: Parse "(2 + 3) * 4 - 1"
        var lexer = new ArithmeticLexer("(2 + 3) * 4 - 1");
        var tokens = lexer.Tokenize();
        var parser = new ArithmeticParser(tokens);
        var ast = parser.Parse();

        var generator = new MermaidAstGenerator();

        // Act
        var mermaid = generator.Generate(ast);

        // Print the diagram for documentation
        Console.WriteLine("\n--- Mermaid Diagram for '(2 + 3) * 4 - 1' ---");
        Console.WriteLine(mermaid);
        Console.WriteLine("--- End Diagram ---\n");

        // Assert
        await Assert.That(mermaid).Contains("Subtract (-)");
        await Assert.That(mermaid).Contains("Multiply (*)");
        await Assert.That(mermaid).Contains("Add (+)");
        await Assert.That(mermaid).Contains("Constant 4");
        await Assert.That(mermaid).Contains("Constant 1");
    }

    /// <summary>
    /// Test that generated Mermaid is valid by checking basic structure.
    /// </summary>
    [Test]
    public async Task MermaidOutput_BasicStructure_IsWellFormed()
    {
        // Arrange
        var ast = new Multiply(new Add(Wrap(1), Wrap(2)), Wrap(3));
        var generator = new MermaidAstGenerator();

        // Act
        var mermaid = generator.Generate(ast);
        var lines = mermaid.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Assert - Must start with graph declaration
        await Assert.That(lines[0]).StartsWith("graph ");

        // Should have node definitions (containing square brackets or parentheses)
        var hasNodeDefinitions = lines.Any(l => l.Contains('[') || l.Contains('('));
        await Assert.That(hasNodeDefinitions).IsTrue();

        // Should have edges (containing -->)
        var hasEdges = lines.Any(l => l.Contains("-->"));
        await Assert.That(hasEdges).IsTrue();
    }

    /// <summary>
    /// Test visualization of deeply nested expression.
    /// </summary>
    [Test]
    public async Task DeeplyNested_MultiLevel_HandlesCorrectly()
    {
        // Arrange: ((1 + 2) * (3 + 4)) - 5
        var add1 = new Add(Wrap(1), Wrap(2));
        var add2 = new Add(Wrap(3), Wrap(4));
        var multiply = new Multiply(add1, add2);
        var subtract = new Subtract(multiply, Wrap(5));

        var generator = new MermaidAstGenerator();

        // Act
        var mermaid = generator.Generate(subtract);

        // Assert - Should contain all operations
        await Assert.That(mermaid).Contains("Subtract (-)");
        await Assert.That(mermaid).Contains("Multiply (*)");

        // Count number of Add nodes (should be 2)
        var addCount = mermaid.Split("Add (+)").Length - 1;
        await Assert.That(addCount).IsEqualTo(2);
    }

    /// <summary>
    /// Test coalesce operator visualization.
    /// </summary>
    [Test]
    public async Task CoalesceOperator_NullCoalescing_ShowsValueAndDefault()
    {
        // Arrange: x ?? 42
        var x = new Parameter("x", TypeReference.To<int?>());
        var ast = new Coalesce(x, Wrap(42));
        var generator = new MermaidAstGenerator();

        // Act
        var mermaid = generator.Generate(ast);

        // Assert
        await Assert.That(mermaid).Contains("Coalesce (??)");
        await Assert.That(mermaid).Contains("value");
        await Assert.That(mermaid).Contains("default");
    }

    /// <summary>
    /// Test type cast visualization.
    /// </summary>
    [Test]
    public async Task TypeCast_IntToDouble_ShowsCastOperation()
    {
        // Arrange: (double)42
        var ast = new TypeCast(Wrap(42), TypeReference.To<double>());
        var generator = new MermaidAstGenerator();

        // Act
        var mermaid = generator.Generate(ast);

        // Assert
        await Assert.That(mermaid).Contains("Cast to");
        await Assert.That(mermaid).Contains("Double");
    }

    #region Helper classes from ArithmeticParserEvaluatorTests

    private enum TokenType {
        Number, Plus, Minus, Multiply, Divide, LeftParen, RightParen, End
    }

    private record Token(TokenType Type, string Value, int Position);

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
                if (char.IsWhiteSpace(_input[_position])) {
                    _position++;
                    continue;
                }

                if (char.IsDigit(_input[_position]) || _input[_position] == '.') {
                    var start = _position;
                    while (_position < _input.Length &&
                           (char.IsDigit(_input[_position]) || _input[_position] == '.'))
                        _position++;
                    tokens.Add(new Token(TokenType.Number, _input[start.._position], start));
                    continue;
                }

                var tokenType = _input[_position] switch {
                    '+' => TokenType.Plus,
                    '-' => TokenType.Minus,
                    '*' => TokenType.Multiply,
                    '/' => TokenType.Divide,
                    '(' => TokenType.LeftParen,
                    ')' => TokenType.RightParen,
                    _ => throw new InvalidOperationException($"Unexpected character '{_input[_position]}'")
                };

                tokens.Add(new Token(tokenType, _input[_position].ToString(), _position));
                _position++;
            }

            tokens.Add(new Token(TokenType.End, string.Empty, _position));
            return tokens;
        }
    }

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
            if (_tokens[_current].Type != TokenType.End)
                throw new InvalidOperationException("Unexpected token");
            return result;
        }

        private Node ParseExpression()
        {
            var left = ParseTerm();
            while (_tokens[_current].Type is TokenType.Plus or TokenType.Minus) {
                var op = _tokens[_current].Type;
                _current++;
                var right = ParseTerm();
                left = op == TokenType.Plus ? new Add(left, right) : new Subtract(left, right);
            }
            return left;
        }

        private Node ParseTerm()
        {
            var left = ParseFactor();
            while (_tokens[_current].Type is TokenType.Multiply or TokenType.Divide) {
                var op = _tokens[_current].Type;
                _current++;
                var right = ParseFactor();
                left = op == TokenType.Multiply ? new Multiply(left, right) : new Divide(left, right);
            }
            return left;
        }

        private Node ParseFactor()
        {
            var token = _tokens[_current];
            if (token.Type == TokenType.LeftParen) {
                _current++;
                var expr = ParseExpression();
                if (_tokens[_current].Type != TokenType.RightParen)
                    throw new InvalidOperationException("Expected ')'");
                _current++;
                return expr;
            }

            if (token.Type == TokenType.Number) {
                _current++;
                return token.Value.Contains('.')
                    ? new Constant(double.Parse(token.Value))
                    : new Constant(int.Parse(token.Value));
            }

            if (token.Type == TokenType.Minus) {
                _current++;
                return new UnaryMinus(ParseFactor());
            }

            throw new InvalidOperationException("Unexpected token");
        }
    }

    #endregion
}