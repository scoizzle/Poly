using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.LinqExpressions;

namespace Poly.Tests.Integration;

/// <summary>
/// Integration tests for a self-contained C99 subset lexer, parser, and interpreter pipeline.
/// Demonstrates the full flow: C99 source text → tokens → Poly AST → LINQ expression → execution.
///
/// Supported subset: int/float/double types, arithmetic, comparison, logical operators,
/// ternary (?:), if/else, while, for, variable declarations, struct definitions,
/// member access, and assignments.
/// </summary>
public class C99ParserInterpreterTests {

    // =========================================================================
    // Lexer
    // =========================================================================

    private enum C99TokenKind {
        IntLiteral, FloatLiteral, DoubleLiteral,
        Identifier,
        KwInt, KwFloat, KwDouble, KwStruct, KwNamespace, KwReturn, KwIf, KwElse, KwWhile, KwFor,
        Plus, Minus, Star, Slash, Percent,
        Lt, LtEq, Gt, GtEq, EqEq, BangEq,
        AmpAmp, PipePipe, Bang,
        Eq,
        Question, Colon,
        Dot,
        LBracket, RBracket,
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
            ["struct"] = C99TokenKind.KwStruct,
            ["namespace"] = C99TokenKind.KwNamespace,
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
                '[' => new C99Token(C99TokenKind.LBracket, "[", start),
                ']' => new C99Token(C99TokenKind.RBracket, "]", start),
                '{' => new C99Token(C99TokenKind.LBrace, "{", start),
                '}' => new C99Token(C99TokenKind.RBrace, "}", start),
                ';' => new C99Token(C99TokenKind.Semicolon, ";", start),
                ',' => new C99Token(C99TokenKind.Comma, ",", start),
                '?' => new C99Token(C99TokenKind.Question, "?", start),
                ':' => new C99Token(C99TokenKind.Colon, ":", start),
                '.' => new C99Token(C99TokenKind.Dot, ".", start),
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

    private struct C99Point {
        public int x;
        public int y;

        public C99Point(int x, int y) {
            this.x = x;
            this.y = y;
        }
    }

    private struct C99Segment {
#pragma warning disable CS0649
        public C99Point start;
        public C99Point end;
#pragma warning restore CS0649
    }

    private sealed class C99Parser {
        private readonly List<C99Token> _tokens;
        private int _cur;

        // Shared symbol table: all in-scope identifiers (function params + local variables)
        private readonly Dictionary<string, (Parameter Param, Type ClrType)> _symbols = new();
        // Accumulated local variable declarations (excludes function parameters)
        private readonly List<(Parameter Param, Type ClrType)> _locals = new();
        // Struct aliases available to this test parser; values are host CLR types.
        private static readonly Dictionary<string, Type> KnownStructTypes = new() {
            ["C99Point"] = typeof(C99Point),
            ["C99Segment"] = typeof(C99Segment),
        };
        private readonly Dictionary<string, Type> _structTypes = new(KnownStructTypes);

        public C99Parser(List<C99Token> tokens) => _tokens = tokens;

        public C99ParsedFunction ParseFunction() {
            if (Check(C99TokenKind.KwNamespace)) {
                throw new InvalidOperationException("C99 does not support namespaces.");
            }

            while (Check(C99TokenKind.KwStruct) && IsStructDefinition()) {
                ParseStructDefinition();
            }

            var returnType = ParseTypeSpec();
            var name = Expect(C99TokenKind.Identifier).Text;

            Expect(C99TokenKind.LParen);
            var parameters = new List<(Parameter, Type)>();
            if (!Check(C99TokenKind.RParen)) {
                do {
                    var baseType = ParseTypeSpec();
                    var (pName, pType) = ParseDeclarator(baseType);
                    var param = CreateParameter(pName, pType);
                    _symbols[pName] = (param, pType);
                    parameters.Add((param, pType));
                } while (TryConsume(C99TokenKind.Comma));
            }
            Expect(C99TokenKind.RParen);

            var body = ParseBlock();
            return new C99ParsedFunction(name, returnType, [.. parameters], [.. _locals], body);
        }

        private bool IsStructDefinition() {
            if (!Check(C99TokenKind.KwStruct)) return false;
            if (_cur + 2 >= _tokens.Count) return false;
            return _tokens[_cur + 1].Kind == C99TokenKind.Identifier
                && _tokens[_cur + 2].Kind == C99TokenKind.LBrace;
        }

        private void ParseStructDefinition() {
            Expect(C99TokenKind.KwStruct);
            var name = Expect(C99TokenKind.Identifier).Text;
            Expect(C99TokenKind.LBrace);

            var declaredFields = new List<(string Name, Type Type)>();
            while (!Check(C99TokenKind.RBrace) && !Check(C99TokenKind.Eof)) {
                var fieldType = ParseTypeSpec();
                var fieldName = Expect(C99TokenKind.Identifier).Text;
                Expect(C99TokenKind.Semicolon);
                declaredFields.Add((fieldName, fieldType));
            }

            Expect(C99TokenKind.RBrace);
            Expect(C99TokenKind.Semicolon);

            if (!KnownStructTypes.TryGetValue(name, out var hostType)) {
                throw new InvalidOperationException($"Struct '{name}' is not mapped to a host CLR type in this test harness.");
            }

            ValidateStructFields(name, hostType, declaredFields);
            _structTypes[name] = hostType;
        }

        private static void ValidateStructFields(string structName, Type hostType, List<(string Name, Type Type)> fields) {
            foreach (var (name, type) in fields) {
                var hostField = hostType.GetField(name);
                if (hostField == null) {
                    throw new InvalidOperationException($"Struct '{structName}' field '{name}' is not present on host type '{hostType.Name}'.");
                }

                if (hostField.FieldType != type) {
                    throw new InvalidOperationException(
                        $"Struct '{structName}' field '{name}' type mismatch. C99 declares '{type.Name}', host has '{hostField.FieldType.Name}'.");
                }
            }
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
            var baseType = ParseTypeSpec();
            var (name, type) = ParseDeclarator(baseType);
            var param = CreateParameter(name, type);
            _symbols[name] = (param, type);

            Node? init = null;
            if (TryConsume(C99TokenKind.Eq)) {
                init = ParseDeclarationInitializer(param, type);
            }
            Expect(C99TokenKind.Semicolon);
            return ((param, type), init);
        }

        private Node ParseDeclarationInitializer(Parameter destination, Type destinationType) {
            if (Check(C99TokenKind.LBrace)) {
                return ParseDesignatedInitializer(destination, destinationType);
            }

            return new Assignment(destination, ParseExpr());
        }

        private Node ParseDesignatedInitializer(Parameter destination, Type destinationType) {
            return destinationType.IsArray
                ? ParseDesignatedArrayInitializer(destination, destinationType)
                : ParseDesignatedStructInitializer(destination, destinationType);
        }

        private Node ParseDesignatedStructInitializer(Parameter destination, Type destinationType) {
            if (destinationType == typeof(int) || destinationType == typeof(float) || destinationType == typeof(double)) {
                throw new InvalidOperationException("Designated initializers are only supported for struct types.");
            }

            Expect(C99TokenKind.LBrace);
            var assignments = new List<Node>();

            while (!Check(C99TokenKind.RBrace) && !Check(C99TokenKind.Eof)) {
                if (!Check(C99TokenKind.Dot) && !Check(C99TokenKind.LBracket)) {
                    throw new InvalidOperationException("Expected designated field path starting with '.' or '['.");
                }

                var destinationPath = ParseDesignatorPath(destination, destinationType);
                Expect(C99TokenKind.Eq);
                var value = ParseExpr();
                assignments.Add(new Assignment(destinationPath, value));

                if (!TryConsume(C99TokenKind.Comma)) {
                    break;
                }
            }

            Expect(C99TokenKind.RBrace);

            if (assignments.Count == 0) {
                throw new InvalidOperationException("Designated initializer must include at least one field assignment.");
            }

            return assignments.Count == 1
                ? assignments[0]
                : new Block(assignments.ToArray());
        }

        private Node ParseDesignatedArrayInitializer(Parameter destination, Type destinationType) {
            var elementType = destinationType.GetElementType()
                ?? throw new InvalidOperationException($"Array type '{destinationType}' has no element type.");

            Expect(C99TokenKind.LBrace);
            var pending = new List<(int Index, Node Value)>();

            while (!Check(C99TokenKind.RBrace) && !Check(C99TokenKind.Eof)) {
                Expect(C99TokenKind.LBracket);
                var index = ParseDesignatorIndex();
                Expect(C99TokenKind.RBracket);
                Expect(C99TokenKind.Eq);
                var value = ParseExpr();
                pending.Add((index, value));

                if (!TryConsume(C99TokenKind.Comma)) {
                    break;
                }
            }

            Expect(C99TokenKind.RBrace);

            if (pending.Count == 0) {
                throw new InvalidOperationException("Designated array initializer must include at least one [index] assignment.");
            }

            int length = pending.Max(p => p.Index) + 1;
            var initNodes = new List<Node> {
                new Assignment(destination, new Constant(Array.CreateInstance(elementType, length)))
            };

            foreach (var (index, value) in pending) {
                var target = new IndexAccess(destination, [new Constant(index)]);
                initNodes.Add(new Assignment(target, value));
            }

            return new Block(initNodes.ToArray());
        }

        private Node ParseDesignatorPath(Node root, Type rootType) {
            Node currentNode = root;
            var currentType = rootType;

            while (Check(C99TokenKind.Dot) || Check(C99TokenKind.LBracket)) {
                if (TryConsume(C99TokenKind.Dot)) {
                    var member = Expect(C99TokenKind.Identifier).Text;
                    var nextType = ResolveMemberType(currentType, member);
                    currentNode = new Member(currentNode, member);
                    currentType = nextType;
                    continue;
                }

                Expect(C99TokenKind.LBracket);
                var index = ParseDesignatorIndex();
                Expect(C99TokenKind.RBracket);

                if (!currentType.IsArray) {
                    throw new InvalidOperationException($"Type '{currentType.Name}' is not indexable in designated path.");
                }

                currentNode = new IndexAccess(currentNode, [new Constant(index)]);
                currentType = currentType.GetElementType()
                    ?? throw new InvalidOperationException($"Array type '{currentType}' has no element type.");
            }

            return currentNode;
        }

        private int ParseDesignatorIndex() {
            var token = Expect(C99TokenKind.IntLiteral);
            if (!int.TryParse(token.Text, out var index) || index < 0) {
                throw new InvalidOperationException($"Designator index must be a non-negative integer literal, got '{token.Text}'.");
            }

            return index;
        }

        private static Type ResolveMemberType(Type ownerType, string member) {
            var field = ownerType.GetField(member);
            if (field != null) {
                return field.FieldType;
            }

            var property = ownerType.GetProperty(member);
            if (property != null) {
                return property.PropertyType;
            }

            throw new InvalidOperationException($"Struct '{ownerType.Name}' does not contain field '{member}'.");
        }

        private static void EnsureStructFieldExists(Type structType, string fieldName) {
            if (structType.GetField(fieldName) == null) {
                throw new InvalidOperationException($"Struct '{structType.Name}' does not contain field '{fieldName}'.");
            }
        }

        private (string Name, Type Type) ParseDeclarator(Type baseType) {
            var name = Expect(C99TokenKind.Identifier).Text;
            if (TryConsume(C99TokenKind.LBracket)) {
                Expect(C99TokenKind.RBracket);
                return (name, baseType.MakeArrayType());
            }

            return (name, baseType);
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

        // Handles `ident = expr` and `ident.member = expr` assignments, or falls through to a plain expression.
        private Node ParseAssignmentOrExpr() {
            if (Check(C99TokenKind.Identifier)) {
                int checkpoint = _cur;
                var destination = ParseAssignmentDestination();
                if (TryConsume(C99TokenKind.Eq)) {
                    return new Assignment(destination, ParseExpr());
                }

                _cur = checkpoint;
            }

            return ParseExpr();
        }

        private Node ParseAssignmentDestination() {
            var nameToken = Expect(C99TokenKind.Identifier);
            if (!_symbols.TryGetValue(nameToken.Text, out var sym)) {
                throw new InvalidOperationException($"Undefined identifier '{nameToken.Text}'");
            }

            Node destination = sym.Param;
            while (TryConsume(C99TokenKind.Dot)) {
                var member = Expect(C99TokenKind.Identifier).Text;
                destination = new Member(destination, member);
            }

            return destination;
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
            var expr = ParsePrimaryAtom();

            while (Check(C99TokenKind.Dot) || Check(C99TokenKind.LBracket)) {
                if (TryConsume(C99TokenKind.Dot)) {
                    var member = Expect(C99TokenKind.Identifier).Text;
                    expr = new Member(expr, member);
                }
                else {
                    Expect(C99TokenKind.LBracket);
                    var indexExpr = ParseExpr();
                    Expect(C99TokenKind.RBracket);
                    expr = new IndexAccess(expr, [indexExpr]);
                }
            }

            return expr;
        }

        private Node ParsePrimaryAtom() {
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
            Peek().Kind is C99TokenKind.KwInt or C99TokenKind.KwFloat or C99TokenKind.KwDouble or C99TokenKind.KwStruct;

        private Type ParseTypeSpec() {
            if (TryConsume(C99TokenKind.KwStruct)) {
                var name = Expect(C99TokenKind.Identifier).Text;
                if (_structTypes.TryGetValue(name, out var structType)) return structType;
                throw new InvalidOperationException($"Unknown struct type '{name}'.");
            }

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

        private static Parameter CreateParameter(string name, Type type) {
            if (type == typeof(int) || type == typeof(float) || type == typeof(double)) {
                return new Parameter(name, TypeRef(type));
            }

            // Struct types are resolved from pre-registered metadata in CompileC99.
            return new Parameter(name);
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
        var parameters = fn.Parameters.Select(p => p.Param).ToArray();
        if (parameters.Length == 0) {
            var body = generator.Compile(fn.Body).Expression;
            return System.Linq.Expressions.Expression.Lambda<TDelegate>(body).Compile();
        }

        return generator.CompileAsDelegate<TDelegate>(fn.Body, parameters);
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

    [Test]
    public async Task StructDefinition_AndMemberAccess_UsesMappedHostStruct() {
        var sum = CompileC99<Func<C99Point, int>>("""
            struct C99Point {
                int x;
                int y;
            };

            int sum(struct C99Point p) {
                return p.x + p.y;
            }
            """);

        await Assert.That(sum(new C99Point(3, 4))).IsEqualTo(7);
        await Assert.That(sum(new C99Point(10, -2))).IsEqualTo(8);
    }

    [Test]
    public async Task StructLocal_FieldAssignments_AndReadback_WorkCorrectly() {
        var eval = CompileC99<Func<int>>("""
            struct C99Point {
                int x;
                int y;
            };

            int eval() {
                struct C99Point p;
                p.x = 8;
                p.y = 13;
                return p.x + p.y;
            }
            """);

        await Assert.That(eval()).IsEqualTo(21);
    }

    [Test]
    public async Task StructCopyInitialization_ThenMemberMutation_WorksCorrectly() {
        var project = CompileC99<Func<C99Point, int>>("""
            struct C99Point {
                int x;
                int y;
            };

            int project(struct C99Point seed) {
                struct C99Point p = seed;
                p.x = p.x + 2;
                p.y = p.y + 3;
                return p.x * 100 + p.y;
            }
            """);

        await Assert.That(project(new C99Point(1, 2))).IsEqualTo(305);
        await Assert.That(project(new C99Point(10, 20))).IsEqualTo(1223);
    }

    [Test]
    public async Task StructDesignatedInitializer_AssignsNamedFields() {
        var eval = CompileC99<Func<int>>("""
            struct C99Point {
                int x;
                int y;
            };

            int eval() {
                struct C99Point p = { .x = 7, .y = 9 };
                return p.x * 10 + p.y;
            }
            """);

        await Assert.That(eval()).IsEqualTo(79);
    }

    [Test]
    public async Task StructDesignatedInitializer_AllowsOutOfOrderAndDefaultsUnspecifiedFields() {
        var eval = CompileC99<Func<int>>("""
            struct C99Point {
                int x;
                int y;
            };

            int eval() {
                struct C99Point p = { .y = 11 };
                return p.x + p.y;
            }
            """);

        await Assert.That(eval()).IsEqualTo(11);
    }

    [Test]
    public async Task StructDesignatedInitializer_UnknownField_ThrowsException() {
        await Assert.That(() => CompileC99<Func<int>>("""
            struct C99Point {
                int x;
                int y;
            };

            int eval() {
                struct C99Point p = { .z = 1 };
                return p.x;
            }
            """))
            .Throws<InvalidOperationException>()
                .WithMessage("Struct 'C99Point' does not contain field 'z'.");
    }

    [Test]
    public async Task StructNestedDesignatedInitializer_AssignsNestedFields() {
        var eval = CompileC99<Func<int>>("""
            struct C99Point {
                int x;
                int y;
            };

            struct C99Segment {
                struct C99Point start;
                struct C99Point end;
            };

            int eval() {
                struct C99Segment s = { .start.x = 2, .start.y = 3, .end.x = 7, .end.y = 11 };
                return s.start.x + s.start.y + s.end.x + s.end.y;
            }
            """);

        await Assert.That(eval()).IsEqualTo(23);
    }

    [Test]
    public async Task ArrayDesignatedInitializer_AssignsIndexedValues() {
        var eval = CompileC99<Func<int>>("""
            int eval() {
                int values[] = { [0] = 3, [2] = 5, [4] = 8 };
                return values[0] + values[1] + values[2] + values[3] + values[4];
            }
            """);

        await Assert.That(eval()).IsEqualTo(16);
    }

    [Test]
    public async Task ArrayDesignatedInitializer_NonArrayType_ThrowsException() {
        await Assert.That(() => CompileC99<Func<int>>("""
            int eval() {
                int x = { [0] = 1 };
                return x;
            }
            """))
            .Throws<InvalidOperationException>()
            .WithMessage("Designated initializers are only supported for struct types.");
    }

    [Test]
    public async Task NamespaceKeyword_IsRejected_WithClearMessage() {
        await Assert.That(() => CompileC99<Func<int, int, int>>("""
            namespace math {
                int add(int a, int b) {
                    return a + b;
                }
            }
            """))
            .Throws<InvalidOperationException>()
            .WithMessage("C99 does not support namespaces.");
    }
}