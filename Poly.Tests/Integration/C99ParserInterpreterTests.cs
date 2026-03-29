using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.AbstractSyntaxTree.Arithmetic;
using Poly.Interpretation.AbstractSyntaxTree.Boolean;
using Poly.Interpretation.AbstractSyntaxTree.Comparison;
using Poly.Interpretation.AbstractSyntaxTree.Equality;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.LinqExpressions;

namespace Poly.Tests.Integration;

/// <summary>
/// Integration tests for a self-contained C99 subset lexer, parser, and interpreter pipeline.
/// Demonstrates the full flow: C99 source text → tokens → Poly AST → LINQ expression → execution.
///
/// Supported subset: int/float/double types, arithmetic, comparison, logical operators,
/// ternary (?:), if/else, while, for, variable declarations, assignments.
/// </summary>
public class C99ParserInterpreterTests {

    // =========================================================================
    // Lexer
    // =========================================================================

    private enum C99TokenKind {
        IntLiteral, FloatLiteral, DoubleLiteral,
        Identifier,
        KwInt, KwFloat, KwDouble, KwReturn, KwIf, KwElse, KwWhile, KwFor,
        Plus, Minus, Star, Slash, Percent,
        Lt, LtEq, Gt, GtEq, EqEq, BangEq,
        AmpAmp, PipePipe, Bang,
        Eq,
        Question, Colon,
        LParen, RParen, LBrace, RBrace, Semicolon, Comma,
        Eof
    }

    private record C99Token(C99TokenKind Kind, string Text, int Position);

    private sealed class C99Lexer {
        private readonly string _src;
        private int _pos;

        public C99Lexer(string src) => _src = src;

        public List<C99Token> Tokenize() {
            var tokens = new List<C99Token>();
            while (_pos < _src.Length) {
                SkipWhitespace();
                if (_pos >= _src.Length) break;

                char c = _src[_pos];
                if (char.IsDigit(c) || (c == '.' && _pos + 1 < _src.Length && char.IsDigit(_src[_pos + 1]))) {
                    tokens.Add(ReadNumber());
                }
                else if (char.IsLetter(c) || c == '_') {
                    tokens.Add(ReadIdentOrKeyword());
                }
                else {
                    tokens.Add(ReadPunctuation());
                }
            }
            tokens.Add(new C99Token(C99TokenKind.Eof, "", _pos));
            return tokens;
        }

        private void SkipWhitespace() {
            while (_pos < _src.Length && char.IsWhiteSpace(_src[_pos])) _pos++;
        }

        private C99Token ReadNumber() {
            int start = _pos;
            bool hasDecimal = false;
            while (_pos < _src.Length && (char.IsDigit(_src[_pos]) || _src[_pos] == '.')) {
                if (_src[_pos] == '.') hasDecimal = true;
                _pos++;
            }
            if (_pos < _src.Length && (_src[_pos] == 'f' || _src[_pos] == 'F')) {
                _pos++;
                return new C99Token(C99TokenKind.FloatLiteral, _src[start.._pos], start);
            }
            var kind = hasDecimal ? C99TokenKind.DoubleLiteral : C99TokenKind.IntLiteral;
            return new C99Token(kind, _src[start.._pos], start);
        }

        private static readonly Dictionary<string, C99TokenKind> Keywords = new() {
            ["int"] = C99TokenKind.KwInt,
            ["float"] = C99TokenKind.KwFloat,
            ["double"] = C99TokenKind.KwDouble,
            ["return"] = C99TokenKind.KwReturn,
            ["if"] = C99TokenKind.KwIf,
            ["else"] = C99TokenKind.KwElse,
            ["while"] = C99TokenKind.KwWhile,
            ["for"] = C99TokenKind.KwFor,
        };

        private C99Token ReadIdentOrKeyword() {
            int start = _pos;
            while (_pos < _src.Length && (char.IsLetterOrDigit(_src[_pos]) || _src[_pos] == '_')) _pos++;
            var text = _src[start.._pos];
            var kind = Keywords.GetValueOrDefault(text, C99TokenKind.Identifier);
            return new C99Token(kind, text, start);
        }

        private C99Token ReadPunctuation() {
            int start = _pos;
            char c = _src[_pos++];
            return c switch {
                '+' => new C99Token(C99TokenKind.Plus, "+", start),
                '-' => new C99Token(C99TokenKind.Minus, "-", start),
                '*' => new C99Token(C99TokenKind.Star, "*", start),
                '/' => new C99Token(C99TokenKind.Slash, "/", start),
                '%' => new C99Token(C99TokenKind.Percent, "%", start),
                '<' when TryPeek('=') => new C99Token(C99TokenKind.LtEq, "<=", start),
                '<' => new C99Token(C99TokenKind.Lt, "<", start),
                '>' when TryPeek('=') => new C99Token(C99TokenKind.GtEq, ">=", start),
                '>' => new C99Token(C99TokenKind.Gt, ">", start),
                '=' when TryPeek('=') => new C99Token(C99TokenKind.EqEq, "==", start),
                '=' => new C99Token(C99TokenKind.Eq, "=", start),
                '!' when TryPeek('=') => new C99Token(C99TokenKind.BangEq, "!=", start),
                '!' => new C99Token(C99TokenKind.Bang, "!", start),
                '&' when TryPeek('&') => new C99Token(C99TokenKind.AmpAmp, "&&", start),
                '|' when TryPeek('|') => new C99Token(C99TokenKind.PipePipe, "||", start),
                '(' => new C99Token(C99TokenKind.LParen, "(", start),
                ')' => new C99Token(C99TokenKind.RParen, ")", start),
                '{' => new C99Token(C99TokenKind.LBrace, "{", start),
                '}' => new C99Token(C99TokenKind.RBrace, "}", start),
                ';' => new C99Token(C99TokenKind.Semicolon, ";", start),
                ',' => new C99Token(C99TokenKind.Comma, ",", start),
                '?' => new C99Token(C99TokenKind.Question, "?", start),
                ':' => new C99Token(C99TokenKind.Colon, ":", start),
                _ => throw new InvalidOperationException($"Unexpected character '{c}' at position {start}")
            };
        }

        private bool TryPeek(char expected) {
            if (_pos < _src.Length && _src[_pos] == expected) { _pos++; return true; }
            return false;
        }
    }

    // =========================================================================
    // Parser
    // =========================================================================

    private sealed record C99ParsedFunction(
        string Name,
        Type ReturnType,
        (Parameter Param, Type ClrType)[] Parameters,
        (Parameter Param, Type ClrType)[] Locals,
        Node Body
    );

    private sealed class C99Parser {
        private readonly List<C99Token> _tokens;
        private int _cur;

        // Shared symbol table: all in-scope identifiers (function params + local variables)
        private readonly Dictionary<string, (Parameter Param, Type ClrType)> _symbols = new();
        // Accumulated local variable declarations (excludes function parameters)
        private readonly List<(Parameter Param, Type ClrType)> _locals = new();

        public C99Parser(List<C99Token> tokens) => _tokens = tokens;

        public C99ParsedFunction ParseFunction() {
            var returnType = ParseTypeSpec();
            var name = Expect(C99TokenKind.Identifier).Text;

            Expect(C99TokenKind.LParen);
            var parameters = new List<(Parameter, Type)>();
            if (!Check(C99TokenKind.RParen)) {
                do {
                    var pType = ParseTypeSpec();
                    var pName = Expect(C99TokenKind.Identifier).Text;
                    var param = new Parameter(pName, TypeRef(pType));
                    _symbols[pName] = (param, pType);
                    parameters.Add((param, pType));
                } while (TryConsume(C99TokenKind.Comma));
            }
            Expect(C99TokenKind.RParen);

            var body = ParseBlock();
            return new C99ParsedFunction(name, returnType, [.. parameters], [.. _locals], body);
        }

        private Block ParseBlock() {
            Expect(C99TokenKind.LBrace);
            var blockVars = new List<Node>();
            var stmts = new List<Node>();

            while (!Check(C99TokenKind.RBrace) && !Check(C99TokenKind.Eof)) {
                if (IsTypeSpec()) {
                    var (decl, init) = ParseDeclaration();
                    blockVars.Add(decl.Param);
                    _locals.Add(decl);
                    if (init != null) stmts.Add(init);
                }
                else {
                    stmts.Add(ParseStatement());
                }
            }
            Expect(C99TokenKind.RBrace);

            // Block(expressions, variables): expressions first, then the declared variable nodes
            return new Block(stmts, blockVars);
        }

        private ((Parameter Param, Type ClrType) Decl, Node? Init) ParseDeclaration() {
            var type = ParseTypeSpec();
            var name = Expect(C99TokenKind.Identifier).Text;
            var param = new Parameter(name, TypeRef(type));
            _symbols[name] = (param, type);

            Node? init = null;
            if (TryConsume(C99TokenKind.Eq)) {
                init = new Assignment(param, ParseExpr());
            }
            Expect(C99TokenKind.Semicolon);
            return ((param, type), init);
        }

        private Node ParseStatement() {
            if (Check(C99TokenKind.KwReturn)) return ParseReturn();
            if (Check(C99TokenKind.KwIf)) return ParseIf();
            if (Check(C99TokenKind.KwWhile)) return ParseWhile();
            if (Check(C99TokenKind.KwFor)) return ParseFor();
            if (Check(C99TokenKind.LBrace)) return ParseBlock();

            var expr = ParseAssignmentOrExpr();
            Expect(C99TokenKind.Semicolon);
            return expr;
        }

        // In the "return value as last block expression" pattern, `return expr;` is unwrapped
        // to just `expr` so it becomes the block's result value without using ReturnStatement.
        private Node ParseReturn() {
            Expect(C99TokenKind.KwReturn);
            if (TryConsume(C99TokenKind.Semicolon)) return new Constant(null);
            var value = ParseExpr();
            Expect(C99TokenKind.Semicolon);
            return value;
        }

        private Node ParseIf() {
            Expect(C99TokenKind.KwIf);
            Expect(C99TokenKind.LParen);
            var cond = ParseExpr();
            Expect(C99TokenKind.RParen);
            var then = ParseStatement();
            Node? else_ = TryConsume(C99TokenKind.KwElse) ? ParseStatement() : null;
            return new IfStatement(cond, then, else_);
        }

        private Node ParseWhile() {
            Expect(C99TokenKind.KwWhile);
            Expect(C99TokenKind.LParen);
            var cond = ParseExpr();
            Expect(C99TokenKind.RParen);
            return new WhileLoop(cond, ParseStatement());
        }

        private Node ParseFor() {
            Expect(C99TokenKind.KwFor);
            Expect(C99TokenKind.LParen);

            Node? init = null;
            if (!Check(C99TokenKind.Semicolon))
                init = ParseAssignmentOrExpr();
            Expect(C99TokenKind.Semicolon);

            Node? cond = null;
            if (!Check(C99TokenKind.Semicolon))
                cond = ParseExpr();
            Expect(C99TokenKind.Semicolon);

            Node? incr = null;
            if (!Check(C99TokenKind.RParen))
                incr = ParseAssignmentOrExpr();
            Expect(C99TokenKind.RParen);

            return new ForLoop(init, cond, incr, ParseStatement());
        }

        // Handles `ident = expr` (assignment) or falls through to a plain expression.
        private Node ParseAssignmentOrExpr() {
            if (_cur + 1 < _tokens.Count
                && Check(C99TokenKind.Identifier)
                && _tokens[_cur + 1].Kind == C99TokenKind.Eq) {
                var name = Advance().Text;
                Advance(); // consume '='
                if (!_symbols.TryGetValue(name, out var sym))
                    throw new InvalidOperationException($"Undefined identifier '{name}'");
                return new Assignment(sym.Param, ParseExpr());
            }
            return ParseExpr();
        }

        private Node ParseExpr() => ParseTernary();

        private Node ParseTernary() {
            var cond = ParseOr();
            if (!TryConsume(C99TokenKind.Question)) return cond;
            var ifTrue = ParseExpr();
            Expect(C99TokenKind.Colon);
            var ifFalse = ParseExpr();
            return new Conditional(cond, ifTrue, ifFalse);
        }

        private Node ParseOr() {
            var left = ParseAnd();
            while (TryConsume(C99TokenKind.PipePipe))
                left = new Or(left, ParseAnd());
            return left;
        }

        private Node ParseAnd() {
            var left = ParseEquality();
            while (TryConsume(C99TokenKind.AmpAmp))
                left = new And(left, ParseEquality());
            return left;
        }

        private Node ParseEquality() {
            var left = ParseComparison();
            while (true) {
                if (TryConsume(C99TokenKind.EqEq)) left = new Equal(left, ParseComparison());
                else if (TryConsume(C99TokenKind.BangEq)) left = new NotEqual(left, ParseComparison());
                else break;
            }
            return left;
        }

        private Node ParseComparison() {
            var left = ParseAdditive();
            while (true) {
                if (TryConsume(C99TokenKind.Lt)) left = new LessThan(left, ParseAdditive());
                else if (TryConsume(C99TokenKind.LtEq)) left = new LessThanOrEqual(left, ParseAdditive());
                else if (TryConsume(C99TokenKind.Gt)) left = new GreaterThan(left, ParseAdditive());
                else if (TryConsume(C99TokenKind.GtEq)) left = new GreaterThanOrEqual(left, ParseAdditive());
                else break;
            }
            return left;
        }

        private Node ParseAdditive() {
            var left = ParseMultiplicative();
            while (true) {
                if (TryConsume(C99TokenKind.Plus)) left = new Add(left, ParseMultiplicative());
                else if (TryConsume(C99TokenKind.Minus)) left = new Subtract(left, ParseMultiplicative());
                else break;
            }
            return left;
        }

        private Node ParseMultiplicative() {
            var left = ParseUnary();
            while (true) {
                if (TryConsume(C99TokenKind.Star)) left = new Multiply(left, ParseUnary());
                else if (TryConsume(C99TokenKind.Slash)) left = new Divide(left, ParseUnary());
                else if (TryConsume(C99TokenKind.Percent)) left = new Modulo(left, ParseUnary());
                else break;
            }
            return left;
        }

        private Node ParseUnary() {
            if (TryConsume(C99TokenKind.Minus)) return new UnaryMinus(ParseUnary());
            if (TryConsume(C99TokenKind.Bang)) return new Not(ParseUnary());
            return ParsePrimary();
        }

        private Node ParsePrimary() {
            var tok = Peek();
            switch (tok.Kind) {
                case C99TokenKind.IntLiteral:
                    Advance();
                    return new Constant(int.Parse(tok.Text));

                case C99TokenKind.FloatLiteral:
                    Advance();
                    return new Constant(float.Parse(tok.Text.TrimEnd('f', 'F'), System.Globalization.CultureInfo.InvariantCulture));

                case C99TokenKind.DoubleLiteral:
                    Advance();
                    return new Constant(double.Parse(tok.Text, System.Globalization.CultureInfo.InvariantCulture));

                case C99TokenKind.Identifier:
                    Advance();
                    if (_symbols.TryGetValue(tok.Text, out var sym)) return sym.Param;
                    throw new InvalidOperationException($"Undefined identifier '{tok.Text}' at position {tok.Position}");

                case C99TokenKind.LParen:
                    Advance();
                    var inner = ParseExpr();
                    Expect(C99TokenKind.RParen);
                    return inner;

                default:
                    throw new InvalidOperationException($"Unexpected token '{tok.Text}' ({tok.Kind}) at position {tok.Position}");
            }
        }

        private bool IsTypeSpec() =>
            Peek().Kind is C99TokenKind.KwInt or C99TokenKind.KwFloat or C99TokenKind.KwDouble;

        private Type ParseTypeSpec() {
            var tok = Advance();
            return tok.Kind switch {
                C99TokenKind.KwInt => typeof(int),
                C99TokenKind.KwFloat => typeof(float),
                C99TokenKind.KwDouble => typeof(double),
                _ => throw new InvalidOperationException($"Expected type specifier, got '{tok.Text}'")
            };
        }

        private static TypeReference TypeRef(Type type) {
            if (type == typeof(int)) return TypeReference.To<int>();
            if (type == typeof(float)) return TypeReference.To<float>();
            if (type == typeof(double)) return TypeReference.To<double>();
            throw new ArgumentException($"Unsupported C99 type mapping for {type}");
        }

        private C99Token Peek() => _tokens[_cur];
        private C99Token Advance() => _tokens[_cur++];
        private bool Check(C99TokenKind kind) => _tokens[_cur].Kind == kind;
        private bool TryConsume(C99TokenKind kind) {
            if (Check(kind)) { _cur++; return true; }
            return false;
        }
        private C99Token Expect(C99TokenKind kind) {
            if (!Check(kind))
                throw new InvalidOperationException($"Expected {kind}, got '{Peek().Text}' at position {Peek().Position}");
            return Advance();
        }
    }

    // =========================================================================
    // Compiler
    // =========================================================================

    private static TDelegate CompileC99<TDelegate>(string source) where TDelegate : Delegate {
        var tokens = new C99Lexer(source).Tokenize();
        var fn = new C99Parser(tokens).ParseFunction();

        var analyzer = new AnalyzerBuilder()
            .UseTypeResolver()
            .UseMemberResolver()
            .UseVariableScopeValidator()
            .Build();

        var allParams = fn.Parameters.Concat(fn.Locals).ToArray();

        var analysisResult = analyzer
            .With(ctx => {
                foreach (var (param, clrType) in allParams) {
                    var typeDef = ctx.TypeDefinitions.GetTypeDefinition(clrType);
                    if (typeDef != null) ctx.SetResolvedType(param, typeDef);
                }
            })
            .Analyze(fn.Body);

        var generator = new LinqExpressionGenerator(analysisResult);
        return generator.CompileAsDelegate<TDelegate>(fn.Body, fn.Parameters.Select(p => p.Param).ToArray());
    }

    // =========================================================================
    // Tests
    // =========================================================================

    [Test]
    public async Task Add_TwoIntParameters_ReturnsSum() {
        var add = CompileC99<Func<int, int, int>>("""
            int add(int a, int b) {
                return a + b;
            }
            """);

        await Assert.That(add(3, 7)).IsEqualTo(10);
        await Assert.That(add(-5, 5)).IsEqualTo(0);
        await Assert.That(add(100, 200)).IsEqualTo(300);
    }

    [Test]
    public async Task Abs_WithTernary_ReturnsAbsoluteValue() {
        var abs = CompileC99<Func<int, int>>("""
            int abs(int x) {
                return x < 0 ? -x : x;
            }
            """);

        await Assert.That(abs(5)).IsEqualTo(5);
        await Assert.That(abs(-5)).IsEqualTo(5);
        await Assert.That(abs(0)).IsEqualTo(0);
    }

    [Test]
    public async Task Max_WithIfStatement_ReturnsLargerValue() {
        var max = CompileC99<Func<int, int, int>>("""
            int max(int a, int b) {
                int r = a;
                if (b > a) {
                    r = b;
                }
                return r;
            }
            """);

        await Assert.That(max(3, 7)).IsEqualTo(7);
        await Assert.That(max(7, 3)).IsEqualTo(7);
        await Assert.That(max(5, 5)).IsEqualTo(5);
    }

    [Test]
    public async Task Factorial_WithWhileLoop_ReturnsCorrectResults() {
        var factorial = CompileC99<Func<int, int>>("""
            int factorial(int n) {
                int result = 1;
                int i = 2;
                while (i <= n) {
                    result = result * i;
                    i = i + 1;
                }
                return result;
            }
            """);

        await Assert.That(factorial(0)).IsEqualTo(1);
        await Assert.That(factorial(1)).IsEqualTo(1);
        await Assert.That(factorial(5)).IsEqualTo(120);
        await Assert.That(factorial(10)).IsEqualTo(3628800);
    }

    [Test]
    public async Task Fibonacci_WithForLoop_ReturnsCorrectSequence() {
        var fib = CompileC99<Func<int, int>>("""
            int fib(int n) {
                int a = 0;
                int b = 1;
                int i = 0;
                int t = 0;
                for (i = 0; i < n; i = i + 1) {
                    t = b;
                    b = a + b;
                    a = t;
                }
                return a;
            }
            """);

        await Assert.That(fib(0)).IsEqualTo(0);
        await Assert.That(fib(1)).IsEqualTo(1);
        await Assert.That(fib(2)).IsEqualTo(1);
        await Assert.That(fib(3)).IsEqualTo(2);
        await Assert.That(fib(5)).IsEqualTo(5);
        await Assert.That(fib(10)).IsEqualTo(55);
    }

    [Test]
    public async Task SumOfSquares_WithDoubleType_ReturnsCorrectResult() {
        var sumOfSquares = CompileC99<Func<double, double, double>>("""
            double sumOfSquares(double a, double b) {
                return a * a + b * b;
            }
            """);

        await Assert.That(sumOfSquares(3.0, 4.0)).IsEqualTo(25.0);
        await Assert.That(sumOfSquares(0.0, 0.0)).IsEqualTo(0.0);
    }

    [Test]
    public async Task IsEven_WithLogicalOperators_ReturnsCorrectResult() {
        var isEven = CompileC99<Func<int, int>>("""
            int isEven(int n) {
                int r = 0;
                if (n >= 0 && n % 2 == 0) {
                    r = 1;
                }
                return r;
            }
            """);

        await Assert.That(isEven(4)).IsEqualTo(1);
        await Assert.That(isEven(3)).IsEqualTo(0);
        await Assert.That(isEven(0)).IsEqualTo(1);
        await Assert.That(isEven(-2)).IsEqualTo(0);
    }

    [Test]
    public async Task Clamp_WithNestedTernary_ReturnsValueWithinRange() {
        var clamp = CompileC99<Func<int, int, int, int>>("""
            int clamp(int x, int lo, int hi) {
                return x < lo ? lo : x > hi ? hi : x;
            }
            """);

        await Assert.That(clamp(5, 0, 10)).IsEqualTo(5);
        await Assert.That(clamp(-5, 0, 10)).IsEqualTo(0);
        await Assert.That(clamp(15, 0, 10)).IsEqualTo(10);
    }
}