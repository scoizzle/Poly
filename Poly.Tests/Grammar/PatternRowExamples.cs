namespace Poly.Tests.Grammar;

/// <summary>
/// One example for a row that is read but never printed through the table (a reader head or a reader-only
/// row): a text the real matcher must read as exactly <c>Rule/Pattern</c>, to the end of input. The example
/// stands in for the observation such a row cannot produce.
/// </summary>
internal sealed record PatternRowExample(string Surface, string Rule, string Pattern, string Text);

/// <summary>
/// The examples for every <see cref="PatternRowEntryKind.ReaderHead"/> and
/// <see cref="PatternRowEntryKind.ReaderOnly"/> row (A3c). The table is checked both ways: such a row without
/// an example fails, and so does an example for any other row. A row that starts printing through the table
/// deletes its entry and its example together.
/// </summary>
internal static class PatternRowExamples {
    private const string Dsl = PatternRowAllowList.Dsl;
    private const string CSharp = PatternRowAllowList.CSharp;

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

        new(Dsl, "entity-body", "legacy-action", "Submit("),

        // C# reader-only rows: the writer composes these forms by hand, so the example is the row's only
        // matcher check (it is what shows a row another pattern shadows).
        new(CSharp, "name", "simple", "Foo"),
        new(CSharp, "name", "qualified", "A.B"),
        new(CSharp, "type", "core", "long"),
        new(CSharp, "type-core", "void", "void"),
        new(CSharp, "type-core", "bool", "bool"),
        new(CSharp, "type-core", "byte", "byte"),
        new(CSharp, "type-core", "sbyte", "sbyte"),
        new(CSharp, "type-core", "short", "short"),
        new(CSharp, "type-core", "ushort", "ushort"),
        new(CSharp, "type-core", "int", "int"),
        new(CSharp, "type-core", "uint", "uint"),
        new(CSharp, "type-core", "long", "long"),
        new(CSharp, "type-core", "ulong", "ulong"),
        new(CSharp, "type-core", "float", "float"),
        new(CSharp, "type-core", "double", "double"),
        new(CSharp, "type-core", "decimal", "decimal"),
        new(CSharp, "type-core", "char", "char"),
        new(CSharp, "type-core", "string", "string"),
        new(CSharp, "type-core", "object", "object"),
        new(CSharp, "type-core", "named", "Foo"),
        new(CSharp, "type-suffix", "optional", "?"),
        new(CSharp, "type-suffix", "array", "[]"),
        new(CSharp, "type-list", "list", "int, long"),
        new(CSharp, "type-tail", "tail", ", int"),
        new(CSharp, "modifiers", "list", "public static"),
        new(CSharp, "modifier", "public", "public"),
        new(CSharp, "modifier", "private", "private"),
        new(CSharp, "modifier", "protected", "protected"),
        new(CSharp, "modifier", "internal", "internal"),
        new(CSharp, "modifier", "static", "static"),
        new(CSharp, "modifier", "const", "const"),
        new(CSharp, "modifier", "readonly", "readonly"),
        new(CSharp, "modifier", "volatile", "volatile"),
        new(CSharp, "modifier", "override", "override"),
        new(CSharp, "modifier", "async", "async"),
        new(CSharp, "lineage", "list", ": Base, IShape"),
        new(CSharp, "lineage-tail", "tail", ", IShape"),
        new(CSharp, "generic-param", "param", "T"),
        new(CSharp, "param-list", "list", "long a, long b"),
        new(CSharp, "param-tail", "tail", ", long b"),
        new(CSharp, "param", "param", "long a = 1"),
        new(CSharp, "param-default", "default", "= 1"),
        new(CSharp, "arg-list", "list", "a, b"),
        new(CSharp, "arg-tail", "tail", ", b"),
        new(CSharp, "enum-members", "list", "A = 0, B = 1"),
        new(CSharp, "enum-member-tail", "tail", ", B = 1"),
        new(CSharp, "type-keyword", "bool", "bool"),
        new(CSharp, "type-keyword", "byte", "byte"),
        new(CSharp, "type-keyword", "sbyte", "sbyte"),
        new(CSharp, "type-keyword", "short", "short"),
        new(CSharp, "type-keyword", "ushort", "ushort"),
        new(CSharp, "type-keyword", "int", "int"),
        new(CSharp, "type-keyword", "uint", "uint"),
        new(CSharp, "type-keyword", "long", "long"),
        new(CSharp, "type-keyword", "ulong", "ulong"),
        new(CSharp, "type-keyword", "float", "float"),
        new(CSharp, "type-keyword", "double", "double"),
        new(CSharp, "type-keyword", "decimal", "decimal"),
        new(CSharp, "type-keyword", "char", "char"),
        new(CSharp, "type-keyword", "string", "string"),
        new(CSharp, "type-keyword", "object", "object"),
        new(CSharp, "lambda-param", "typed", "long x"),
        new(CSharp, "lambda-param", "bare", "x"),
        new(CSharp, "lambda-param-list", "list", "x, y"),
        new(CSharp, "lambda-param-tail", "tail", ", y"),
        new(CSharp, "shift-tail", "left", "<< 1"),
        new(CSharp, "shift-tail", "right", ">> 1"),
        new(CSharp, "expr-postfix", "postfix", "a.b()"),
        new(CSharp, "attributes", "list", "[A][B]"),
        new(CSharp, "expr", "value", "1"),
        new(CSharp, "field-init", "init", "= 1"),
        new(CSharp, "property-init", "init", "= 1;"),
        new(CSharp, "cond-tail", "tail", "? a : b"),
        new(CSharp, "assign-tail", "tail", "= 1"),
        new(CSharp, "assign-tail", "compound-add", "+= 1"),
        new(CSharp, "assign-tail", "compound-sub", "-= 1"),
        new(CSharp, "assign-tail", "compound-mul", "*= 1"),
        new(CSharp, "assign-tail", "compound-div", "/= 1"),
        new(CSharp, "assign-tail", "compound-mod", "%= 1"),
        new(CSharp, "assign-tail", "compound-and", "&= 1"),
        new(CSharp, "assign-tail", "compound-or", "|= 1"),
        new(CSharp, "assign-tail", "compound-xor", "^= 1"),
        new(CSharp, "assign-tail", "compound-coalesce", "??= 1"),
        new(CSharp, "assign-tail", "compound-shift-left", "<<= 1"),
        new(CSharp, "local-function", "method", "long F() { return 1; }"),
        new(CSharp, "for-cond", "cond", "i < 1"),
        new(CSharp, "for-incr", "incr", "i = i + 1"),
        new(CSharp, "expr-primary", "number", "1"),
        new(CSharp, "expr-primary", "parenthesized", "(a)"),
        new(CSharp, "expr-primary", "cast", "(long)a"),
        new(CSharp, "expr-primary", "base", "base"),
        new(CSharp, "expr-primary", "lambda-block-ident", "x => { }"),
        new(CSharp, "expr-primary", "lambda-block-parens", "() => { }"),
        new(CSharp, "expr-primary", "generic-name", "F<int>"),
        new(CSharp, "expr-primary", "type-keyword", "int"),
        new(CSharp, "expr-relational", "type-is", "a is long"),
        new(CSharp, "expr-relational", "type-as", "a as long"),
        new(CSharp, "expr-unary", "postfix", "a"),
        new(CSharp, "postfix-tail", "conditional-member", "?.b"),
        new(CSharp, "postfix-tail", "conditional-index", "?[0]"),
        new(CSharp, "postfix-tail", "generic-invoke", "<int>()"),
        new(CSharp, "statement", "block", "{ }"),
        new(CSharp, "statement", "local-function", "long F() { return 1; }"),
        new(CSharp, "statement", "const-decl", "const long x = 1;"),
        new(CSharp, "statement", "typed-decl", "long x = 1;"),
        new(CSharp, "statement", "typed-decl-bare", "long x;"),
        new(CSharp, "statement", "empty", ";"),
        new(CSharp, "for-init", "typed-decl", "long i = 0"),
        new(CSharp, "for-init", "init", "i = 0"),
        new(CSharp, "using-resource", "expr", "d"),
        new(CSharp, "method-body", "block", "{ }"),
        new(CSharp, "ctor-body", "block", "{ return; }"),
        new(CSharp, "accessor", "init-semi", "init;"),
    ];

    /// <summary>The examples of one surface. An unknown surface throws.</summary>
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