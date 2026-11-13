namespace Poly.Tests.Grammar;

/// <summary>Why a row, or a slot of a row, is exempt from a rule of the change-protocol check.</summary>
internal enum PatternRowEntryKind {
    /// <summary>Unobserved: a reader dispatch head the hand printer spells outside the table. Needs an example.</summary>
    ReaderHead,
    /// <summary>Unobserved: read surface only. No print site names the row; the writer composes that text by hand.</summary>
    ReaderOnly,
    /// <summary>Unobserved: a recogniser no shipped reader or printer uses.</summary>
    NonLive,
    /// <summary>Asymmetry: the row's hole stands for a run the reader reads outside the table, so read-back fails.</summary>
    PrefixOnly,
    /// <summary>Asymmetry: the caller prints the row's trailing slot itself, after the row, so read-back fails.</summary>
    ContextOnly,
    /// <summary>Asymmetry: another row legitimately reads the printed text.</summary>
    RowOwnsText,
    /// <summary>An optional or repeated slot the caller never fills (keyed by its content-slot index).</summary>
    AlwaysNull,
    /// <summary>A real defect, keyed by a witness text and owned by the row that fixes it.</summary>
    Defect,
}

/// <summary>
/// One allow-list entry. Keyed by <c>(Surface, Rule, Pattern)</c>; an unobserved entry may instead be
/// keyed by <c>(Surface, Rule)</c> with the rule's row count pinned, so a new row in that rule cannot
/// pass unseen. <see cref="PatternRowEntryKind.AlwaysNull"/> adds the slot index;
/// <see cref="PatternRowEntryKind.Defect"/> adds a witness (an asymmetry may): only read-back failures on an
/// observation whose text contains it are exempt. Asymmetry and defect entries exempt read-back only.
/// Every entry carries a reason and an owner: the row that removes it (<c>G-nn</c>, <c>E-nn</c>,
/// <c>F-nn</c>, optionally with its issue, or <c>#nnn</c>), <c>none: permanent — &lt;why&gt;</c>, or
/// <c>pending orchestrator</c> for a finding no row owns yet.
/// </summary>
internal sealed record PatternRowEntry(
    string Surface,
    PatternRowEntryKind Kind,
    string Rule,
    string? Pattern,
    string Reason,
    string Owner) {
    /// <summary>The rule's pinned row count, for a rule-level unobserved entry.</summary>
    public int? RowCount { get; init; }

    /// <summary>The content-slot index (0-based, printer order) for an always-null entry.</summary>
    public int? Slot { get; init; }

    /// <summary>The text a defect's observations contain.</summary>
    public string? Witness { get; init; }

    /// <summary>True for the kinds that say a row is never printed.</summary>
    public bool IsUnobserved => Kind is PatternRowEntryKind.ReaderHead or PatternRowEntryKind.ReaderOnly or PatternRowEntryKind.NonLive;

    /// <summary>True for the kinds that exempt a read-back failure.</summary>
    public bool IsAsymmetry => Kind is PatternRowEntryKind.PrefixOnly or PatternRowEntryKind.ContextOnly or PatternRowEntryKind.RowOwnsText;

    public override string ToString() =>
        $"{Kind} {Surface}:{Rule}/{Pattern ?? $"*({RowCount})"}{(Slot is { } slot ? $"#{slot}" : "")}";

    public static PatternRowEntry Row(string surface, PatternRowEntryKind kind, string rule, string pattern, string reason, string owner) =>
        new(surface, kind, rule, pattern, reason, owner);

    public static PatternRowEntry WholeRule(string surface, PatternRowEntryKind kind, string rule, int rowCount, string reason, string owner) =>
        new(surface, kind, rule, null, reason, owner) { RowCount = rowCount };

    public static PatternRowEntry AlwaysNull(string surface, string rule, string pattern, int slot, string reason, string owner) =>
        new(surface, PatternRowEntryKind.AlwaysNull, rule, pattern, reason, owner) { Slot = slot };

    public static PatternRowEntry Defect(string surface, string rule, string pattern, string witness, string reason, string owner) =>
        new(surface, PatternRowEntryKind.Defect, rule, pattern, reason, owner) { Witness = witness };
}

/// <summary>
/// The change-protocol allow-list (G-23). Each entry is checked both ways: an entry whose condition does
/// not fire fails the check, so a row that starts printing, a slot that starts filling, or a defect that is
/// fixed must delete its entry in the same change. There is deliberately no total count.
/// </summary>
internal static class PatternRowAllowList {
    public const string Dsl = "dsl";
    public const string CSharp = "csharp";

    private const string HandPrinted =
        "none: permanent — DomainDslPrinter spells this head by hand; the row is the reader's MatchRule dispatch head";

    private const string ComposedByHand =
        "none: permanent — the writer composes this text by hand (TypeText, ParamText and the like); Write(Read(x)) guards it";

    /// <summary>A finding no row owns yet: the PR lists it for the orchestrator to place.</summary>
    private const string PendingOrchestrator = "pending orchestrator";

    private const string NonLiveChain =
        "The non-live expr chain: a recogniser only tests use; the driver's precedence table is the live expression grammar.";

    private static readonly PatternRowEntry[] DslEntries = [
        PatternRowEntry.WholeRule(Dsl, PatternRowEntryKind.ReaderHead, "top", 2,
            "Top-level declaration heads; DomainDslPrinter prints declarations by hand.", HandPrinted),
        .. Rows(Dsl, PatternRowEntryKind.ReaderHead, "entity-body",
            ["entity-subscription", "stage", "marked-action", "action", "policy", "typed-line", "property",
                "nav-many", "nav-one", "nav-owned", "primitive-name"],
            "Entity member heads; DomainDslPrinter.Entities prints members by hand.", HandPrinted),
        PatternRowEntry.Row(Dsl, PatternRowEntryKind.ReaderOnly, "entity-body", "legacy-action",
            "The legacy `name(` action spelling: read, never printed.",
            "none: permanent — the reader keeps the legacy spelling; the printer emits `name: action`"),
        PatternRowEntry.WholeRule(Dsl, PatternRowEntryKind.ReaderHead, "stage-body", 5,
            "Stage member heads; DomainDslPrinter prints stages by hand.", HandPrinted),
        PatternRowEntry.WholeRule(Dsl, PatternRowEntryKind.ReaderHead, "effect", 9,
            "Effect heads; DomainDslPrinter prints effects by hand.", HandPrinted),
        PatternRowEntry.Row(Dsl, PatternRowEntryKind.ReaderHead, "annotation", "with-args",
            "Annotation heads; DomainDslPrinter prints annotations by hand.", HandPrinted),
        PatternRowEntry.Row(Dsl, PatternRowEntryKind.NonLive, "annotation", "no-args",
            "Shadowed: with-args with zero arguments matches the same tokens and sorts first, so the matcher never selects no-args (found by G-23's example check; the real fix deletes the row).",
            PendingOrchestrator),
        PatternRowEntry.WholeRule(Dsl, PatternRowEntryKind.ReaderHead, "annotation-args", 5,
            "Annotation argument heads; DomainDslPrinter prints arguments by hand.", HandPrinted),
        PatternRowEntry.WholeRule(Dsl, PatternRowEntryKind.ReaderHead, "visibility", 1,
            "The `public` marker; DomainDslPrinter prints it by hand.", HandPrinted),
        PatternRowEntry.WholeRule(Dsl, PatternRowEntryKind.ReaderHead, "path-hop", 1,
            "A path hop; ExpressionPrinter prints paths by hand.", HandPrinted),
        .. Rows(Dsl, PatternRowEntryKind.ReaderHead, "expr-primary",
            ["group", "not", "duration", "match", "call", "quant-where", "count-bare", "sum-prop", "exists",
                "where-nav", "path-is-not", "path-cmp", "path"],
            "An expression head ExpressionPrinter dispatches by hand (the FallbackBinding list).", HandPrinted),
        .. NonLive("expr", 1), .. NonLive("expr-or", 1), .. NonLive("expr-and", 1), .. NonLive("expr-not", 2),
        .. NonLive("expr-compare", 2), .. NonLive("expr-add-no-not", 1), .. NonLive("expr-mul-no-not", 1),
        .. NonLive("expr-primary-no-not", 18), .. NonLive("expr-add", 1), .. NonLive("expr-mul", 1),
        .. NonLive("expr-operand", 3),
        PatternRowEntry.Row(Dsl, PatternRowEntryKind.PrefixOnly, "uses", "pinned",
            "The pin is a raw same-line token run (DslSyntaxTree.TryReadUse); the row's Identifier is only its print hole, so a dotted numeric pin does not re-read through the row.",
            "none: permanent — the row is one Identifier; the pin is read outside the table by DslSyntaxTree.TryReadUse"),
    ];

    private static readonly PatternRowEntry[] CSharpEntries = [
        .. ReaderOnlyRules(
            ("name", 2), ("type", 1), ("type-core", 17), ("type-suffix", 2), ("type-list", 1), ("type-tail", 1),
            ("modifiers", 1), ("modifier", 10), ("lineage", 1), ("lineage-tail", 1), ("generic-param", 1),
            ("param-list", 1), ("param-tail", 1), ("param", 1), ("param-default", 1), ("arg-list", 1),
            ("arg-tail", 1), ("enum-member-tail", 1), ("type-keyword", 15), ("lambda-param", 2),
            ("lambda-param-list", 1), ("lambda-param-tail", 1), ("shift-tail", 2), ("expr-postfix", 1),
            ("attributes", 1), ("expr", 1), ("field-init", 1), ("property-init", 1), ("cond-tail", 1),
            ("assign-tail", 11), ("local-function", 1), ("for-cond", 1), ("for-incr", 1)),
        .. Rows(CSharp, PatternRowEntryKind.ReaderOnly, "expr-primary",
            ["number", "parenthesized", "cast", "base", "lambda-block-ident", "lambda-block-parens", "generic-name",
                "type-keyword"],
            "No Render site names this row; the writer prints the form by hand.", ComposedByHand),
        .. Rows(CSharp, PatternRowEntryKind.ReaderOnly, "expr-relational", ["type-is", "type-as"],
            "No Render site names this row; the writer prints the form by hand.", ComposedByHand),
        .. Rows(CSharp, PatternRowEntryKind.ReaderOnly, "expr-unary", ["postfix"],
            "No Render site names this row; the writer prints the form by hand.", ComposedByHand),
        .. Rows(CSharp, PatternRowEntryKind.ReaderOnly, "postfix-tail", ["conditional-member", "conditional-index", "generic-invoke"],
            "No Render site names this row; the writer prints the form by hand.", ComposedByHand),
        .. Rows(CSharp, PatternRowEntryKind.ReaderOnly, "statement",
            ["block", "local-function", "const-decl", "typed-decl", "typed-decl-bare", "empty"],
            "No Render site names this row; the writer prints the form by hand.", ComposedByHand),
        .. Rows(CSharp, PatternRowEntryKind.ReaderOnly, "for-init", ["typed-decl", "init"],
            "No Render site names this row; the writer prints the form by hand.", ComposedByHand),
        .. Rows(CSharp, PatternRowEntryKind.ReaderOnly, "using-resource", ["expr"],
            "No Render site names this row; the writer prints the form by hand.", ComposedByHand),
        .. Rows(CSharp, PatternRowEntryKind.ReaderOnly, "method-body", ["block"],
            "No Render site names this row; the writer prints the form by hand.", ComposedByHand),
        .. Rows(CSharp, PatternRowEntryKind.ReaderOnly, "ctor-body", ["block"],
            "No Render site names this row; the writer prints the form by hand.", ComposedByHand),
        .. Rows(CSharp, PatternRowEntryKind.ReaderOnly, "accessor", ["init-semi"],
            "No Render site names this row; the writer prints the form by hand.", ComposedByHand),
        PatternRowEntry.Row(CSharp, PatternRowEntryKind.ContextOnly, "statement", "label",
            "The writer prints `label:` through the row and the labelled statement after it (WriteStatement), so the row text alone does not re-read.",
            "none: permanent — the labelled statement is written by the statement writer, not into the row's slot"),
        PatternRowEntry.AlwaysNull(CSharp, "enum-body", "body", 1,
            "The writer prints every member into the first-member slot; the repeated tail slot is part of that text.",
            "none: permanent — one member list string fills the enum body"),
        PatternRowEntry.AlwaysNull(CSharp, "expr-shift", "shift", 1,
            "BinaryChain prints the whole chain into the operand slot; the repeated shift-tail slot is part of that text.",
            "none: permanent — one chain string fills the shift row, as for the LeftAssoc levels"),
        .. AttributesSlot("type-decl", "class"), .. AttributesSlot("type-decl", "record"),
        .. AttributesSlot("type-decl", "record-semi"), .. AttributesSlot("type-decl", "interface"),
        .. AttributesSlot("type-decl", "enum"), .. AttributesSlot("member", "field"),
        .. AttributesSlot("member", "property"), .. AttributesSlot("member", "method"),
        .. AttributesSlot("member", "ctor"),
        PatternRowEntry.Row(CSharp, PatternRowEntryKind.RowOwnsText, "statement", "expr-stmt",
            "AssignmentText spells a fused declaration `var x = v` by hand and prints it through expr-stmt; the reader reads that text as statement/var-decl.",
            PendingOrchestrator) with { Witness = "var " },
        PatternRowEntry.Row(CSharp, PatternRowEntryKind.RowOwnsText, "expr-primary", "lambda-ident",
            "A block-bodied lambda prints its block into the expression slot; the reader reads that text as expr-primary/lambda-block-ident.",
            PendingOrchestrator) with { Witness = "=> {" },
        PatternRowEntry.Row(CSharp, PatternRowEntryKind.RowOwnsText, "expr-primary", "lambda-parens",
            "A block-bodied lambda prints its block into the expression slot; the reader reads that text as expr-primary/lambda-block-parens.",
            PendingOrchestrator) with { Witness = "=> {" },
        .. InitializerDefect("new int[] {", "statement", "foreach"),
        .. InitializerDefect("new List<int> {", "statement", "return"),
        .. InitializerDefect("new int[] {", "block", "block"), .. InitializerDefect("new List<int> {", "block", "block"),
        .. InitializerDefect("new int[] {", "member", "method"), .. InitializerDefect("new List<int> {", "member", "method"),
        .. InitializerDefect("new int[] {", "type-body", "body"), .. InitializerDefect("new List<int> {", "type-body", "body"),
        .. InitializerDefect("new int[] {", "type-decl", "class"), .. InitializerDefect("new List<int> {", "type-decl", "class"),
        PatternRowEntry.Defect(CSharp, "type-decl", "interface", "interface ",
            "The interface path fills the type-body slot with raw member lines and no braces, so a printed interface does not re-read.",
            "G-23b (#848)"),
        PatternRowEntry.Defect(CSharp, "type-decl", "enum", "enum Empty",
            "An empty enum prints `{ }`; the reader's enum-body needs a first member.",
            "G-23b (#849)"),
        PatternRowEntry.Defect(CSharp, "enum-body", "body", "{ }",
            "An empty enum prints `{ }`; the reader's enum-body needs a first member.",
            "G-23b (#849)"),
    ];

    /// <summary>Every entry, both surfaces.</summary>
    public static IReadOnlyList<PatternRowEntry> All { get; } = [.. DslEntries, .. CSharpEntries];

    /// <summary>The surfaces the check runs over.</summary>
    public static IReadOnlyList<string> Surfaces { get; } = [Dsl, CSharp];

    /// <summary>The entries of one surface. An unknown surface throws rather than reading as an empty list.</summary>
    public static IReadOnlyList<PatternRowEntry> For(string surface) =>
        [.. All.Where(entry => string.Equals(entry.Surface, KnownSurface(surface), StringComparison.Ordinal))];

    /// <summary><paramref name="surface"/>, when it is one the check runs over; otherwise throws.</summary>
    public static string KnownSurface(string surface) =>
        Surfaces.Contains(surface, StringComparer.Ordinal)
            ? surface
            : throw new ArgumentException($"'{surface}' is not a change-protocol surface ({string.Join(", ", Surfaces)}).", nameof(surface));

    private static IEnumerable<PatternRowEntry> Rows(
        string surface, PatternRowEntryKind kind, string rule, string[] patterns, string reason, string owner) =>
        patterns.Select(pattern => PatternRowEntry.Row(surface, kind, rule, pattern, reason, owner));

    private static IEnumerable<PatternRowEntry> NonLive(string rule, int rowCount) =>
        [PatternRowEntry.WholeRule(Dsl, PatternRowEntryKind.NonLive, rule, rowCount, NonLiveChain, "G-24")];

    private static IEnumerable<PatternRowEntry> ReaderOnlyRules(params (string Rule, int RowCount)[] rules) =>
        rules.Select(rule => PatternRowEntry.WholeRule(CSharp, PatternRowEntryKind.ReaderOnly, rule.Rule, rule.RowCount,
            "No Render site names a row of this rule; the writer composes the text by hand.", ComposedByHand));

    /// <summary>
    /// A Constant holding an <c>int[]</c> or a <c>List&lt;int&gt;</c> prints as an initializer
    /// (<c>ArrayLiteral</c>, <c>CollectionLiteral</c>) that no C# row reads, so every row whose text
    /// contains one fails read-back.
    /// </summary>
    private static IEnumerable<PatternRowEntry> InitializerDefect(string witness, string rule, string pattern) =>
        [PatternRowEntry.Defect(CSharp, rule, pattern, witness,
            "The writer prints an array or collection initializer by hand (ArrayLiteral, CollectionLiteral); no C# row reads `new T[] { … }` or `new List<T> { … }`, so Read(Write(x)) fails.",
            PendingOrchestrator)];

    private static IEnumerable<PatternRowEntry> AttributesSlot(string rule, string pattern) =>
        [PatternRowEntry.AlwaysNull(CSharp, rule, pattern, 0,
            "Attributes print before the row (AttributesText); the row's attributes slot is always empty.",
            "none: permanent — attribute lines are printed one per line before the declaration")];
}