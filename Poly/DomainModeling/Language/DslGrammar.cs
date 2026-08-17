using Poly.Grammar;

namespace Poly.DomainModeling.Language;

/// <summary>
/// Product Poly DSL as a <see cref="Grammar{TToken,TTokenKind}"/> pattern table.
/// Rule/pattern surface and element names are the engine's; handlers in
/// PolyDslParser / DslExpressionParser fold matches into IR.
/// </summary>
public static class DslGrammar {
    /// <summary>True when <paramref name="kind"/> is a primitive property type keyword.</summary>
    public static bool IsPrimitiveTypeKind(DslTokenKind kind) => kind is
        DslTokenKind.Text or DslTokenKind.NumberType or DslTokenKind.BooleanType
        or DslTokenKind.DateTimeType or DslTokenKind.DateType;

    /// <summary>True when <paramref name="kind"/> is a comparison operator (product <c>ParseComparison</c> set).</summary>
    public static bool IsCompareOpKind(DslTokenKind kind) => kind is
        DslTokenKind.Is or DslTokenKind.Eq or DslTokenKind.Neq
        or DslTokenKind.Gt or DslTokenKind.Gte or DslTokenKind.Lt or DslTokenKind.Lte;

    /// <summary>Token-predicate wrappers (predicates see the full token).</summary>
    public static bool IsPrimitiveToken(DslToken t) => IsPrimitiveTypeKind(t.Kind);
    public static bool IsCompareToken(DslToken t) => IsCompareOpKind(t.Kind);

    /// <summary>
    /// Canonical text for a fixed DSL token kind — the tokenizer's inverse. Used by
    /// the grammar <see cref="Printer{TToken,TTokenKind}"/> emit surface; value-bearing
    /// kinds (Identifier, Number, StringLiteral) are content positions and have no
    /// canonical text (handlers supply values via print callbacks).
    /// </summary>
    public static string CanonicalText(DslTokenKind kind) => kind switch {
        DslTokenKind.Colon => ":",
        DslTokenKind.Comma => ",",
        DslTokenKind.Dot => ".",
        DslTokenKind.LParen => "(",
        DslTokenKind.RParen => ")",
        DslTokenKind.LBrace => "{",
        DslTokenKind.RBrace => "}",
        DslTokenKind.LBracket => "[",
        DslTokenKind.RBracket => "]",
        DslTokenKind.Arrow => "->",
        DslTokenKind.Gt => ">",
        DslTokenKind.Gte => ">=",
        DslTokenKind.Lt => "<",
        DslTokenKind.Lte => "<=",
        DslTokenKind.Eq => "==",
        DslTokenKind.Neq => "!=",
        DslTokenKind.Plus => "+",
        DslTokenKind.Minus => "-",
        DslTokenKind.Star => "*",
        DslTokenKind.Slash => "/",
        DslTokenKind.Is => "is",
        DslTokenKind.Not => "not",
        DslTokenKind.And => "and",
        DslTokenKind.Or => "or",
        DslTokenKind.Assign => "assign",
        DslTokenKind.To => "to",
        DslTokenKind.Transition => "transition",
        DslTokenKind.When => "when",
        DslTokenKind.Require => "require",
        DslTokenKind.If => "if",
        DslTokenKind.Else => "else",
        DslTokenKind.Domain => "domain",
        DslTokenKind.Uses => "uses",
        DslTokenKind.Entity => "entity",
        DslTokenKind.Stage => "stage",
        DslTokenKind.Action => "action",
        DslTokenKind.Policy => "policy",
        DslTokenKind.True => "true",
        DslTokenKind.False => "false",
        DslTokenKind.Null => "null",
        DslTokenKind.Text => "Text",
        DslTokenKind.NumberType => "Number",
        DslTokenKind.BooleanType => "Boolean",
        DslTokenKind.DateTimeType => "DateTime",
        DslTokenKind.DateType => "Date",
        DslTokenKind.Required => "required",
        DslTokenKind.Unique => "unique",
        DslTokenKind.Range => "range",
        DslTokenKind.Length => "length",
        DslTokenKind.Pattern => "pattern",
        DslTokenKind.Enum => "enum",
        DslTokenKind.Equals => "equals",
        DslTokenKind.Relationship => "relationship",
        DslTokenKind.From => "from",
        DslTokenKind.One => "one",
        DslTokenKind.Many => "many",
        DslTokenKind.Owned => "owned",
        DslTokenKind.Create => "create",
        DslTokenKind.In => "in",
        DslTokenKind.Invoke => "invoke",
        DslTokenKind.For => "for",
        DslTokenKind.Entry => "entry",
        DslTokenKind.Exit => "exit",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, $"No canonical text for DSL token kind '{kind}'"),
    };

    private static readonly Grammar<DslToken, DslTokenKind> CoreTable = CreateCore();

    /// <summary>Immutable product table. Libraries <see cref="Grammar{TToken,TTokenKind}.Extend"/> this.</summary>
    public static Grammar<DslToken, DslTokenKind> Core => CoreTable;

    /// <summary>
    /// Product table plus optional library contributions. No configure returns
    /// the shared <see cref="Core"/>. Contributors mutate one builder; freeze once.
    /// </summary>
    public static Grammar<DslToken, DslTokenKind> Build(
        Action<GrammarBuilder<DslToken, DslTokenKind>>? configure = null) =>
        configure is null ? CoreTable : CoreTable.Extend(configure);

    /// <summary>Product table plus annotation and expression-form contributions.</summary>
    public static Grammar<DslToken, DslTokenKind> For(
        AnnotationRegistry annotations,
        ExpressionFormRegistry forms) {
        ArgumentNullException.ThrowIfNull(annotations);
        ArgumentNullException.ThrowIfNull(forms);
        var builder = CoreTable.ToBuilder();
        annotations.ContributePatterns(builder);
        forms.ContributeGrammarPatterns(builder);
        return builder.Build();
    }

    /// <summary>Parse/print pair — one table, one printer.</summary>
    public static Language<DslToken, DslTokenKind> LanguageFor(
        AnnotationRegistry annotations,
        ExpressionFormRegistry forms) =>
        new(For(annotations, forms), CanonicalText, () => new DslTokenWriter());

    private static Grammar<DslToken, DslTokenKind> CreateCore() =>
        new GrammarBuilder<DslToken, DslTokenKind>()
        .Define("document")
            .Pattern("header").Kind(DslTokenKind.Domain).Value(DslTokenKind.Identifier, "name").Commit()
        .Define("uses")
            .Pattern("id").Kind(DslTokenKind.Uses).Value(DslTokenKind.Identifier, "id").Commit()
        .Define("top")
            .Pattern("enum")
                .Value(DslTokenKind.Identifier).Kind(DslTokenKind.Colon).Kind(DslTokenKind.Enum)
                .Commit()
            .Pattern("entity")
                .Value(DslTokenKind.Identifier).Kind(DslTokenKind.Colon).Kind(DslTokenKind.Entity)
                .Commit()
        .Define("entity-body")
            .Pattern("entity-subscription")
                .Kind(DslTokenKind.When).Value(DslTokenKind.Identifier)
                .Commit()
            .Pattern("stage")
                .Value(DslTokenKind.Identifier).Kind(DslTokenKind.Colon).Kind(DslTokenKind.Stage)
                .Commit()
            .Pattern("action")
                .Value(DslTokenKind.Identifier).Kind(DslTokenKind.Colon).Kind(DslTokenKind.Action)
                .Commit()
            .Pattern("policy")
                .Value(DslTokenKind.Identifier).Kind(DslTokenKind.Colon).Kind(DslTokenKind.Policy)
                .Commit()
            .Pattern("legacy-action")
                .Value(DslTokenKind.Identifier).Kind(DslTokenKind.LParen)
                .Commit()
            .Pattern("typed-line")
                .Value(DslTokenKind.Identifier).Kind(DslTokenKind.Colon).Value(DslTokenKind.Identifier)
                .Commit()
            .Pattern("property")
                .Value(DslTokenKind.Identifier).Kind(DslTokenKind.Colon).Predicate(IsPrimitiveToken, "primitive-type")
                .Commit()
            .Pattern("nav-many")
                .Value(DslTokenKind.Identifier).Kind(DslTokenKind.Colon).Kind(DslTokenKind.Many)
                .Commit()
            .Pattern("nav-one")
                .Value(DslTokenKind.Identifier).Kind(DslTokenKind.Colon).Kind(DslTokenKind.One)
                .Commit()
            .Pattern("nav-owned")
                .Value(DslTokenKind.Identifier).Kind(DslTokenKind.Colon).Kind(DslTokenKind.Owned)
                .Commit()
            .Pattern("primitive-name")
                .Predicate(IsPrimitiveToken, "primitive-type").Kind(DslTokenKind.Colon)
                .Commit()
        .Define("stage-body")
            .Pattern("entry")
                .Kind(DslTokenKind.Entry)
                .Commit()
            .Pattern("exit")
                .Kind(DslTokenKind.Exit)
                .Commit()
            .Pattern("subscription")
                .Kind(DslTokenKind.When).Value(DslTokenKind.Identifier)
                .Commit()
            .Pattern("stage-action")
                .Value(DslTokenKind.Identifier)
                .Commit()
        .Define("annotation")
            .Pattern("no-args")
                .Value(DslTokenKind.Identifier).Kind(DslTokenKind.LParen).Kind(DslTokenKind.RParen)
                .Commit()
            .Pattern("with-args")
                .Value(DslTokenKind.Identifier).Kind(DslTokenKind.LParen).Repeat("annotation-args").Kind(DslTokenKind.RParen)
                .Commit()
        .Define("annotation-args")
            .Pattern("str").Optional(DslTokenKind.Comma).Kind(DslTokenKind.StringLiteral).Commit()
            .Pattern("num").Optional(DslTokenKind.Comma).Kind(DslTokenKind.Number).Commit()
            .Pattern("tru").Optional(DslTokenKind.Comma).Kind(DslTokenKind.True).Commit()
            .Pattern("fal").Optional(DslTokenKind.Comma).Kind(DslTokenKind.False).Commit()
            .Pattern("nul").Optional(DslTokenKind.Comma).Kind(DslTokenKind.Null).Commit()
        .Define("expr")
            .Pattern("top").Ref("expr-or").Commit()
        .Define("expr-or")
            .Pattern("chain").LeftAssoc("expr-and", DslTokenKind.Or).Commit()
        .Define("expr-and")
            .Pattern("chain").LeftAssoc("expr-not", DslTokenKind.And).Commit()
        .Define("expr-not")
            .Pattern("not").Kind(DslTokenKind.Not).Ref("expr-add").Commit()
            .Pattern("pass-through").Ref("expr-compare").Commit()
        .Define("expr-compare")
            .Pattern("bare").Ref("expr-add-no-not").Commit()
            .Pattern("with-op")
                .Ref("expr-add-no-not")
                .Predicate(IsCompareToken, "compare-op")
                .Ref("expr-add").Commit()
        .Define("expr-add-no-not")
            .Pattern("chain").LeftAssoc("expr-mul-no-not", DslTokenKind.Plus, DslTokenKind.Minus).Commit()
        .Define("expr-mul-no-not")
            .Pattern("chain").LeftAssoc("expr-primary-no-not", DslTokenKind.Star, DslTokenKind.Slash).Commit()
        .Define("expr-primary-no-not")
            .Pattern("number").Kind(DslTokenKind.Number).Commit()
            .Pattern("string").Kind(DslTokenKind.StringLiteral).Commit()
            .Pattern("true").Kind(DslTokenKind.True).Commit()
            .Pattern("false").Kind(DslTokenKind.False).Commit()
            .Pattern("null").Kind(DslTokenKind.Null).Commit()
            .Pattern("group").Kind(DslTokenKind.LParen).Ref("expr").Kind(DslTokenKind.RParen).Commit()
            .Pattern("ident").Value(DslTokenKind.Identifier).Commit()
        .Define("expr-add")
            .Pattern("chain").LeftAssoc("expr-mul", DslTokenKind.Plus, DslTokenKind.Minus).Commit()
        .Define("expr-mul")
            .Pattern("chain").LeftAssoc("expr-primary", DslTokenKind.Star, DslTokenKind.Slash).Commit()
        .Define("expr-or-op")
            .Pattern("or").Kind(DslTokenKind.Or).Commit()
        .Define("expr-and-op")
            .Pattern("and").Kind(DslTokenKind.And).Commit()
        .Define("expr-not-op")
            .Pattern("not").Kind(DslTokenKind.Not).Commit()
        .Define("expr-add-op")
            .Pattern("plus").Kind(DslTokenKind.Plus).Commit()
            .Pattern("minus").Kind(DslTokenKind.Minus).Commit()
        .Define("expr-mul-op")
            .Pattern("star").Kind(DslTokenKind.Star).Commit()
            .Pattern("slash").Kind(DslTokenKind.Slash).Commit()
        .Define("expr-compare-op")
            .Pattern("op").Predicate(IsCompareToken, "compare-op").Commit()
        .Define("effect")
            .Pattern("transition")
                .Kind(DslTokenKind.Transition).Kind(DslTokenKind.To).Value(DslTokenKind.Identifier).Commit()
            .Pattern("assign")
                .Kind(DslTokenKind.Assign).Value(DslTokenKind.Identifier).Kind(DslTokenKind.To).Commit()
            .Pattern("create-in")
                .Kind(DslTokenKind.Create).Kind(DslTokenKind.In).Value(DslTokenKind.Identifier).Commit()
            .Pattern("create")
                .Kind(DslTokenKind.Create).Value(DslTokenKind.Identifier).Commit()
            .Pattern("invoke")
                .Kind(DslTokenKind.Invoke).Commit()
            .Pattern("for")
                .Kind(DslTokenKind.For).Commit()
            .Pattern("if")
                .Kind(DslTokenKind.If).Kind(DslTokenKind.LParen).Commit()
        .Define("expr-primary")
            .Pattern("number").Value(DslTokenKind.Number).Commit()
            .Pattern("string").Value(DslTokenKind.StringLiteral).Commit()
            .Pattern("true").Kind(DslTokenKind.True).Commit()
            .Pattern("false").Kind(DslTokenKind.False).Commit()
            .Pattern("null").Kind(DslTokenKind.Null).Commit()
            .Pattern("group").Kind(DslTokenKind.LParen).Ref("expr").Kind(DslTokenKind.RParen).Commit()
            .Pattern("not").Kind(DslTokenKind.Not).Ref("expr-add").Commit()
            .Pattern("ident").Value(DslTokenKind.Identifier).Commit()
            .Build();
}