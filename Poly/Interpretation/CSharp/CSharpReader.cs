using System.Globalization;

using Poly.Grammar;
using Poly.Introspection;

namespace Poly.Interpretation.CSharp;

/// <summary>
/// Reads C# source back into Poly Syntax AST nodes — the read half of the C#
/// language surface. The scanner decodes tokens; the engine's
/// <see cref="Matcher{TToken,TTokenKind}"/> recognises patterns from the shared
/// <see cref="CSharpGrammar"/> table; folds turn each matched form tree into the
/// same node types <see cref="CSharpWriter"/> prints.
///
/// The reader is a recogniser for the exported subset, not a C# compiler front
/// end: constructs outside the table fail closed with a positioned error rather
/// than folding to a wrong shape. Child matches are dispatched by rule name
/// (each optional slot wraps a distinct rule), so optional positions never shift
/// the fold order.
/// </summary>
public static class CSharpReader {
    /// <summary>Reads a compilation unit. Throws <see cref="FormatException"/>
    /// (via <c>GrammarError</c>) on unsupported or invalid input.</summary>
    public static CompilationUnitNode Read(string text) => new Parser(text).ReadUnit();

    private sealed class Parser {
        private readonly CSharpTokenizer _reader;
        private readonly Matcher<CSharpToken, CSharpTokenKind> _matcher;

        public Parser(string text) {
            _reader = new CSharpTokenizer(text);
            _matcher = new Matcher<CSharpToken, CSharpTokenKind>(CSharpGrammar.Table, _reader);
        }

        private CSharpToken Current => _reader.Peek(0);

        internal CompilationUnitNode ReadUnit() {
            var usings = new List<string>();
            var types = new List<TypeDefinitionNode>();
            var statements = new List<Node>();
            string? ns = null;

            while (Current.Kind != CSharpTokenKind.EndOfFile) {
                if (TryTake("using", out var usingMatch)) {
                    usings.Add(FoldName(Child(usingMatch, "name")));
                    continue;
                }
                if (TryTake("namespace", out var nsMatch)) {
                    ns = FoldName(Child(nsMatch, "name"));
                    continue;
                }
                if (AtTypeDeclaration()) {
                    types.Add(FoldTypeDecl(Take("type-decl", "type declaration")));
                    continue;
                }
                statements.Add(FoldStatement(Take("statement", "statement")));
            }
            return new CompilationUnitNode(usings, ns, types, statements.Count > 0 ? statements : null);
        }

        private bool AtTypeDeclaration() => Current.Kind is
            CSharpTokenKind.KwClass or CSharpTokenKind.KwRecord or CSharpTokenKind.KwInterface
            or CSharpTokenKind.KwEnum or CSharpTokenKind.KwPublic or CSharpTokenKind.KwPrivate
            or CSharpTokenKind.KwInternal or CSharpTokenKind.KwProtected or CSharpTokenKind.LBracket;

        // ── match plumbing ───────────────────────────────────────────────────

        private bool TryTake(string rule, out MatchResult<CSharpToken, CSharpTokenKind> match) {
            match = _matcher.TryMatch(rule)!;
            if (match is null)
                return false;
            _reader.Consume(match.Consumed);
            return true;
        }

        private MatchResult<CSharpToken, CSharpTokenKind> Take(string rule, string context) {
            var match = _matcher.TryMatch(rule)
                ?? throw Error($"Expected {context}, got '{Current.Text}'");
            _reader.Consume(match.Consumed);
            return match;
        }

        private FormatException Error(string message) => GrammarError.Error(message, Current.Line, Current.Col);

        private static MatchResult<CSharpToken, CSharpTokenKind> Child(
            MatchResult<CSharpToken, CSharpTokenKind> match, string rule) =>
            match.Children.FirstOrDefault(c => string.Equals(c.RuleName, rule, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Match '{match.RuleName}/{match.PatternName}' is missing child rule '{rule}'.");

        private static MatchResult<CSharpToken, CSharpTokenKind>? MaybeChild(
            MatchResult<CSharpToken, CSharpTokenKind> match, string rule) =>
            match.Children.FirstOrDefault(c => string.Equals(c.RuleName, rule, StringComparison.Ordinal));

        private static IEnumerable<MatchResult<CSharpToken, CSharpTokenKind>> Children(
            MatchResult<CSharpToken, CSharpTokenKind> match, string rule) =>
            match.Children.Where(c => string.Equals(c.RuleName, rule, StringComparison.Ordinal));

        private static string Capture(MatchResult<CSharpToken, CSharpTokenKind> match, string name) =>
            match.Captures.TryGetValue(name, out var tokens) && tokens.Count > 0
                ? tokens[0].Text
                : throw new InvalidOperationException($"Match '{match.RuleName}/{match.PatternName}' is missing capture '{name}'.");

        private static CSharpToken CapturedToken(MatchResult<CSharpToken, CSharpTokenKind> match, string name) =>
            match.Captures.TryGetValue(name, out var tokens) && tokens.Count > 0
                ? tokens[0]
                : throw new InvalidOperationException($"Match '{match.RuleName}/{match.PatternName}' is missing capture '{name}'.");

        private static bool HasCapture(MatchResult<CSharpToken, CSharpTokenKind> match, string name) =>
            match.Captures.TryGetValue(name, out var tokens) && tokens.Count > 0;

        /// <summary>Source text of a match's token span (types and other tight
        /// text where token concatenation is the spelling).</summary>
        private static string SourceText(MatchResult<CSharpToken, CSharpTokenKind> match) =>
            string.Concat(match.Tokens.Select(t => t.Text));

        // ── names / attributes / modifiers ───────────────────────────────────

        private static string FoldName(MatchResult<CSharpToken, CSharpTokenKind> match) => match.PatternName switch {
            "simple" => Capture(match, "name"),
            "qualified" => Capture(match, "head") + "." + FoldName(match.Children[0]),
            _ => throw new InvalidOperationException($"Cannot fold name '{match.PatternName}'."),
        };

        private AttributeNode FoldAttribute(MatchResult<CSharpToken, CSharpTokenKind> match) {
            var name = FoldName(Child(match, "name"));
            var arguments = MaybeChild(match, "args") is { } args
                ? FoldArgList(args).Cast<Poly.Ast.Nodes.Expression>().ToArray()
                : [];
            return new AttributeNode(name, arguments);
        }

        private IReadOnlyList<AttributeNode> FoldAttributes(MatchResult<CSharpToken, CSharpTokenKind> match) =>
            [.. Children(match, "attribute").Select(FoldAttribute)];

        private sealed record ModifierSet(
            AccessModifier Access = AccessModifier.Public,
            bool Static = false,
            bool Const = false,
            bool Readonly = false,
            bool Volatile = false,
            bool Override = false,
            bool Async = false);

        private static ModifierSet FoldModifiers(MatchResult<CSharpToken, CSharpTokenKind>? match) {
            if (match is null)
                return new ModifierSet();
            var set = new ModifierSet();
            foreach (var modifier in match.Children) {
                set = modifier.PatternName switch {
                    "public" => set with { Access = AccessModifier.Public },
                    "private" => set with { Access = AccessModifier.Private },
                    "protected" => set with { Access = AccessModifier.Protected },
                    "internal" => set with { Access = AccessModifier.Internal },
                    "static" => set with { Static = true },
                    "const" => set with { Const = true },
                    "readonly" => set with { Readonly = true },
                    "volatile" => set with { Volatile = true },
                    "override" => set with { Override = true },
                    "async" => set with { Async = true },
                    _ => throw new InvalidOperationException($"Cannot fold modifier '{modifier.PatternName}'."),
                };
            }
            return set;
        }

        // ── type declarations ────────────────────────────────────────────────

        private TypeDefinitionNode FoldTypeDecl(MatchResult<CSharpToken, CSharpTokenKind> match) {
            var attributes = MaybeChild(match, "attributes") is { } attrs ? FoldAttributes(attrs) : [];
            var modifiers = FoldModifiers(MaybeChild(match, "modifiers"));
            var name = Capture(match, "name");
            var genericParameters = MaybeChild(match, "generic-params") is { } generics ? FoldGenericParams(generics) : null;
            var primaryParameters = MaybeChild(match, "params") is { } primary ? FoldParams(primary) : null;
            var (baseType, interfaces) = FoldLineage(MaybeChild(match, "lineage"));
            var body = MaybeChild(match, "type-body");

            var typeDef = match.PatternName switch {
                "enum" => new TypeDefinitionNode(name,
                    Fields: FoldEnumFields(Child(match, "enum-body")),
                    TypeCategory: TypeCategory.Enumeration,
                    AccessModifier: modifiers.Access),
                "interface" => new TypeDefinitionNode(name,
                    Properties: FoldProperties(body),
                    Methods: FoldMethods(body),
                    BaseType: baseType,
                    Interfaces: interfaces,
                    GenericParameters: genericParameters,
                    IsInterface: true,
                    AccessModifier: modifiers.Access),
                "record-semi" or "record" => new TypeDefinitionNode(name,
                    Constructors: FoldConstructors(body),
                    Properties: FoldProperties(body),
                    Methods: FoldMethods(body),
                    Fields: FoldFields(body),
                    BaseType: baseType,
                    Interfaces: interfaces,
                    GenericParameters: genericParameters,
                    PrimaryConstructorParameters: primaryParameters,
                    Semantics: TypeDefinitionSemantics.ImmutableValue,
                    AccessModifier: modifiers.Access),
                "class" => new TypeDefinitionNode(name,
                    Constructors: FoldConstructors(body),
                    Properties: FoldProperties(body),
                    Methods: FoldMethods(body),
                    Fields: FoldFields(body),
                    BaseType: baseType,
                    Interfaces: interfaces,
                    GenericParameters: genericParameters,
                    AccessModifier: modifiers.Access),
                _ => throw new InvalidOperationException($"Cannot fold type-decl '{match.PatternName}'."),
            };
            return typeDef with { Attributes = attributes };
        }

        private IReadOnlyList<Parameter> FoldLambdaParams(MatchResult<CSharpToken, CSharpTokenKind>? list) {
            if (list is null)
                return [];
            var parameters = new List<Parameter> { FoldLambdaParam(list.Children[0]) };
            foreach (var tail in list.Children.Skip(1))
                parameters.Add(FoldLambdaParam(tail.Children[0]));
            return parameters;
        }

        private Parameter FoldLambdaParam(MatchResult<CSharpToken, CSharpTokenKind> match) {
            var type = MaybeChild(match, "type") is { } typeMatch ? FoldType(typeMatch) : null;
            var defaultValue = MaybeChild(match, "param-default") is { } def ? FoldExpr(def.Children[0]) : null;
            return new Parameter(Capture(match, "name"), type, defaultValue);
        }

        private static IReadOnlyList<Parameter> FoldGenericParams(MatchResult<CSharpToken, CSharpTokenKind> match) =>
            [.. Children(match, "generic-param").Select(p => new Parameter(Capture(p, "name")))];

        private static (Node? BaseType, IReadOnlyList<Node>? Interfaces) FoldLineage(
            MatchResult<CSharpToken, CSharpTokenKind>? match) {
            if (match is null)
                return (null, null);
            var first = FoldType(match.Children[0]);
            var rest = match.Children.Skip(1).Select(tail => FoldType(tail.Children[0])).ToArray();
            return (first, rest.Length == 0 ? null : rest);
        }

        private static IReadOnlyList<FieldDefinitionNode> FoldEnumFields(MatchResult<CSharpToken, CSharpTokenKind> match) {
            var fields = new List<FieldDefinitionNode>();
            foreach (var member in match.Children) {
                var target = string.Equals(member.RuleName, "enum-member", StringComparison.Ordinal)
                    ? member
                    : member.Children[0];
                fields.Add(new FieldDefinitionNode(
                    Capture(target, "name"),
                    new PrimitiveTypeReference(PrimitiveType.Int32),
                    IsStatic: true,
                    IsConst: true));
            }
            return fields;
        }

        // ── members ──────────────────────────────────────────────────────────

        private IReadOnlyList<FieldDefinitionNode> FoldFields(MatchResult<CSharpToken, CSharpTokenKind>? body) =>
            body is null ? [] : [.. Children(body, "member").Where(m => m.PatternName == "field").Select(FoldField)];

        private IReadOnlyList<PropertyDefinitionNode> FoldProperties(MatchResult<CSharpToken, CSharpTokenKind>? body) =>
            body is null ? [] : [.. Children(body, "member").Where(m => m.PatternName == "property").Select(FoldProperty)];

        private IReadOnlyList<MethodDefinitionNode> FoldMethods(MatchResult<CSharpToken, CSharpTokenKind>? body) =>
            body is null ? [] : [.. Children(body, "member").Where(m => m.PatternName == "method").Select(FoldMethod)];

        private IReadOnlyList<ConstructorDefinitionNode> FoldConstructors(MatchResult<CSharpToken, CSharpTokenKind>? body) =>
            body is null ? [] : [.. Children(body, "member").Where(m => m.PatternName == "ctor").Select(FoldConstructor)];

        private FieldDefinitionNode FoldField(MatchResult<CSharpToken, CSharpTokenKind> match) {
            var modifiers = FoldModifiers(MaybeChild(match, "modifiers"));
            var field = new FieldDefinitionNode(
                Capture(match, "name"),
                FoldType(Child(match, "type")),
                MaybeChild(match, "field-init") is { } init ? FoldExpr(init.Children[0]) : null,
                IsStatic: modifiers.Static,
                IsReadOnly: modifiers.Readonly,
                IsVolatile: modifiers.Volatile,
                IsConst: modifiers.Const,
                AccessModifier: modifiers.Access);
            return MaybeChild(match, "attributes") is { } attrs
                ? field with { Attributes = FoldAttributes(attrs) }
                : field;
        }

        private PropertyDefinitionNode FoldProperty(MatchResult<CSharpToken, CSharpTokenKind> match) {
            var modifiers = FoldModifiers(MaybeChild(match, "modifiers"));
            PropertyGetterDefinitionNode? getter = null;
            PropertySetterDefinitionNode? setter = null;
            var sawInit = false;
            foreach (var accessor in Children(Child(match, "property-body"), "accessor")) {
                switch (accessor.PatternName) {
                    case "get-semi":
                        getter = new PropertyGetterDefinitionNode();
                        break;
                    case "get-arrow":
                        getter = new PropertyGetterDefinitionNode(FoldExpr(accessor.Children[0]));
                        break;
                    case "set-semi":
                        setter = new PropertySetterDefinitionNode();
                        break;
                    case "set-arrow":
                        setter = new PropertySetterDefinitionNode(null, FoldExpr(accessor.Children[0]));
                        break;
                    case "init-semi":
                        sawInit = true;
                        break;
                    case "set-mod-semi":
                        setter = new PropertySetterDefinitionNode(null, null, FoldAccessorModifier(accessor));
                        break;
                    case "set-mod-arrow":
                        setter = new PropertySetterDefinitionNode(null, FoldExpr(accessor.Children[1]), FoldAccessorModifier(accessor));
                        break;
                    default:
                        throw new InvalidOperationException($"Cannot fold accessor '{accessor.PatternName}'.");
                }
            }
            var initializer = MaybeChild(match, "property-init") is { } init
                ? new PropertyInitializerDefinitionNode(FoldExpr(init.Children[0]))
                : sawInit ? new PropertyInitializerDefinitionNode() : null;
            var property = new PropertyDefinitionNode(
                Capture(match, "name"),
                FoldType(Child(match, "type")),
                Getter: getter,
                Setter: setter,
                Initializer: initializer,
                IsStatic: modifiers.Static,
                IsVolatile: modifiers.Volatile,
                IsReadOnly: modifiers.Readonly,
                IsConst: modifiers.Const,
                AccessModifier: modifiers.Access);
            return MaybeChild(match, "attributes") is { } attrs
                ? property with { Attributes = FoldAttributes(attrs) }
                : property;
        }

        private static AccessModifier? FoldAccessorModifier(MatchResult<CSharpToken, CSharpTokenKind> accessor) =>
            accessor.Children[0].PatternName switch {
                "public" => AccessModifier.Public,
                "private" => AccessModifier.Private,
                "protected" => AccessModifier.Protected,
                "internal" => AccessModifier.Internal,
                _ => throw new InvalidOperationException($"Cannot fold accessor modifier '{accessor.Children[0].PatternName}'."),
            };

        private MethodDefinitionNode FoldMethod(MatchResult<CSharpToken, CSharpTokenKind> match) {
            var modifiers = FoldModifiers(MaybeChild(match, "modifiers"));
            var body = MaybeChild(match, "method-body");
            Node? methodBody = body?.PatternName switch {
                "block" => FoldStatement(body.Children[0]),
                "arrow" => new Block(new Return(FoldExpr(body.Children[0]))),
                "semi" => null,
                _ => throw new InvalidOperationException($"Cannot fold method body '{body?.PatternName}'."),
            };
            var method = new MethodDefinitionNode(
                Capture(match, "name"),
                FoldType(Child(match, "type")),
                MaybeChild(match, "params") is { } parameters ? FoldParams(parameters) : null,
                methodBody,
                IsStatic: modifiers.Static,
                IsAsync: modifiers.Async,
                IsOverride: modifiers.Override,
                GenericParameters: MaybeChild(match, "generic-params") is { } generics ? FoldGenericParams(generics) : null,
                AccessModifier: modifiers.Access);
            return MaybeChild(match, "attributes") is { } attrs
                ? method with { Attributes = FoldAttributes(attrs) }
                : method;
        }

        private ConstructorDefinitionNode FoldConstructor(MatchResult<CSharpToken, CSharpTokenKind> match) {
            var modifiers = FoldModifiers(MaybeChild(match, "modifiers"));
            var body = MaybeChild(match, "ctor-body");
            Node? ctorBody = body?.PatternName switch {
                "block" => FoldStatement(body.Children[0]),
                "empty" => null,
                _ => throw new InvalidOperationException($"Cannot fold constructor body '{body?.PatternName}'."),
            };
            var ctor = new ConstructorDefinitionNode(
                MaybeChild(match, "params") is { } parameters ? FoldParams(parameters) : null,
                ctorBody,
                AccessModifier: modifiers.Access);
            if (MaybeChild(match, "ctor-base") is { } baseMatch) {
                ctor = ctor with {
                    BaseConstructorInvocation = new BaseConstructorInvocationNode(
                        [.. FoldArgList(baseMatch.Children[0]).Cast<Poly.Ast.Nodes.Expression>()]),
                };
            }
            return ctor;
        }

        // ── parameters and arguments ─────────────────────────────────────────

        private IReadOnlyList<Parameter> FoldParams(MatchResult<CSharpToken, CSharpTokenKind> match) {
            if (match.Children.Count == 0)
                return [];
            return FoldParamList(match.Children[0]);
        }

        private IReadOnlyList<Parameter> FoldParamList(MatchResult<CSharpToken, CSharpTokenKind> list) {
            var parameters = new List<Parameter> { FoldParam(list.Children[0]) };
            foreach (var tail in list.Children.Skip(1))
                parameters.Add(FoldParam(tail.Children[0]));
            return parameters;
        }

        private Parameter FoldParam(MatchResult<CSharpToken, CSharpTokenKind> match) {
            var type = MaybeChild(match, "type") is { } typeMatch ? FoldType(typeMatch) : null;
            var defaultValue = MaybeChild(match, "param-default") is { } def ? FoldExpr(def.Children[0]) : null;
            return new Parameter(Capture(match, "name"), type, defaultValue);
        }

        private IReadOnlyList<Node> FoldArgList(MatchResult<CSharpToken, CSharpTokenKind> match) {
            // Accepts the 'args' wrapper (parenthesised) or an 'arg-list' match.
            var list = string.Equals(match.RuleName, "args", StringComparison.Ordinal)
                ? match.Children.FirstOrDefault()
                : match;
            if (list is null)
                return [];
            var arguments = new List<Node> { FoldExpr(list.Children[0]) };
            foreach (var tail in list.Children.Skip(1))
                arguments.Add(FoldExpr(tail.Children[0]));
            return arguments;
        }

        private static IReadOnlyList<Node> FoldTypeArgs(MatchResult<CSharpToken, CSharpTokenKind> match) =>
            [.. FoldTypeList(match.Children[0])];

        private static IReadOnlyList<Node> FoldTypeList(MatchResult<CSharpToken, CSharpTokenKind> list) {
            var types = new List<Node> { FoldType(list.Children[0]) };
            foreach (var tail in list.Children.Skip(1))
                types.Add(FoldType(tail.Children[0]));
            return types;
        }

        // ── types ────────────────────────────────────────────────────────────

        private static Node FoldType(MatchResult<CSharpToken, CSharpTokenKind> match) {
            var type = FoldTypeCore(match.Children[0]);
            foreach (var suffix in match.Children.Skip(1)) {
                type = suffix.PatternName switch {
                    "optional" => new OptionalTypeReference(type),
                    "array" => new CollectionTypeReference(type, CollectionKind.Array),
                    _ => throw new InvalidOperationException($"Cannot fold type suffix '{suffix.PatternName}'."),
                };
            }
            return type;
        }

        private static Node FoldTypeCore(MatchResult<CSharpToken, CSharpTokenKind> match) {
            switch (match.PatternName) {
                case "void":
                    return new TypeReference("void");
                case "bool": return Primitive(PrimitiveType.Boolean);
                case "byte": return Primitive(PrimitiveType.UInt8);
                case "sbyte": return Primitive(PrimitiveType.Int8);
                case "short": return Primitive(PrimitiveType.Int16);
                case "ushort": return Primitive(PrimitiveType.UInt16);
                case "int": return Primitive(PrimitiveType.Int32);
                case "uint": return Primitive(PrimitiveType.UInt32);
                case "long": return Primitive(PrimitiveType.Int64);
                case "ulong": return Primitive(PrimitiveType.UInt64);
                case "float": return Primitive(PrimitiveType.Float32);
                case "double": return Primitive(PrimitiveType.Float64);
                case "decimal": return Primitive(PrimitiveType.Decimal);
                case "char": return Primitive(PrimitiveType.Char);
                case "string": return Primitive(PrimitiveType.String);
                case "object": return Primitive(PrimitiveType.Structure);
                case "named": {
                        var name = FoldName(match.Children[0]);
                        var argsMatch = MaybeChild(match, "type-args");
                        if (argsMatch is null)
                            return new TypeReference(name);
                        var arguments = FoldTypeList(Child(argsMatch, "type-list"));
                        return name switch {
                            "List" when arguments.Count == 1 => new CollectionTypeReference(arguments[0], CollectionKind.List),
                            "ISet" when arguments.Count == 1 => new CollectionTypeReference(arguments[0], CollectionKind.Set),
                            "Dictionary" when arguments.Count == 2 => new MapTypeReference(arguments[0], arguments[1]),
                            _ => new NamedTypeReference(name, TypeArguments: arguments),
                        };
                    }
                default:
                    throw new InvalidOperationException($"Cannot fold type core '{match.PatternName}'.");
            }
        }

        private static PrimitiveTypeReference Primitive(PrimitiveType type) => new(type);

        // ── statements ───────────────────────────────────────────────────────

        private Node FoldStatement(MatchResult<CSharpToken, CSharpTokenKind> match) {
            switch (match.PatternName) {
                case "block":
                    return FoldBlock(match.RuleName == "statement" ? Child(match, "block") : match);
                case "if": {
                        var condition = FoldExpr(match.Children[0]);
                        var thenBranch = FoldStatement(match.Children[1]);
                        var elseMatch = MaybeChild(match, "else");
                        var elseBranch = elseMatch is null ? null : FoldStatement(elseMatch.Children[0]);
                        return new IfStatement(condition, thenBranch, elseBranch);
                    }
                case "while":
                    return new WhileLoop(FoldExpr(match.Children[0]), FoldStatement(match.Children[1]));
                case "do":
                    return new DoWhileLoop(FoldStatement(match.Children[0]), FoldExpr(match.Children[1]));
                case "for": {
                        var (init, declared) = MaybeChild(match, "for-init") is { } i
                            ? FoldForInit(i)
                            : (null, null);
                        var condition = MaybeChild(match, "for-cond") is { } c ? FoldExpr(c.Children[0]) : null;
                        var increment = MaybeChild(match, "for-incr") is { } inc ? FoldExpr(inc.Children[0]) : null;
                        var body = FoldStatement(MaybeChild(match, "statement") ?? throw Error("Expected for statement body."));
                        return new ForLoop(init, condition, increment, body, DeclaredVariables: declared);
                    }
                case "foreach": {
                        var loopVariable = new Variable(Capture(match, "name"));
                        var collection = FoldExpr(match.Children[1]);
                        var body = FoldStatement(match.Children[2]);
                        var decl = match.Children[0];
                        var declaredType = decl.PatternName == "typed" ? FoldType(decl.Children[0]) : null;
                        return new ForEachLoop(loopVariable, collection, body, DeclaredType: declaredType);
                    }
                case "switch": {
                        var value = FoldExpr(match.Children[0]);
                        var cases = new List<SwitchCase>();
                        Node? defaultCase = null;
                        foreach (var caseMatch in Children(match, "switch-case")) {
                            if (caseMatch.PatternName == "default") {
                                defaultCase = FoldStatement(caseMatch.Children[0]);
                                continue;
                            }
                            cases.Add(new SwitchCase(FoldExpr(caseMatch.Children[0]), FoldStatement(caseMatch.Children[1])));
                        }
                        return new SwitchStatement(value, cases, defaultCase);
                    }
                case "try": {
                        var tryBlock = FoldStatement(match.Children[0]);
                        var catches = new List<CatchClause>();
                        foreach (var catchMatch in Children(match, "catch")) {
                            var decl = MaybeChild(catchMatch, "catch-decl");
                            Node? exceptionType = decl is null ? null : FoldType(decl.Children[0]);
                            var variableName = decl is not null && HasCapture(decl, "name") ? Capture(decl, "name") : null;
                            var body = FoldStatement(catchMatch.Children[^1]);
                            catches.Add(new CatchClause(exceptionType, variableName, body));
                        }
                        var finallyMatch = MaybeChild(match, "finally");
                        var finallyBlock = finallyMatch is null ? null : FoldStatement(finallyMatch.Children[0]);
                        return new TryCatchFinally(tryBlock, catches.Count == 0 ? null : catches, finallyBlock);
                    }
                case "using": {
                        var resource = FoldUsingResource(Child(match, "using-resource"));
                        var body = FoldStatement(match.Children[^1]);
                        return new UsingStatement(resource, body);
                    }
                case "local-function":
                    return FoldMethod(Child(match, "local-function"));
                case "return":
                    return new Return(match.Children.Count > 0 && match.Children[0].RuleName == "expr"
                        ? FoldExpr(match.Children[0])
                        : null);
                case "throw":
                    return new ThrowStatement(FoldExpr(match.Children[0]));
                case "break":
                    return new BreakStatement();
                case "continue":
                    return new ContinueStatement();
                case "goto":
                    return new GotoStatement(Capture(match, "target"));
                case "label":
                    return new LabelDeclaration(Capture(match, "label"), FoldStatement(match.Children[0]));
                case "var-decl" or "typed-decl" or "const-decl" or "typed-decl-bare": {
                        var (variable, value) = match.PatternName == "typed-decl-bare"
                            ? FoldBareDeclaration(match)
                            : FoldDeclaration(match);
                        return new Assignment(variable, value);
                    }
                case "expr-stmt":
                    return FoldExpr(match.Children[0]);
                case "empty":
                    throw Error("Empty statements are not part of the exported subset.");
                default:
                    throw new InvalidOperationException($"Cannot fold statement '{match.PatternName}'.");
            }
        }

        /// <summary>
        /// A local declaration folds to an assignment to a fresh <see cref="Variable"/>.
        /// <c>var x = e;</c> infers from <c>e</c>; <c>T x = e;</c> and <c>const T x = e;</c>
        /// cast the initializer so the variable <em>is</em> the declared type
        /// (<c>double x = 1;</c> divides as double). Later plain assignments are
        /// converted to that representation by the emitter, so the type holds across
        /// reassignment. The caller keeps the returned variable so the block declares
        /// the same instance.
        /// </summary>
        private (Variable Variable, Node Value) FoldDeclaration(
            MatchResult<CSharpToken, CSharpTokenKind> statement) {
            var variable = new Variable(Capture(statement, "name"));
            // var-decl children = [expr]; typed-decl / const-decl children = [type, expr].
            var value = statement.PatternName is "typed-decl" or "const-decl"
                ? (Node)new TypeCast(FoldExpr(statement.Children[^1]), FoldType(statement.Children[0]))
                : FoldExpr(statement.Children[^1]);
            return (variable, value);
        }

        /// <summary>
        /// A bare declaration (<c>int x;</c>) folds to an assignment of the declared
        /// type's default, so the variable carries its declared type and the writer emits
        /// <c>var x = default(int);</c>. C# requires an assignment before use; this
        /// normalization gives the local the type's default value instead — the declared
        /// type is what the form has to carry, and the writer/reader cycle is stable.
        /// </summary>
        private static (Variable Variable, Node Value) FoldBareDeclaration(
            MatchResult<CSharpToken, CSharpTokenKind> statement) =>
            (new Variable(Capture(statement, "name")), new Default(FoldType(statement.Children[0])));

        /// <summary>
        /// Folds a for-loop initializer. A <c>var</c> or typed declaration declares a
        /// loop-local variable (<c>for (var i = 0; …)</c>): the declaration assignment is
        /// the initializer and the freshly minted variable is returned so the loop owns
        /// its scope. A plain expression is an assignment to a variable declared in an
        /// enclosing scope and declares nothing.
        /// </summary>
        private (Node Init, IReadOnlyList<Variable>? Declared) FoldForInit(
            MatchResult<CSharpToken, CSharpTokenKind> match) {
            if (match.PatternName == "init")
                return (FoldExpr(match.Children[0]), null);
            var variable = new Variable(Capture(match, "name"));
            // var-decl children = [expr]; typed-decl children = [type, expr].
            var value = match.PatternName == "typed-decl"
                ? (Node)new TypeCast(FoldExpr(match.Children[^1]), FoldType(match.Children[0]))
                : FoldExpr(match.Children[^1]);
            return (new Assignment(variable, value), [variable]);
        }

        private Block FoldBlock(MatchResult<CSharpToken, CSharpTokenKind> match) {
            var nodes = new List<Node>();
            var variables = new List<Node>();
            foreach (var statement in Children(match, "statement")) {
                if (statement.PatternName is "var-decl" or "typed-decl" or "const-decl" or "typed-decl-bare") {
                    var (variable, value) = statement.PatternName == "typed-decl-bare"
                        ? FoldBareDeclaration(statement)
                        : FoldDeclaration(statement);
                    variables.Add(variable);
                    nodes.Add(new Assignment(variable, value));
                    continue;
                }
                nodes.Add(FoldStatement(statement));
            }
            return new Block(nodes, variables);
        }

        /// <summary>The binary node a compound assignment desugars into.</summary>
        private static Node CompoundOperator(string pattern, Node target, Node value) => pattern switch {
            "compound-add" => new Add(target, value),
            "compound-sub" => new Subtract(target, value),
            "compound-mul" => new Multiply(target, value),
            "compound-div" => new Divide(target, value),
            "compound-mod" => new Modulo(target, value),
            "compound-and" => new BitwiseAnd(target, value),
            "compound-or" => new BitwiseOr(target, value),
            "compound-xor" => new BitwiseXor(target, value),
            "compound-coalesce" => new Coalesce(target, value),
            "compound-shift-left" => new ShiftLeft(target, value),
            _ => throw new InvalidOperationException($"Unknown compound assignment '{pattern}'."),
        };

        /// <summary>
        /// True when a compound assignment may be desugared to
        /// <c>target = target op value</c>. The desugar repeats the target, so the
        /// target's receiver and index expressions must be re-evaluable without
        /// observable difference — see <see cref="IsPureExpression"/>. The member or
        /// index access itself is fine: both forms read it once and assign once.
        /// </summary>
        private static bool IsPureAssignmentTarget(Node target) => target switch {
            Variable or ThisReference => true,
            Member member => IsPureExpression(member.Value),
            IndexAccess index => IsPureExpression(index.Value) && index.Arguments.All(IsPureExpression),
            _ => false,
        };

        /// <summary>
        /// True when evaluating <paramref name="expression"/> twice cannot be observed:
        /// literals, variables, <c>this</c>, and operators over those. Member/index
        /// reads are excluded because a getter or indexer may have side effects, and
        /// invocations, allocations, assignments, and awaits are excluded outright.
        /// </summary>
        private static bool IsPureExpression(Node expression) {
            switch (expression) {
                case Member or IndexAccess or Invoke or New or NewArray or Await or Assignment or SuspendNode:
                    return false;
            }
            foreach (var child in expression.Children) {
                if (child is not null && !IsPureExpression(child))
                    return false;
            }
            return true;
        }

        private Node FoldUsingResource(MatchResult<CSharpToken, CSharpTokenKind> match) =>
            match.PatternName == "declare"
                ? new Assignment(new Variable(Capture(match, "name")), FoldExpr(match.Children[0]))
                : FoldExpr(match.Children[0]);

        // ── expressions ──────────────────────────────────────────────────────

        private Node FoldExpr(MatchResult<CSharpToken, CSharpTokenKind> match) {
            switch (match.RuleName) {
                case "expr":
                    return FoldExpr(match.Children[0]);
                case "expr-assignment": {
                        var destination = FoldExpr(match.Children[0]);
                        var tail = MaybeChild(match, "assign-tail");
                        if (tail is null)
                            return destination;
                        if (tail.PatternName is "tail")
                            return new Assignment(destination, FoldExpr(tail.Children[0]));
                        // A compound assignment desugars to `target = target op value`, which
                        // evaluates the target twice. Only fold it when the target cannot run
                        // side effects; otherwise fail closed rather than change meaning.
                        if (!IsPureAssignmentTarget(destination))
                            throw Error(
                                "Compound assignment on a target that cannot be re-evaluated safely is not part of the exported subset.");
                        return new Assignment(
                            destination,
                            CompoundOperator(tail.PatternName, destination, FoldExpr(tail.Children[0])));
                    }
                case "expr-conditional": {
                        // A `throw` in expression position; the operand is a full
                        // expression (C# `throw_expression : 'throw' expression`).
                        if (match.PatternName == "throw")
                            return new ThrowExpression((Poly.Ast.Nodes.Expression)FoldExpr(match.Children[0]));
                        var condition = FoldExpr(match.Children[0]);
                        var tail = MaybeChild(match, "cond-tail");
                        return tail is null
                            ? condition
                            : new Conditional(condition, FoldExpr(tail.Children[0]), FoldExpr(tail.Children[1]));
                    }
                case "expr-coalesce" or "expr-or" or "expr-and" or "expr-bitor" or "expr-bitxor"
                    or "expr-bitand" or "expr-equality":
                    return FoldLeftAssoc(match);
                case "expr-relational":
                    return match.PatternName is "type-is" or "type-as" ? FoldTypeTest(match) : FoldLeftAssoc(match);
                case "expr-shift": {
                        var acc = FoldExpr(match.Children[0]);
                        foreach (var tail in match.Children.Skip(1)) {
                            var right = FoldExpr(tail.Children[0]);
                            acc = tail.PatternName == "right"
                                ? new ShiftRight(acc, right)
                                : new ShiftLeft(acc, right);
                        }
                        return acc;
                    }
                case "expr-additive" or "expr-multiplicative":
                    return FoldLeftAssoc(match);
                case "expr-unary":
                    return FoldUnary(match);
                case "expr-postfix":
                    return FoldPostfix(match);
                case "expr-primary":
                    return FoldPrimary(match);
                default:
                    throw new InvalidOperationException($"Cannot fold expression rule '{match.RuleName}'.");
            }
        }

        private Node FoldTypeTest(MatchResult<CSharpToken, CSharpTokenKind> match) {
            var operand = FoldExpr(match.Children[0]);
            var type = FoldType(match.Children[1]);
            if (match.PatternName == "type-as")
                return new TypeAs(operand, type);
            return new TypeIs(operand, type) {
                VariableName = HasCapture(match, "var") ? Capture(match, "var") : null,
            };
        }

        private Node FoldLeftAssoc(MatchResult<CSharpToken, CSharpTokenKind> match) {
            var acc = FoldExpr(match.Children[0]);
            for (var i = 1; i < match.Children.Count; i++) {
                var right = FoldExpr(match.Children[i]);
                acc = match.Operators[i - 1].Kind switch {
                    CSharpTokenKind.QuestionQuestion => new Coalesce(acc, right),
                    CSharpTokenKind.PipePipe => new Or(acc, right),
                    CSharpTokenKind.AmpAmp => new And(acc, right),
                    CSharpTokenKind.Pipe => new BitwiseOr(acc, right),
                    CSharpTokenKind.Caret => new BitwiseXor(acc, right),
                    CSharpTokenKind.Amp => new BitwiseAnd(acc, right),
                    CSharpTokenKind.EqEq => new Equal(acc, right),
                    CSharpTokenKind.NotEq => new NotEqual(acc, right),
                    CSharpTokenKind.Lt => new LessThan(acc, right),
                    CSharpTokenKind.LtEq => new LessThanOrEqual(acc, right),
                    CSharpTokenKind.Gt => new GreaterThan(acc, right),
                    CSharpTokenKind.GtEq => new GreaterThanOrEqual(acc, right),
                    CSharpTokenKind.Plus => new Add(acc, right),
                    CSharpTokenKind.Minus => new Subtract(acc, right),
                    CSharpTokenKind.Star => new Multiply(acc, right),
                    CSharpTokenKind.Slash => new Divide(acc, right),
                    CSharpTokenKind.Percent => new Modulo(acc, right),
                    var op => throw Error($"Cannot fold binary operator '{op}'."),
                };
            }
            return acc;
        }

        private Node FoldUnary(MatchResult<CSharpToken, CSharpTokenKind> match) {
            if (match.PatternName == "postfix")
                return FoldExpr(match.Children[0]);
            var operand = FoldExpr(match.Children[0]);
            return match.PatternName switch {
                "unary-minus" => new UnaryMinus(operand),
                "not" => new Not(operand),
                "bitwise-not" => new BitwiseNot(operand),
                "await" => new Await(operand),
                _ => throw new InvalidOperationException($"Cannot fold unary '{match.PatternName}'."),
            };
        }

        private Node FoldPostfix(MatchResult<CSharpToken, CSharpTokenKind> match) {
            var acc = FoldExpr(match.Children[0]);
            var tails = match.Children.Skip(1).ToArray();
            return FoldPostfixTails(acc, tails, 0);
        }

        /// <summary>Folds the postfix tails from <paramref name="index"/> onto
        /// <paramref name="acc"/>. A <c>?.</c> tail short-circuits the rest of the
        /// chain, so the remaining tails fold inside the non-null branch of a
        /// <see cref="Conditional"/> rather than being applied to the (possibly
        /// null) receiver.</summary>
        private Node FoldPostfixTails(
            Node acc,
            IReadOnlyList<MatchResult<CSharpToken, CSharpTokenKind>> tails,
            int index) {
            if (index >= tails.Count)
                return acc;
            var tail = tails[index];
            switch (tail.PatternName) {
                case "member":
                    return FoldPostfixTails(new Member(acc, Capture(tail, "name")), tails, index + 1);
                case "invoke":
                    return FoldPostfixTails(
                        new Invoke(ChainNameof(acc), [.. FoldArgList(tail.Children[0])]), tails, index + 1);
                case "generic-invoke":
                    return FoldPostfixTails(
                        new Invoke(ChainNameof(acc), [.. FoldArgList(tail.Children[1])]) {
                            TypeArguments = FoldTypeArgs(tail.Children[0]),
                        }, tails, index + 1);
                case "index":
                    return FoldPostfixTails(
                        new IndexAccess(acc, tail.Children.Count > 0 ? [.. FoldArgList(tail.Children[0])] : []),
                        tails, index + 1);
                case "forgiving":
                    return FoldPostfixTails(new NullForgiving(acc), tails, index + 1);
                case "conditional-member":
                    return FoldConditionalMember(acc, tails, index, tail);
                case "conditional-index":
                    return FoldConditionalIndex(acc, tails, index, tail);
                default:
                    throw new InvalidOperationException($"Cannot fold postfix '{tail.PatternName}'.");
            }
        }

        private static Node ChainNameof(Node acc) =>
            acc is Variable { Name: "nameof" } ? new TypeReference("nameof") : acc;

        /// <summary>Folds <c>recv?.member</c> onto the single conditional-chain fold in
        /// <see cref="FoldConditionalChain"/>.</summary>
        private Node FoldConditionalMember(
            Node acc,
            IReadOnlyList<MatchResult<CSharpToken, CSharpTokenKind>> tails,
            int index,
            MatchResult<CSharpToken, CSharpTokenKind> tail) =>
            FoldConditionalChain(acc, tails, index, tail, new Member(acc, Capture(tail, "name")), "?.");

        /// <summary>Folds <c>recv?[index]</c> onto the single conditional-chain fold in
        /// <see cref="FoldConditionalChain"/>.</summary>
        private Node FoldConditionalIndex(
            Node acc,
            IReadOnlyList<MatchResult<CSharpToken, CSharpTokenKind>> tails,
            int index,
            MatchResult<CSharpToken, CSharpTokenKind> tail) =>
            FoldConditionalChain(
                acc, tails, index, tail,
                new IndexAccess(acc, tail.Children.Count > 0 ? [.. FoldArgList(tail.Children[0])] : []),
                "?[");

        /// <summary>Folds <c>recv?.member</c> (and <c>recv?[index]</c>) — and every tail
        /// that follows it — to <c>recv == null ? null : recv.member…</c>. The member (or
        /// element) is read only when the receiver is non-null, and the whole remainder
        /// of the chain is skipped when it is null, as in C# (so <c>a?.b.c</c>,
        /// <c>a?.b[c]</c> and <c>a?.M(args)</c> propagate the null). The desugaring
        /// re-evaluates the receiver, so it is sound only when the receiver has no side
        /// effects; an impure receiver fails closed rather than folding to a wrong
        /// shape.</summary>
        private Node FoldConditionalChain(
            Node acc,
            IReadOnlyList<MatchResult<CSharpToken, CSharpTokenKind>> tails,
            int index,
            MatchResult<CSharpToken, CSharpTokenKind> tail,
            Node access,
            string form) {
            if (!IsSideEffectFreeReceiver(acc))
                throw GrammarError.Error(
                    $"Conditional access ('{form}') requires a side-effect-free receiver in the exported subset.",
                    tail.Tokens[0].Line, tail.Tokens[0].Col);
            var inner = FoldPostfixTails(access, tails, index + 1);
            // `recv != null ? member : null` rather than `recv == null ? null : member`:
            // the analysis classifies a Conditional from its *true* branch, so the null
            // constant must be the false branch or the result types (and root-extracts)
            // as the null branch instead of the member.
            return new Conditional(new Not(new Equal(acc, new Constant(null))), inner, new Constant(null));
        }

        /// <summary>True when evaluating <paramref name="node"/> twice cannot change
        /// observable behaviour — the condition under which re-evaluating a
        /// conditional-access receiver is sound. Mirrors the classification in
        /// <c>Poly.Interpretation.Analysis.Semantics.SideEffectAnalyzer</c>: member
        /// reads, arithmetic, comparisons and their operand shapes are pure;
        /// assignments, calls, indexers, allocations, control flow and suspension
        /// are not.</summary>
        private static bool IsSideEffectFreeReceiver(Node node) => node switch {
            Assignment or Invoke or IndexAccess or New or NewArray or SuspendNode or Return or Await
                or WhileLoop or DoWhileLoop or ForLoop or ForEachLoop
                or IfStatement or SwitchStatement or ThrowStatement
                or TryCatchFinally or UsingStatement
                or BreakStatement or ContinueStatement or GotoStatement or LabelDeclaration => false,
            _ => node.Children.All(child => child is null || IsSideEffectFreeReceiver(child)),
        };

        private Node FoldPrimary(MatchResult<CSharpToken, CSharpTokenKind> match) {
            switch (match.PatternName) {
                case "number":
                    return new Constant(FoldNumber(Capture(match, "value")));
                case "string":
                    return new Constant(Capture(match, "value"));
                case "interpolated":
                    return FoldInterpolatedString(CapturedToken(match, "value"));
                case "char":
                    return new Constant(Capture(match, "value").Length == 1 ? Capture(match, "value")[0] : '\0');
                case "true":
                    return new Constant(true);
                case "false":
                    return new Constant(false);
                case "null":
                    return new Constant(null);
                case "ident":
                    return new Variable(Capture(match, "name"));
                case "this":
                    return new ThisReference();
                case "base":
                    return new BaseConstructorInvocationNode([]);
                case "parenthesized":
                    return FoldExpr(match.Children[0]);
                case "cast":
                    return new TypeCast(FoldExpr(match.Children[1]), FoldType(match.Children[0]));
                case "typeof":
                    return new TypeOf(new TypeReference(SourceText(match.Children[0])));
                case "default-typed":
                    return new Default(FoldType(match.Children[0]));
                case "default-bare":
                    return new Default();
                case "new":
                    return new New(FoldType(match.Children[0]), [.. FoldArgList(match.Children[1])]);
                case "new-array":
                    return new NewArray(FoldType(match.Children[0]), FoldExpr(match.Children[1]));
                case "lambda-ident": {
                        var parameter = new Parameter(Capture(match, "param"));
                        var body = FoldExpr(match.Children[^1]);
                        return new Lambda([parameter], body);
                    }
                case "lambda-parens":
                    return new Lambda(FoldLambdaParams(MaybeChild(match, "lambda-param-list")), FoldExpr(match.Children[^1]));
                case "lambda-block-ident": {
                        var parameter = new Parameter(Capture(match, "param"));
                        return new Lambda([parameter], FoldBlock(Child(match, "block")));
                    }
                case "lambda-block-parens":
                    return new Lambda(FoldLambdaParams(MaybeChild(match, "lambda-param-list")), FoldBlock(Child(match, "block")));
                case "generic-name": {
                        var name = Capture(match, "name");
                        var arguments = FoldTypeArgs(Child(match, "type-args"));
                        return new NamedTypeReference(name, TypeArguments: arguments);
                    }
                case "type-keyword":
                    return FoldTypeCore(match.Children[0]);
                default:
                    throw new InvalidOperationException($"Cannot fold primary '{match.PatternName}'.");
            }
        }

        /// <summary>
        /// Folds <c>$"…"</c> from its raw token body into literal segments and
        /// expression holes. <c>{{</c>/<c>}}</c> are literal braces; every hole
        /// expression is parsed with the same <c>expr</c> rule the rest of the
        /// reader uses, so a hole is a real expression node. Anything the hole
        /// parser cannot consume whole — format specifiers (<c>{x:N2}</c>),
        /// alignment (<c>{x,10}</c>), tuples — fails closed with a positioned error
        /// instead of folding to a wrong shape.
        /// </summary>
        private Node FoldInterpolatedString(CSharpToken token) {
            var raw = token.Text;
            var literals = new List<string>();
            var holes = new List<Poly.Ast.Nodes.Expression>();
            var literal = new StringBuilder();
            var i = 0;
            while (i < raw.Length) {
                var ch = raw[i];
                if (ch == '{') {
                    if (i + 1 < raw.Length && raw[i + 1] == '{') {
                        literal.Append('{');
                        i += 2;
                        continue;
                    }
                    literals.Add(CSharpTokenizer.DecodeStringBody(literal.ToString()));
                    literal.Clear();
                    var holeStart = i + 1;
                    var holeEnd = FindInterpolationHoleEnd(raw, holeStart);
                    if (holeEnd < 0)
                        throw GrammarError.Error("Unterminated interpolation hole.", token.Line, token.Col);
                    holes.Add(ReadInterpolationHole(raw[holeStart..holeEnd], token, holeStart));
                    i = holeEnd + 1;
                    continue;
                }
                if (ch == '}') {
                    if (i + 1 < raw.Length && raw[i + 1] == '}') {
                        literal.Append('}');
                        i += 2;
                        continue;
                    }
                    throw GrammarError.Error(
                        "Unmatched '}' in interpolated string; a literal brace is written '}}'.",
                        token.Line, token.Col);
                }
                literal.Append(ch);
                i++;
            }
            literals.Add(CSharpTokenizer.DecodeStringBody(literal.ToString()));
            return new InterpolatedString(literals, holes);
        }

        /// <summary>Index of the <c>}</c> that closes a hole opened at
        /// <paramref name="start"/>, or -1. Quotes cannot occur inside a hole: the
        /// tokenizer would have ended the interpolated token there. A char literal can
        /// (the writer prints holes verbatim), so it is skipped whole — otherwise a
        /// <c>'}'</c> inside it would end the hole early and break write→read.</summary>
        private static int FindInterpolationHoleEnd(string raw, int start) {
            var depth = 1;
            for (var i = start; i < raw.Length; i++) {
                var ch = raw[i];
                if (ch == '\'') {
                    i++;
                    while (i < raw.Length && raw[i] != '\'') {
                        if (raw[i] == '\\')
                            i++;
                        i++;
                    }
                    continue;
                }
                if (ch == '{') depth++;
                else if (ch == '}' && --depth == 0) return i;
            }
            return -1;
        }

        private Poly.Ast.Nodes.Expression ReadInterpolationHole(string holeText, CSharpToken token, int offsetInRaw) {
            var absoluteCol = token.Col + 2 + offsetInRaw;
            if (holeText.Trim().Length == 0)
                throw GrammarError.Error("Empty interpolation hole '{}'.", token.Line, absoluteCol);
            var sub = new Parser(holeText);
            var folded = sub.FoldExpr(sub.Take("expr", "interpolation hole expression"));
            if (sub.Current.Kind != CSharpTokenKind.EndOfFile)
                throw GrammarError.Error(
                    $"Unsupported interpolation hole '{{{holeText}}}'; format specifiers and alignment are not part of the exported subset.",
                    token.Line, absoluteCol + sub.Current.Col - 1);
            return folded as Poly.Ast.Nodes.Expression
                ?? throw GrammarError.Error(
                    $"Interpolation hole '{{{holeText}}}' is not a value expression.", token.Line, absoluteCol);
        }

        private static object FoldNumber(string text) {
            var clean = text.Replace("_", "", StringComparison.Ordinal);
            // A hex/binary literal never has a `.` or an exponent, and its `E`/`D`/`F`
            // letters are digits — so the radix path must run before the float checks.
            if (clean.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                || clean.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
                return FoldRadixNumber(clean[2..], clean[1] is 'x' or 'X' ? 16 : 2);
            if (clean.EndsWith('f') || clean.EndsWith('F'))
                return float.Parse(clean[..^1], NumberStyles.Float, CultureInfo.InvariantCulture);
            if (clean.EndsWith('m') || clean.EndsWith('M'))
                return decimal.Parse(clean[..^1], NumberStyles.Float, CultureInfo.InvariantCulture);
            if (clean.EndsWith('d') || clean.EndsWith('D'))
                return double.Parse(clean[..^1], NumberStyles.Float, CultureInfo.InvariantCulture);
            if (clean.EndsWith("UL", StringComparison.OrdinalIgnoreCase) || clean.EndsWith("LU", StringComparison.OrdinalIgnoreCase))
                return ulong.Parse(clean[..^2], NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (clean.EndsWith('l') || clean.EndsWith('L'))
                return long.Parse(clean[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (clean.EndsWith('u') || clean.EndsWith('U'))
                return uint.Parse(clean[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (clean.Contains('.') || clean.Contains('e') || clean.Contains('E'))
                return double.Parse(clean, NumberStyles.Float, CultureInfo.InvariantCulture);
            // An unsuffixed integer literal is `int` when it fits (C#), so the int must
            // not be widened by a ternary's common type — `1 is int` depends on it.
            if (int.TryParse(clean, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
                return intValue;
            return long.Parse(clean, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Folds the digits and suffix of a <c>0x</c>/<c>0b</c> literal. The tokenizer
        /// has already accepted the digit run, so a malformed value here is a
        /// conversion throw, never a silently wrong constant. Unsuffixed literals take
        /// the smallest C# integer type that holds them, so <c>0xFF</c> stays <c>int</c>.
        /// </summary>
        private static object FoldRadixNumber(string body, int radix) {
            var suffix = "";
            if (body.EndsWith("UL", StringComparison.OrdinalIgnoreCase)
                || body.EndsWith("LU", StringComparison.OrdinalIgnoreCase)) {
                suffix = "ul";
                body = body[..^2];
            }
            else if (body.EndsWith('u') || body.EndsWith('U')) {
                suffix = "u";
                body = body[..^1];
            }
            else if (body.EndsWith('l') || body.EndsWith('L')) {
                suffix = "l";
                body = body[..^1];
            }
            var value = Convert.ToUInt64(body, radix);
            return suffix switch {
                "ul" => value,
                "u" => value <= uint.MaxValue ? (uint)value : value,
                "l" => value <= long.MaxValue ? (long)value : value,
                _ => value <= int.MaxValue ? (int)value
                    : value <= uint.MaxValue ? (uint)value
                    : value <= long.MaxValue ? (long)value
                    : value,
            };
        }
    }
}