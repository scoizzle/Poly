namespace Poly.Tests.Grammar;

/// <summary>
/// One reader-head example: a text the real matcher must read as exactly <c>Rule/Pattern</c>, to the
/// end of input. The example stands in for the observation a hand-printed head cannot produce.
/// </summary>
internal sealed record PatternRowExample(string Surface, string Rule, string Pattern, string Text);

/// <summary>
/// The examples for every <see cref="PatternRowEntryKind.ReaderHead"/> row (A3c). The table is checked
/// both ways: a reader-head row without an example fails, and so does an example for a row that is not a
/// reader head. A row that starts printing through the table deletes its entry and its example together.
/// </summary>
internal static class PatternRowExamples {
    private const string Dsl = PatternRowAllowList.Dsl;

    public static IReadOnlyList<PatternRowExample> All { get; } = [
        new(Dsl, "top", "enum", "Status: enum"),
        new(Dsl, "top", "entity", "Order: entity"),

        new(Dsl, "entity-body", "entity-subscription", "when Created"),
        new(Dsl, "entity-body", "stage", "Draft: stage"),
        new(Dsl, "entity-body", "marked-action", "public action Submit"),
        new(Dsl, "entity-body", "action", "Submit: action"),
        new(Dsl, "entity-body", "policy", "IsLarge: policy"),
        new(Dsl, "entity-body", "typed-line", "Owner: Customer"),
        new(Dsl, "entity-body", "property", "Title: Text"),
        new(Dsl, "entity-body", "nav-many", "Lines: many"),
        new(Dsl, "entity-body", "nav-one", "Owner: one"),
        new(Dsl, "entity-body", "nav-owned", "Address: owned"),
        new(Dsl, "entity-body", "primitive-name", "Text:"),

        new(Dsl, "stage-body", "entry", "entry"),
        new(Dsl, "stage-body", "exit", "exit"),
        new(Dsl, "stage-body", "subscription", "when Paid"),
        new(Dsl, "stage-body", "marked-stage-action", "public action Ship"),
        new(Dsl, "stage-body", "stage-action", "Ship"),

        new(Dsl, "effect", "transition", "transition to Paid"),
        new(Dsl, "effect", "assign", "assign Total to"),
        new(Dsl, "effect", "link", "link Owner to"),
        new(Dsl, "effect", "unlink", "unlink Owner from"),
        new(Dsl, "effect", "create-in", "create in Lines"),
        new(Dsl, "effect", "create", "create Line"),
        new(Dsl, "effect", "invoke", "invoke"),
        new(Dsl, "effect", "for", "for"),
        new(Dsl, "effect", "if", "if ("),

        new(Dsl, "annotation", "with-args", "max(10)"),

        new(Dsl, "annotation-args", "str", "\"a\""),
        new(Dsl, "annotation-args", "num", "1"),
        new(Dsl, "annotation-args", "tru", "true"),
        new(Dsl, "annotation-args", "fal", "false"),
        new(Dsl, "annotation-args", "nul", "null"),

        new(Dsl, "visibility", "public", "public"),
        new(Dsl, "path-hop", "hop", "Total"),

        new(Dsl, "expr-primary", "group", "("),
        new(Dsl, "expr-primary", "not", "not"),
        new(Dsl, "expr-primary", "duration", "3 Days"),
        new(Dsl, "expr-primary", "match", "match { }"),
        new(Dsl, "expr-primary", "call", "f(x)"),
        new(Dsl, "expr-primary", "quant-where", "any Lines where"),
        new(Dsl, "expr-primary", "count-bare", "count Lines"),
        new(Dsl, "expr-primary", "sum-prop", "sum Lines Total"),
        new(Dsl, "expr-primary", "exists", "Lines exists"),
        new(Dsl, "expr-primary", "where-nav", "Lines where"),
        new(Dsl, "expr-primary", "path-is-not", "Order Total is not"),
        new(Dsl, "expr-primary", "path-cmp", "Order Total >"),
        new(Dsl, "expr-primary", "path", "Order Total"),
    ];

    /// <summary>The examples of one surface (the C# surface has none). An unknown surface throws.</summary>
    public static IReadOnlyList<PatternRowExample> For(string surface) =>
        [.. All.Where(example => string.Equals(example.Surface, PatternRowAllowList.KnownSurface(surface), StringComparison.Ordinal))];
}

/// <summary>
/// Rows the drive must observe on each surface: a floor, so a drive that silently stops printing cannot
/// pass the check by observing less. Each name is first looked up in the table, so renaming a floor row
/// fails loudly instead of shrinking the floor. A slice that moves a row onto the table (G-29 to G-36 for
/// the DSL) adds it here.
/// </summary>
internal static class PatternRowFloors {
    /// <summary>The DSL rows printed through the table today.</summary>
    public static IReadOnlyList<(string Rule, string Pattern)> Dsl { get; } = [
        ("document", "header"),
        ("uses", "id"),
        ("uses", "pinned"),
        ("expr-primary", "ident"),
        ("expr-primary", "number"),
        ("expr-primary", "string"),
        ("expr-primary", "true"),
        ("expr-primary", "false"),
        ("expr-primary", "null"),
        ("date-operation", "add"),
        ("date-operation", "sub"),
    ];

    /// <summary>The C# canary rows (14 named by the lock) plus <c>type-decl/record</c>.</summary>
    public static IReadOnlyList<(string Rule, string Pattern)> CSharp { get; } = [
        ("statement", "if"),
        ("statement", "while"),
        ("statement", "for"),
        ("statement", "try"),
        ("statement", "switch"),
        ("type-decl", "class"),
        ("type-decl", "enum"),
        ("type-decl", "interface"),
        ("member", "method"),
        ("member", "property"),
        ("attribute", "bare"),
        ("attribute", "args"),
        ("expr-coalesce", "coalesce"),
        ("postfix-tail", "invoke"),
        ("type-decl", "record"),
    ];
}