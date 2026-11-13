using Poly.DomainModeling.Libraries.Temporal;
using Poly.DomainModeling.Meaning;
using Poly.Grammar;

namespace Poly.DomainModeling.Language;

/// <summary>
/// Product Poly DSL as a <see cref="Grammar{TToken,TTokenKind}"/> pattern table.
/// Rule/pattern surface and element names are the engine's; handlers in
/// DomainResolver / DslExpressionParser fold matches into IR.
/// </summary>
public static class DslGrammar {
    /// <summary>True when <paramref name="kind"/> is a primitive property type keyword.</summary>
    public static bool IsPrimitiveTypeKind(DslTokenKind kind) => kind is
        DslTokenKind.Text or DslTokenKind.NumberType or DslTokenKind.BooleanType;

    /// <summary>
    /// True when a word of <paramref name="kind"/> and <paramref name="text"/> names a primitive type:
    /// a primitive keyword (<c>Text</c>, <c>Number</c>, <c>Boolean</c>), or an identifier
    /// <paramref name="isKnownPrimitiveName"/> knows, such as <c>Date</c>. A property named after a
    /// primitive keyword (<c>Number: Text</c>) must have such a type.
    /// </summary>
    public static bool IsPrimitiveType(DslTokenKind kind, string text, Func<string, bool> isKnownPrimitiveName) =>
        IsPrimitiveTypeKind(kind) || (kind == DslTokenKind.Identifier && isKnownPrimitiveName(text));

    /// <summary>True when <paramref name="kind"/> is a comparison operator (product <c>ParseComparison</c> set).</summary>
    public static bool IsCompareOpKind(DslTokenKind kind) => kind is
        DslTokenKind.Is or DslTokenKind.Eq or DslTokenKind.Neq
        or DslTokenKind.Gt or DslTokenKind.Gte or DslTokenKind.Lt or DslTokenKind.Lte;

    /// <summary>Token-predicate wrappers (predicates see the full token).</summary>
    public static bool IsPrimitiveToken(DslToken t) => IsPrimitiveTypeKind(t.Kind);

    /// <summary>The value-type kind word. It stays an identifier so <c>value</c> can still be a name.</summary>
    public static bool IsValueWord(DslToken t) =>
        t.Kind == DslTokenKind.Identifier && t.TextEquals("value");
    public static bool IsCompareToken(DslToken t) => IsCompareOpKind(t.Kind);

    /// <summary>
    /// The DSL's same-line run (a <see cref="Run{TToken,TTokenKind}"/> delegate): <paramref name="candidate"/>
    /// is on <paramref name="previous"/>'s line and, from the run's second token (<paramref name="index"/> 1
    /// on), starts where <paramref name="previous"/> ends.
    /// </summary>
    public static bool AdjacentSameLine(DslToken previous, DslToken candidate, int index) =>
        candidate.Line == previous.Line
        && (index == 0 || candidate.Start == previous.Start + previous.Length);

    /// <summary>
    /// One piece of a <c>uses</c> pin (<c>v1</c>, <c>1.2.3</c>, <c>"v1"</c>, <c>-2</c>): an identifier, number,
    /// string literal, minus or dot, on the same line as the token before it and adjacent to it after the first.
    /// </summary>
    public static bool ContinuesUsePin(DslToken previous, DslToken candidate, int index) =>
        candidate.Kind is (DslTokenKind.Identifier or DslTokenKind.Number or DslTokenKind.StringLiteral
            or DslTokenKind.Minus or DslTokenKind.Dot)
        && AdjacentSameLine(previous, candidate, index);

    /// <summary>
    /// A contextual word (<c>via</c>, <c>initial</c>, <c>key</c>, <c>external</c>, <c>inbound</c>, <c>value</c>):
    /// an identifier spelled <paramref name="word"/>, never a keyword kind with the same text. It prints as
    /// that identifier.
    /// </summary>
    public static PatternBuilder<DslToken, DslTokenKind> Word(this PatternBuilder<DslToken, DslTokenKind> builder, string word) {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(word);
        return builder.Word(DslTokenKind.Identifier, word, t => t.Kind == DslTokenKind.Identifier && t.TextEquals(word));
    }

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
        DslTokenKind.SingleEq => "=",
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
        DslTokenKind.Optional => "optional",
        DslTokenKind.Unique => "unique",
        DslTokenKind.Range => "range",
        DslTokenKind.Length => "length",
        DslTokenKind.Pattern => "pattern",
        DslTokenKind.Enum => "enum",
        DslTokenKind.Equals => "default",
        DslTokenKind.Equality => "equals",
        DslTokenKind.Relationship => "relationship",
        DslTokenKind.From => "from",
        DslTokenKind.Link => "link",
        DslTokenKind.Unlink => "unlink",
        DslTokenKind.One => "one",
        DslTokenKind.Many => "many",
        DslTokenKind.Owned => "owned",
        DslTokenKind.Create => "create",
        DslTokenKind.In => "in",
        DslTokenKind.Invoke => "invoke",
        DslTokenKind.For => "for",
        DslTokenKind.Entry => "entry",
        DslTokenKind.Exit => "exit",
        DslTokenKind.As => "as",
        DslTokenKind.Public => "public",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, $"No canonical text for DSL token kind '{kind}'"),
    };

    private static readonly Grammar<DslToken, DslTokenKind> CoreTable = CreateCore();

    /// <summary>Immutable product table. Spell is constant; libraries do not Extend this.</summary>
    public static Grammar<DslToken, DslTokenKind> Core => CoreTable;

    /// <summary>
    /// Product table plus optional extra patterns (tests). No configure returns
    /// the shared <see cref="Core"/>. Contributors mutate one builder; freeze once.
    /// </summary>
    public static Grammar<DslToken, DslTokenKind> Build(
        System.Action<GrammarBuilder<DslToken, DslTokenKind>>? configure = null) =>
        configure is null ? CoreTable : CoreTable.Extend(configure);

    /// <summary>Product table. Extra productions only when a registry still has test contributors.</summary>
    public static Grammar<DslToken, DslTokenKind> For(
        AnnotationRegistry annotations,
        ExpressionFormRegistry forms) {
        ArgumentNullException.ThrowIfNull(annotations);
        ArgumentNullException.ThrowIfNull(forms);
        if (!annotations.HasGrammarContributors && !forms.HasGrammarContributors)
            return CoreTable;
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

    private static Grammar<DslToken, DslTokenKind> CreateCore() => new GrammarBuilder<DslToken, DslTokenKind>()
        .Define("document")
            .Pattern("header").Kind(DslTokenKind.Domain).Value(DslTokenKind.Identifier, "name").Commit()
        .Define("uses")
            .Pattern("id").Kind(DslTokenKind.Uses).Value(DslTokenKind.Identifier, "id").Commit()
            // The pin is a raw same-line token run (DslSyntaxTree.TryReadUse), not one token;
            // the Identifier position is this table's print hole, filled with the run's text.
            .Pattern("pinned")
                .Kind(DslTokenKind.Uses).Value(DslTokenKind.Identifier, "id")
                .Value(DslTokenKind.Identifier, "version").Commit()
        .Define("visibility")
            .Pattern("public").Kind(DslTokenKind.Public).Commit()
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
            .Pattern("marked-action", priority: 2)
                .Ref("visibility").Kind(DslTokenKind.Action).Value(DslTokenKind.Identifier, "name")
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
            .Pattern("marked-stage-action", priority: 2)
                .Ref("visibility").Kind(DslTokenKind.Action).Value(DslTokenKind.Identifier, "name")
                .Commit()
            .Pattern("stage-action")
                .Value(DslTokenKind.Identifier)
                .Commit()
        .Define("annotation")
            .Pattern("with-args")
                .Value(DslTokenKind.Identifier).Kind(DslTokenKind.LParen).Repeat("annotation-args").Kind(DslTokenKind.RParen)
                .Commit()
        .Define("annotation-args")
            .Pattern("str").Optional(DslTokenKind.Comma).Kind(DslTokenKind.StringLiteral).Commit()
            .Pattern("num").Optional(DslTokenKind.Comma).Kind(DslTokenKind.Number).Commit()
            .Pattern("tru").Optional(DslTokenKind.Comma).Kind(DslTokenKind.True).Commit()
            .Pattern("fal").Optional(DslTokenKind.Comma).Kind(DslTokenKind.False).Commit()
            .Pattern("nul").Optional(DslTokenKind.Comma).Kind(DslTokenKind.Null).Commit()
        .Define("path-hop")
            .Pattern("hop").Predicate(IsPathHopIdent, "hop").NotFollowedBy(DslTokenKind.Colon).Commit()
        .Define("effect")
            .Pattern("transition")
                .Kind(DslTokenKind.Transition).Kind(DslTokenKind.To).Value(DslTokenKind.Identifier).Commit()
            .Pattern("assign")
                .Kind(DslTokenKind.Assign).Value(DslTokenKind.Identifier).Kind(DslTokenKind.To).Commit()
            .Pattern("link")
                .Kind(DslTokenKind.Link).Value(DslTokenKind.Identifier).Kind(DslTokenKind.To).Commit()
            .Pattern("unlink")
                .Kind(DslTokenKind.Unlink).Value(DslTokenKind.Identifier).Kind(DslTokenKind.From).Commit()
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
            .Pattern("number").Value(DslTokenKind.Number).Fold(ExpressionFoldTable.Number).Commit()
            .Pattern("string").Value(DslTokenKind.StringLiteral).Fold(ExpressionFoldTable.String).Commit()
            .Pattern("true").Kind(DslTokenKind.True).Fold(ExpressionFoldTable.True).Commit()
            .Pattern("false").Kind(DslTokenKind.False).Fold(ExpressionFoldTable.False).Commit()
            .Pattern("null").Kind(DslTokenKind.Null).Fold(ExpressionFoldTable.Null).Commit()
            .Pattern("group").Kind(DslTokenKind.LParen).Commit()
            .Pattern("not").Kind(DslTokenKind.Not).Commit()
            .Pattern("duration").Value(DslTokenKind.Number, "amount").Predicate(IsDurationUnitToken, "unit").NotFollowedBy(DslTokenKind.Colon).Commit()
            .Pattern("ident").Value(DslTokenKind.Identifier).Fold(ExpressionFoldTable.Ident).Commit()
            .Pattern("match", priority: 3)
                .Predicate(IsMatchIdent, "kw").Balanced(DslTokenKind.LBrace, DslTokenKind.RBrace).Commit()
            .Pattern("call", priority: 3)
                .Predicate(IsPathHopIdent, "fn").Balanced(DslTokenKind.LParen, DslTokenKind.RParen).Commit()
            .Pattern("quant-where", priority: 2)
                .Predicate(IsQuantifierToken, "quant").Value(DslTokenKind.Identifier, "rel")
                .Predicate(IsWhereIdent, "where").Commit()
            .Pattern("count-bare", priority: 2)
                .Predicate(IsCountToken, "quant").Value(DslTokenKind.Identifier, "rel").Commit()
            .Pattern("sum-prop", priority: 2)
                .Predicate(IsSumToken, "quant")
                .Value(DslTokenKind.Identifier, "rel")
                .Predicate(IsPathHopIdent, "prop")
                .Commit()
            .Pattern("exists", priority: 1)
                .Value(DslTokenKind.Identifier, "rel").Predicate(IsExistsIdent, "exists").Commit()
            .Pattern("where-nav", priority: 1)
                .Value(DslTokenKind.Identifier, "rel").Predicate(IsWhereIdent, "where").Commit()
            .Pattern("path-is-not", priority: 1)
                .Predicate(IsPathHopIdent, "rel").Predicate(IsPathHopIdent, "prop")
                .NotFollowedBy(DslTokenKind.Colon).Repeat("path-hop")
                .Kind(DslTokenKind.Is).Kind(DslTokenKind.Not).Commit()
            .Pattern("path-cmp", priority: 1)
                .Predicate(IsPathHopIdent, "rel").Predicate(IsPathHopIdent, "prop")
                .NotFollowedBy(DslTokenKind.Colon).Repeat("path-hop")
                .Predicate(IsCompareToken, "op").Commit()
            .Pattern("path")
                .Predicate(IsPathHopIdent, "rel").Predicate(IsPathHopIdent, "prop")
                .NotFollowedBy(DslTokenKind.Colon).Repeat("path-hop").Commit()
        .Define("date-operation")
            .Pattern("add")
                .Ref("expr-primary").Kind(DslTokenKind.Plus)
                .Value(DslTokenKind.Number, "amount").Predicate(IsDurationUnitToken, "unit").NotFollowedBy(DslTokenKind.Colon)
                .Commit()
            .Pattern("sub")
                .Ref("expr-primary").Kind(DslTokenKind.Minus)
                .Value(DslTokenKind.Number, "amount").Predicate(IsDurationUnitToken, "unit").NotFollowedBy(DslTokenKind.Colon)
                .Commit()
            .Build();

    internal static bool IsMatchIdent(DslToken t) =>
        t.Kind == DslTokenKind.Identifier
        && t.TextEquals("match", StringComparison.Ordinal);

    internal static bool IsExistsIdent(DslToken t) =>
        t.Kind == DslTokenKind.Identifier
        && t.TextEquals("exists", StringComparison.OrdinalIgnoreCase);

    internal static bool IsWhereIdent(DslToken t) =>
        t.Kind == DslTokenKind.Identifier
        && t.TextEquals("where", StringComparison.OrdinalIgnoreCase);

    internal static bool IsQuantifierToken(DslToken t) =>
        t.Kind == DslTokenKind.Identifier && IsQuantifierKeyword(t.Span);

    internal static bool IsCountToken(DslToken t) =>
        t.Kind == DslTokenKind.Identifier
        && t.TextEquals("count", StringComparison.OrdinalIgnoreCase);

    internal static bool IsSumToken(DslToken t) =>
        t.Kind == DslTokenKind.Identifier
        && t.TextEquals("sum", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A path hop is a plain identifier. <c>function</c> is excluded so an expression that ends
    /// in an identifier cannot run into the next top-level <c>function</c> declaration
    /// (<c>… = a + b</c> then <c>function Next(…)</c> would read as the path <c>b function Next</c>).
    /// </summary>
    internal static bool IsPathHopIdent(DslToken t) =>
        t.Kind == DslTokenKind.Identifier
        && !IsFunctionDeclarationWord(t)
        && !IsExistsIdent(t)
        && !IsWhereIdent(t)
        && !IsQuantifierToken(t);

    /// <summary>True when <paramref name="t"/> is the <c>function</c> keyword that starts a declaration.</summary>
    internal static bool IsFunctionDeclarationWord(DslToken t) =>
        t.Kind == DslTokenKind.Identifier
        && t.TextEquals("function", StringComparison.Ordinal);

    /// <summary>
    /// True when <paramref name="t"/> is an identifier spelling a duration unit
    /// (<see cref="DurationUnits"/>). A duration is <c>Number</c> + a known unit, so
    /// <c>3 Days</c> is one and <c>3 function</c> is not — a body ending in a number cannot
    /// swallow the next declaration. The kind check keeps a quoted <c>"Days"</c> out.
    /// </summary>
    internal static bool IsDurationUnitToken(DslToken t) =>
        t.Kind == DslTokenKind.Identifier && DurationUnits.IsUnit(t.Span);

    internal static bool IsQuantifierKeyword(ReadOnlySpan<char> text) =>
        text.Equals("any", StringComparison.OrdinalIgnoreCase)
        || text.Equals("all", StringComparison.OrdinalIgnoreCase)
        || text.Equals("none", StringComparison.OrdinalIgnoreCase)
        || text.Equals("count", StringComparison.OrdinalIgnoreCase)
        || text.Equals("sum", StringComparison.OrdinalIgnoreCase);
}