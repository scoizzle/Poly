using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

using Poly.Compile;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Language;
using Poly.Grammar;
using Poly.Packs.Product;
using Poly.Tests.Docs;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// G-13a's preservation oracle for the DSL token CONSUMERS: a frozen dump of what the resolver, the
/// outline, the edit splicer, the compiler's extension diagnostics and the expression fragment BUILD from
/// tokens (names, literal values with their CLR types, constraint bounds, annotation values, error
/// messages and positions), written while every consumer still read <c>DslToken.Text</c>. The file
/// (<c>ResolverBaseline.txt</c>) is DATA; every record is re-derived from its own key and must match
/// byte for byte.
///
/// <para>Records: <c>S</c> a reader-site row (<see cref="Sites"/> label, case, door, input, result), keyed
/// to the token-text read it reaches: the case names say how (<c>ok</c> the word in position, <c>name</c>
/// the word used as a name, <c>str</c> the same spelling as a string literal, <c>mis</c> another kind);
/// <c>D</c> a corpus document's text SHA and change count, and <c>M</c> one per change (index, type, SHA
/// of its canonical print); <c>O</c> the outline of a hostile or corpus input; <c>N</c> the mutation
/// sweep: one row per (document, token) holding the SHA of what the resolver and the outline do when
/// that token becomes each filler (identifier, keyword, <c>Text</c>, number, escaped string,
/// punctuation, end of input); <c>X</c> each sweep outcome that is neither a success nor a positioned
/// grammar error, with its message; <c>T</c> the exception types the sweep produced.</para>
///
/// <para>Regeneration (<c>POLY_REGEN_RESOLVER_BASELINE=1</c>) rewrites the file from the CURRENT code.
/// Never regenerate on changed consumers: that would move the oracle with the code. A shipped document
/// that changes under the D/M rows is re-recorded for that document only, at the oracle commit.</para>
/// </summary>
public sealed class ResolverBaselineTests {
    private const string BaselinePath = "Poly.Tests/DomainModeling/Parsing/ResolverBaseline.txt";

    // ── the reader sites (file and line on ab00d068, before the sweep) ───────────────

    /// <summary>
    /// One entry per production read of <c>DslToken.Text</c> (114). <c>Guarded</c> marks a read whose
    /// token kind the old code guaranteed (a kind test, an <c>Expect</c>, or the pattern row that matched),
    /// so a kind-mismatch row must reach the guard; <c>Unreachable</c> gives the reason when no input can.
    /// </summary>
    internal static readonly (string Label, string Where, bool Guarded, string? Unreachable)[] Sites = [
        ("Core.ErrorAtUse", "DomainResolver.cs:183", true, null),
        ("Core.PropertyName.name", "DomainResolver.cs:226", true, null),
        ("Core.PropertyName.error", "DomainResolver.cs:230", false, null),
        ("Core.RequiredWord", "DomainResolver.cs:252", true, null),
        ("Constraints.length.min", "DomainResolver.Constraints.cs:72", true, null),
        ("Constraints.length.max", "DomainResolver.Constraints.cs:78", true, null),
        ("Constraints.pattern", "DomainResolver.Constraints.cs:91", true, null),
        ("Constraints.bound.error", "DomainResolver.Constraints.cs:111", false, null),
        ("Constraints.bound.text", "DomainResolver.Constraints.cs:112", true, null),
        ("Effects.unsupported.check", "DomainResolver.Effects.cs:19", true, null),
        ("Effects.unsupported.message", "DomainResolver.Effects.cs:21", true, null),
        ("Effects.expected", "DomainResolver.Effects.cs:35", false, null),
        ("Effects.transition", "DomainResolver.Effects.cs:40", true, null),
        ("Effects.assign", "DomainResolver.Effects.cs:46", true, null),
        ("Effects.link", "DomainResolver.Effects.cs:53", true, null),
        ("Effects.unlink", "DomainResolver.Effects.cs:59", true, null),
        ("Effects.create-in", "DomainResolver.Effects.cs:65", true, null),
        ("Effects.create", "DomainResolver.Effects.cs:73", true, null),
        ("Effects.general-for.where-as-rel", "DomainResolver.Effects.cs:190", true, null),
        ("Effects.general-for.where", "DomainResolver.Effects.cs:196", true, null),
        ("Effects.general-for.brace", "DomainResolver.Effects.cs:202", false, null),
        ("Effects.for.expected", "DomainResolver.Effects.cs:235", false, null),
        ("Effects.for.where", "DomainResolver.Effects.cs:245", true, null),
        ("Effects.for.policy", "DomainResolver.Effects.cs:258", true, null),
        ("Entities.initial", "DomainResolver.Entities.cs:37", true, null),
        ("Entities.key-clause.error", "DomainResolver.Entities.cs:73", false, null),
        ("Entities.key.first", "DomainResolver.Entities.cs:104", true, null),
        ("Entities.header.unknown-annotation", "DomainResolver.Entities.cs:116", false, null),
        ("Entities.key.second", "DomainResolver.Entities.cs:119", true, null),
        ("Entities.legacy-action.error", "DomainResolver.Entities.cs:178", false, null),
        ("Entities.typed-line.enum", "DomainResolver.Entities.cs:190", false, null),
        ("Entities.typed-line.value", "DomainResolver.Entities.cs:191", false, null),
        ("Entities.typed-line.primitive", "DomainResolver.Entities.cs:192", false, null),
        ("Entities.typed-line.unsupported", "DomainResolver.Entities.cs:202", false, null),
        ("Entities.primitive-name.name", "DomainResolver.Entities.cs:229", true, null),
        ("Entities.primitive-name.type-check", "DomainResolver.Entities.cs:232", false, null),
        ("Entities.primitive-name.error", "DomainResolver.Entities.cs:233", false, null),
        ("Entities.primitive-name.named-type", "DomainResolver.Entities.cs:239", true, null),
        ("Entities.fallback.name", "DomainResolver.Entities.cs:253", true, null),
        ("Entities.fallback.unsupported", "DomainResolver.Entities.cs:261", false, null),
        ("Entities.body.error", "DomainResolver.Entities.cs:267", false, null),
        ("Entities.tail.unknown-annotation", "DomainResolver.Entities.cs:383", false, null),
        ("Entities.annotation.keyword", "DomainResolver.Entities.cs:417", true, null),
        ("Entities.annotation.string", "DomainResolver.Entities.cs:463", true, null),
        ("Entities.annotation.number", "DomainResolver.Entities.cs:468", true, null),
        ("Entities.annotation.error", "DomainResolver.Entities.cs:484", false, null),
        ("Entities.annotation.separator", "DomainResolver.Entities.cs:497", false, null),
        ("Relationships.navline.text", "DomainResolver.Relationships.cs:36", true, null),
        ("Relationships.navline.primitive-hint", "DomainResolver.Relationships.cs:78", false, null),
        ("Relationships.navline.unexpected", "DomainResolver.Relationships.cs:79", false, null),
        ("Relationships.via.name", "DomainResolver.Relationships.cs:94", true, null),
        ("Relationships.via.word", "DomainResolver.Relationships.cs:114", true, null),
        ("Stages.prev", "DomainResolver.Stages.cs:16", true, null),
        ("Stages.entry.repeat", "DomainResolver.Stages.cs:54", false, null),
        ("Stages.exit.repeat", "DomainResolver.Stages.cs:67", false, null),
        ("Stages.entry-exit.late", "DomainResolver.Stages.cs:96", false,
            "the stage-body rows 'entry' and 'exit' always match an Entry or Exit token first, so the fallback never sees one"),
        ("Stages.quantifier.any", "DomainResolver.Stages.cs:150", true, null),
        ("Stages.quantifier.all", "DomainResolver.Stages.cs:155", true, null),
        ("TopLevel.marked.error", "DomainResolver.TopLevel.cs:47", false, null),
        ("TopLevel.rejection.name", "DomainResolver.TopLevel.cs:58", true, null),
        ("TopLevel.rejection.unsupported", "DomainResolver.TopLevel.cs:60", true, null),
        ("TopLevel.rejection.unsupported-message", "DomainResolver.TopLevel.cs:62", true, null),
        ("TopLevel.rejection.inheritance", "DomainResolver.TopLevel.cs:67", true, null),
        ("TopLevel.rejection.inheritance-define", "DomainResolver.TopLevel.cs:68", true, null),
        ("TopLevel.rejection.expected", "DomainResolver.TopLevel.cs:71", false, null),
        ("TopLevel.named.kind", "DomainResolver.TopLevel.cs:84", true, null),
        ("TopLevel.enum.member", "DomainResolver.TopLevel.cs:120", true, null),
        ("TopLevel.enum.member-annotation", "DomainResolver.TopLevel.cs:128", true, null),
        ("TopLevel.enum.reserved.length", "DomainResolver.TopLevel.cs:156", false, null),
        ("TopLevel.enum.reserved.letter", "DomainResolver.TopLevel.cs:157", false, null),
        ("TopLevel.enum.reserved.word", "DomainResolver.TopLevel.cs:158", false, null),
        ("TopLevel.function.start", "DomainResolver.TopLevel.cs:192", true, null),
        ("TopLevel.function.name-error", "DomainResolver.TopLevel.cs:203", false, null),
        ("TopLevel.function.name", "DomainResolver.TopLevel.cs:204", true, null),
        ("TopLevel.function.separator", "DomainResolver.TopLevel.cs:220", false, null),
        ("TopLevel.function.equals", "DomainResolver.TopLevel.cs:227", false, null),
        ("TopLevel.value.primitive", "DomainResolver.TopLevel.cs:255", true, null),
        ("TopLevel.contract.external", "DomainResolver.TopLevel.cs:297", true, null),
        ("TopLevel.contract.internal", "DomainResolver.TopLevel.cs:301", true, null),
        ("TopLevel.contract.source-string", "DomainResolver.TopLevel.cs:311", true, null),
        ("TopLevel.contract.version-string", "DomainResolver.TopLevel.cs:320", true, null),
        ("TopLevel.contract.value-word", "DomainResolver.TopLevel.cs:334", true, null),
        ("TopLevel.contract.inbound", "DomainResolver.TopLevel.cs:348", true, null),
        ("TopLevel.contract.outbound", "DomainResolver.TopLevel.cs:352", true, null),
        ("TopLevel.contract.operation", "DomainResolver.TopLevel.cs:361", true, null),
        ("TopLevel.contract.event", "DomainResolver.TopLevel.cs:365", true, null),
        ("TopLevel.contract.payload-primitive", "DomainResolver.TopLevel.cs:375", true, null),
        ("TopLevel.word.check", "DomainResolver.TopLevel.cs:406", true, null),
        ("TopLevel.word.error", "DomainResolver.TopLevel.cs:407", false,
            "every ExpectWord call follows a check of the same word (TryParseNamedTopLevel's switch, the contract value test), so the word never differs"),
        ("Types.expected", "DomainResolver.Types.cs:22", false, null),
        ("Fragment.trailing", "DslExpressionFragment.cs:41", false, null),
        ("Expression.NameOf", "DslExpressionParser.cs:36", false, null),
        ("Expression.Describe", "DslExpressionParser.cs:39", false, null),
        ("Outline.domain", "DslOutline.cs:151", false, null),
        ("Outline.public-value", "DslOutline.cs:166", false, null),
        ("Outline.function", "DslOutline.cs:318", true, null),
        ("Outline.top.name", "DslOutline.cs:335", true, null),
        ("Outline.top.kind", "DslOutline.cs:336", false, null),
        ("Outline.marked.kind", "DslOutline.cs:365", false, null),
        ("Outline.marked.name", "DslOutline.cs:366", false, null),
        ("Outline.member.unnamed", "DslOutline.cs:471", true, null),
        ("Outline.member.public-action", "DslOutline.cs:478", false, null),
        ("Outline.member.name", "DslOutline.cs:486", true, null),
        ("Outline.member.legacy-action", "DslOutline.cs:507", true, null),
        ("Edit.marked-name", "PolyEdit.cs:411", true, null),
        ("Edit.name", "PolyEdit.cs:413", true, null),
        ("Edit.top-kind", "PolyEdit.cs:439", false, null),
        ("Temporal.amount.parse", "TemporalExpressionPrintBinders.cs:37", true, null),
        ("Temporal.amount.message", "TemporalExpressionPrintBinders.cs:38", true, null),
        ("Temporal.unit.lookup", "TemporalExpressionPrintBinders.cs:39", true, null),
        ("Temporal.unit.message", "TemporalExpressionPrintBinders.cs:40", true,
            "the duration row's 'unit' predicate admits only a known unit, so the lookup never fails"),
        ("Fold.text", "ExpressionFoldTable.cs:66", true, null),
        ("Fold.number", "ExpressionFoldTable.cs:73", true, null),
        ("Compiler.extension", "PolyCompiler.cs:320", false, null),
    ];

    /// <summary>Guarded sites whose guard no input can make false, so they have no kind-mismatch row.</summary>
    internal static readonly Dictionary<string, string> GuardAlwaysHolds = new(StringComparer.Ordinal) {
        ["Core.ErrorAtUse"] = "only header tokens are scanned, and a header 'uses' is always followed by its identifier id",
        ["TopLevel.rejection.name"] = "the top-level loop runs only on an identifier or 'public', and 'public' is taken before the rejection",
        ["TopLevel.word.check"] = "every ExpectWord call follows a check of the same word on the same token",
    };

    // ── documents ─────────────────────────────────────────────────────────────────────

    private static string Doc(string body) => "domain A\n" + body + "\n";

    private static string Ent(string members) => Doc("X: entity {\n  " + members + "\n}");

    private const string Kid = "Kid: entity {\n  Qty: Number\n  IsPaid: policy { Qty > 0 }\n  Pay: action { }\n}";

    private static string Act(string effects) =>
        Doc("X: entity {\n  N: Number\n  Kids: many Kid\n  Done: stage { }\n  Go: action {\n    " + effects + "\n  }\n}\n" + Kid);

    private static string Sub(string head) =>
        Doc("X: entity {\n  Kids: many Kid\n  Done: stage {\n    " + head + "\n  }\n}\n" + Kid);

    private static string Contract(string head, string body) =>
        Doc("C: contract " + head + " {\n  " + body + "\n}");

    private static string Temporal(string expression) =>
        "domain A\nuses temporal\nX: entity {\n  Due: Date\n  Late: policy { " + expression + " }\n}\n";

    private static string Edit(string source, string json) => source + "\u0001" + json;

    private static readonly string EditSource = Doc("X: entity {\n  N: Text\n}\nS: enum { A, B }");

    private static string AddTop(string dsl) =>
        Edit(EditSource, "[{\"op\":\"add\",\"parent\":\"\",\"dsl\":" + System.Text.Json.JsonSerializer.Serialize(dsl) + "}]");

    private static string AddMember(string dsl) =>
        Edit(EditSource, "[{\"op\":\"add\",\"parent\":\"X\",\"dsl\":" + System.Text.Json.JsonSerializer.Serialize(dsl) + "}]");

    private static string ReplaceTop(string path, string dsl) =>
        Edit(EditSource, "[{\"op\":\"replace\",\"path\":\"" + path + "\",\"dsl\":" + System.Text.Json.JsonSerializer.Serialize(dsl) + "}]");

    /// <summary>The site rows: (site label, case, door, input). Doors: R resolver, O outline, E edit, C compiler, F fragment.</summary>
    internal static IEnumerable<(string Site, string Case, string Door, string Input)> Cases() {
        // DomainResolver.cs
        yield return ("Core.ErrorAtUse", "ok", "R", Doc("uses temporal v1"));
        yield return ("Core.ErrorAtUse", "second", "R", Doc("uses storage\nuses temporal 2024-11-20"));
        yield return ("Core.ErrorAtUse", "pin-string", "R", Doc("uses temporal \"v\\\"1\""));
        yield return ("Core.PropertyName.name", "ok", "R", Doc("V: value { Amount: Number }"));
        yield return ("Core.PropertyName.name", "keyword", "R", Doc("V: value { Text: Text Number: Number }"));
        yield return ("Core.PropertyName.name", "unicode", "R", Doc("V: value { Gr\u00F6\u00DFe: Number \u540D\u524D: Text }"));
        yield return ("Core.PropertyName.name", "initializer", "R", Act("create Kid { Qty: 1 }"));
        yield return ("Core.PropertyName.name", "str", "R", Doc("V: value { \"A\": Number }"));
        yield return ("Core.PropertyName.error", "mis-number", "R", Doc("V: value { 5: Number }"));
        yield return ("Core.PropertyName.error", "str", "R", Doc("V: value { \"Amount\": Number }"));
        yield return ("Core.PropertyName.error", "mis-punct", "R", Doc("V: value { -> }"));
        yield return ("Core.PropertyName.error", "mis-keyword", "R", Doc("V: value { entity: Text }"));
        yield return ("Core.PropertyName.error", "mis-initializer", "R", Act("create Kid { \"Qty\": 1 }"));
        yield return ("Core.RequiredWord", "ok", "R", Ent("N: Text required"));
        yield return ("Core.RequiredWord", "name", "R", Ent("required: Text"));
        yield return ("Core.RequiredWord", "case", "R", Ent("N: Text Required"));
        yield return ("Core.RequiredWord", "str", "R", Ent("N: Text \"required\""));
        yield return ("Core.RequiredWord", "header", "R", Doc("X: entity required { N: Text }"));
        yield return ("Core.RequiredWord", "value-member", "R", Doc("V: value { N: Text required }"));

        // Constraints
        yield return ("Constraints.length.min", "ok", "R", Ent("N: Text length(3)"));
        yield return ("Constraints.length.min", "max-int", "R", Ent("N: Text length(2147483647)"));
        yield return ("Constraints.length.min", "overflow", "R", Ent("N: Text length(2147483648)"));
        yield return ("Constraints.length.min", "fraction", "R", Ent("N: Text length(1.5)"));
        yield return ("Constraints.length.min", "leading-zero", "R", Ent("N: Text length(007)"));
        yield return ("Constraints.length.min", "arabic-indic", "R", Ent("N: Text length(\u0663)"));
        yield return ("Constraints.length.min", "str", "R", Ent("N: Text length(\"3\")"));
        yield return ("Constraints.length.min", "mis-ident", "R", Ent("N: Text length(three)"));
        yield return ("Constraints.length.max", "ok", "R", Ent("N: Text length(3, 5)"));
        yield return ("Constraints.length.max", "open-upper", "R", Ent("N: Text length(3, )"));
        yield return ("Constraints.length.max", "open-lower", "R", Ent("N: Text length(, 5)"));
        yield return ("Constraints.length.max", "overflow", "R", Ent("N: Text length(3, 99999999999)"));
        yield return ("Constraints.length.max", "str", "R", Ent("N: Text length(3, \"5\")"));
        yield return ("Constraints.pattern", "ok", "R", Ent("N: Text pattern(\"^a+$\")"));
        yield return ("Constraints.pattern", "escapes", "R", Ent("N: Text pattern(\"a\\\"b\\\\c\")"));
        yield return ("Constraints.pattern", "unknown-escape", "R", Ent("N: Text pattern(\"\\d+\")"));
        yield return ("Constraints.pattern", "empty", "R", Ent("N: Text pattern(\"\")"));
        yield return ("Constraints.pattern", "unterminated", "R", "domain A\nX: entity {\n  N: Text pattern(\"abc");
        yield return ("Constraints.pattern", "mis-ident", "R", Ent("N: Text pattern(abc)"));
        yield return ("Constraints.bound.error", "mis-ident", "R", Ent("N: Number range(-x, 5)"));
        yield return ("Constraints.bound.error", "str", "R", Ent("N: Number range(-\"5\", 9)"));
        yield return ("Constraints.bound.error", "mis-punct", "R", Ent("N: Number range(-, 9)"));
        yield return ("Constraints.bound.error", "mis-eof", "R", "domain A\nX: entity {\n  N: Number range(-");
        yield return ("Constraints.bound.text", "ok", "R", Ent("N: Number range(0, 100)"));
        yield return ("Constraints.bound.text", "negative", "R", Ent("N: Number range(-500, )"));
        yield return ("Constraints.bound.text", "decimal", "R", Ent("N: Decimal range(0.01, 1.0)"));
        yield return ("Constraints.bound.text", "fraction-on-int", "R", Ent("N: Number range(1.5, 2)"));
        yield return ("Constraints.bound.text", "integral-fraction", "R", Ent("N: Number range(1.000, 2)"));
        yield return ("Constraints.bound.text", "long-min", "R", Ent("N: Number range(-9223372036854775808, )"));
        yield return ("Constraints.bound.text", "long-overflow", "R", Ent("N: Number range(9223372036854775808, )"));
        yield return ("Constraints.bound.text", "decimal-29", "R", Ent("N: Decimal range(99999999999999999999999999999, )"));
        yield return ("Constraints.bound.text", "decimal-28", "R", Ent("N: Decimal range(7922816251426433759354395033, )"));
        yield return ("Constraints.bound.text", "arabic-indic", "R", Ent("N: Number range(\u0661\u0662, )"));
        yield return ("Constraints.bound.text", "value-member", "R", Doc("V: value { N: Number range(1, 9) }"));
        yield return ("Constraints.bound.text", "str", "R", Ent("N: Number range(\"5\", 9)"));
        yield return ("Constraints.bound.text", "mis-ident", "R", Ent("N: Number range(x, 9)"));

        // Effects
        yield return ("Effects.unsupported.check", "ok", "R", Act("actor"));
        yield return ("Effects.unsupported.check", "case", "R", Act("Schedule"));
        yield return ("Effects.unsupported.check", "name", "R", Act("Kids"));
        yield return ("Effects.unsupported.check", "str", "R", Act("\"actor\""));
        yield return ("Effects.unsupported.message", "ok", "R", Act("PARALLEL"));
        yield return ("Effects.unsupported.message", "function", "R", Act("function"));
        yield return ("Effects.unsupported.message", "str", "R", Act("\"PARALLEL\""));
        yield return ("Effects.expected", "mis-number", "R", Act("5"));
        yield return ("Effects.expected", "mis-string", "R", Act("\"a\\\"b\""));
        yield return ("Effects.expected", "mis-ident", "R", Act("frobnicate"));
        yield return ("Effects.expected", "tail-transition", "R", Act("transition Done"));
        yield return ("Effects.expected", "tail-assign", "R", Act("assign 5"));
        yield return ("Effects.expected", "tail-create", "R", Act("create 5"));
        yield return ("Effects.expected", "tail-if", "R", Act("if N > 1"));
        yield return ("Effects.expected", "tail-link", "R", Act("link 5"));
        yield return ("Effects.expected", "tail-unlink", "R", Act("unlink Kids to x"));
        yield return ("Effects.expected", "mis-punct", "R", Act("->"));
        yield return ("Effects.expected", "mis-keyword", "R", Act("entity"));
        yield return ("Effects.expected", "mis-eof", "R", "domain A\nX: entity {\n  Go: action {\n");
        yield return ("Effects.expected", "when", "R", Act("when Kids Done { }"));
        yield return ("Effects.transition", "ok", "R", Act("transition to Done"));
        yield return ("Effects.transition", "unicode", "R", Act("transition to Gr\u00F6\u00DFe"));
        yield return ("Effects.transition", "word", "R", Act("transition to where"));
        yield return ("Effects.transition", "entry", "R", Doc("X: entity {\n  Done: stage {\n    entry { transition to Done }\n  }\n}"));
        yield return ("Effects.transition", "str", "R", Act("transition to \"Done\""));
        yield return ("Effects.transition", "mis-number", "R", Act("transition to 5"));
        yield return ("Effects.assign", "ok", "R", Act("assign N to 5"));
        yield return ("Effects.assign", "escape", "R", Act("assign N to \"a\\\"b\\\\c\""));
        yield return ("Effects.assign", "unicode", "R", Act("assign \u540D\u524D to N + 1"));
        yield return ("Effects.assign", "str", "R", Act("assign \"N\" to 1"));
        yield return ("Effects.link", "ok", "R", Act("link Kids to k"));
        yield return ("Effects.link", "str", "R", Act("link \"Kids\" to k"));
        yield return ("Effects.unlink", "ok", "R", Act("unlink Kids from k"));
        yield return ("Effects.unlink", "mis-number", "R", Act("unlink 5 from k"));
        yield return ("Effects.create-in", "ok", "R", Act("create in Kids { Qty: 1 }"));
        yield return ("Effects.create-in", "str", "R", Act("create in \"Kids\" { }"));
        yield return ("Effects.create-in", "subscription", "R", Doc("X: entity {\n  Kids: many Kid\n  when Kids Done {\n    create in Kids { Qty: 2 }\n  }\n}\n" + Kid));
        yield return ("Effects.create", "ok", "R", Act("create Kid { Qty: 1.5 }"));
        yield return ("Effects.create", "unicode", "R", Act("create Gr\u00F6\u00DFe { }"));
        yield return ("Effects.create", "str", "R", Act("create \"Kid\" { }"));
        yield return ("Effects.create", "mis-keyword", "R", Act("create Text { }"));
        yield return ("Effects.general-for.where-as-rel", "ok", "R", Act("for k in where { }"));
        yield return ("Effects.general-for.where-as-rel", "case", "R", Act("for k in WHERE { }"));
        yield return ("Effects.general-for.where-as-rel", "name", "R", Act("for k in Kids { }"));
        yield return ("Effects.general-for.where-as-rel", "str", "R", Act("for k in \"where\" { }"));
        yield return ("Effects.general-for.where", "ok", "R", Act("for k in Kids where k Qty > 1 { invoke Pay }"));
        yield return ("Effects.general-for.where", "case", "R", Act("for k in Kids Where Qty > 1 { }"));
        yield return ("Effects.general-for.where", "str", "R", Act("for k in Kids \"where\" Qty > 1 { }"));
        yield return ("Effects.general-for.where", "nested", "R", Act("for k in Kids { for j in Kids { } }"));
        yield return ("Effects.general-for.brace", "mis-number", "R", Act("for k in Kids 5"));
        yield return ("Effects.general-for.brace", "mis-string", "R", Act("for k in Kids \"x\""));
        yield return ("Effects.general-for.brace", "mis-ident", "R", Act("for k in Kids other { }"));
        yield return ("Effects.general-for.brace", "mis-eof", "R", "domain A\nX: entity {\n  Go: action {\n    for k in Kids");
        yield return ("Effects.for.expected", "mis-ident", "R", Act("for Kids k"));
        yield return ("Effects.for.expected", "mis-number", "R", Act("for Kids 5"));
        yield return ("Effects.for.expected", "mis-string", "R", Act("for Kids \"k\""));
        yield return ("Effects.for.expected", "mis-eof", "R", "domain A\nX: entity {\n  Go: action {\n    for Kids");
        yield return ("Effects.for.where", "ok", "R", Act("for Kids as k where k IsPaid invoke k.Pay"));
        yield return ("Effects.for.where", "stage", "R", Act("for Kids as k where k in Done invoke k.Pay(n: 1)"));
        yield return ("Effects.for.where", "case", "R", Act("for Kids as k WHERE k IsPaid invoke k.Pay"));
        yield return ("Effects.for.where", "str", "R", Act("for Kids as k \"where\" k IsPaid invoke k.Pay"));
        yield return ("Effects.for.where", "dot", "R", Act("for Kids as k where k.IsPaid invoke k.Pay"));
        yield return ("Effects.for.policy", "ok", "R", Act("for Kids as k where k Gr\u00F6\u00DFe invoke k.Pay"));
        yield return ("Effects.for.policy", "mis-number", "R", Act("for Kids as k where k 5 invoke k.Pay"));
        yield return ("Effects.for.policy", "mis-string", "R", Act("for Kids as k where k \"IsPaid\" invoke k.Pay"));

        // Entities
        yield return ("Entities.initial", "ok", "R", Doc("X: entity initial(Draft) {\n  Draft: stage { }\n}"));
        yield return ("Entities.initial", "name", "R", Ent("initial: Text"));
        yield return ("Entities.initial", "case", "R", Doc("X: entity Initial(Draft) {\n  Draft: stage { }\n}"));
        yield return ("Entities.initial", "str", "R", Doc("X: entity \"initial\"(Draft) {\n  Draft: stage { }\n}"));
        yield return ("Entities.initial", "repeat", "R", Doc("X: entity initial(A) initial(B) { }"));
        yield return ("Entities.initial", "after-key", "R", Doc("X: entity key(N) initial(A) {\n  N: Text\n}"));
        yield return ("Entities.initial", "no-paren", "R", Doc("X: entity initial {\n}"));
        yield return ("Entities.key-clause.error", "mis-ident", "R", Doc("X: entity key(a b) { }"));
        yield return ("Entities.key-clause.error", "mis-number", "R", Doc("X: entity key(a 5) { }"));
        yield return ("Entities.key-clause.error", "mis-string", "R", Doc("X: entity key(a \"b\") { }"));
        yield return ("Entities.key-clause.error", "mis-eof", "R", "domain A\nX: entity key(a");
        yield return ("Entities.key.first", "ok", "R", Doc("X: entity key(Code, Region) {\n  Code: Text\n  Region: Text\n}"));
        yield return ("Entities.key.first", "name", "R", Ent("key: Text"));
        yield return ("Entities.key.first", "case", "R", Doc("X: entity Key(Code) { Code: Text }"));
        yield return ("Entities.key.first", "str", "R", Doc("X: entity \"key\"(Code) { Code: Text }"));
        yield return ("Entities.key.first", "no-paren", "R", Doc("X: entity key { }"));
        yield return ("Entities.header.unknown-annotation", "ok", "R", Doc("X: entity frob(\"x\") { }"));
        yield return ("Entities.header.unknown-annotation", "empty", "R", Doc("X: entity frob() { }"));
        yield return ("Entities.header.unknown-annotation", "number", "R", Doc("X: entity frob(1.5) { }"));
        yield return ("Entities.key.second", "ok", "R", Doc("X: entity key(a) key(b) { }"));
        yield return ("Entities.key.second", "case", "R", Doc("X: entity key(a) Key(b) { }"));
        yield return ("Entities.key.second", "str", "R", Doc("X: entity key(a) \"key\"(b) { }"));
        yield return ("Entities.legacy-action.error", "mis-number", "R", Ent("Go(n: Number): 5"));
        yield return ("Entities.legacy-action.error", "mis-string", "R", Ent("Go(): \"x\""));
        yield return ("Entities.legacy-action.error", "mis-keyword", "R", Ent("Go(): Text"));
        yield return ("Entities.legacy-action.error", "ok", "R", Ent("Go(n: Number): action { }"));
        yield return ("Entities.typed-line.enum", "ok", "R", Doc("S: enum { A, B }\nX: entity {\n  St: S\n}"));
        yield return ("Entities.typed-line.value", "ok", "R", Doc("V: value { A: Text }\nX: entity {\n  Vv: V\n}"));
        yield return ("Entities.typed-line.primitive", "ok", "R", Ent("Id: Uuid"));
        yield return ("Entities.typed-line.primitive", "unknown-int", "R", Ent("Id: Int"));
        yield return ("Entities.typed-line.primitive", "temporal", "R", "domain A\nuses temporal\nX: entity {\n  Due: Date optional\n}\n");
        yield return ("Entities.typed-line.primitive", "nav", "R", Doc("X: entity {\n  Owner: Y\n}\nY: entity { }"));
        yield return ("Entities.typed-line.primitive", "unknown", "R", Ent("Owner: Nope"));
        yield return ("Entities.typed-line.unsupported", "ok", "R", Ent("Who: actor"));
        yield return ("Entities.typed-line.unsupported", "case", "R", Ent("Who: Schedule"));
        yield return ("Entities.typed-line.unsupported", "function", "R", Ent("Who: function"));
        yield return ("Entities.primitive-name.name", "ok", "R", Ent("Number: Text"));
        yield return ("Entities.primitive-name.name", "text", "R", Ent("Text: Number optional"));
        yield return ("Entities.primitive-name.name", "boolean", "R", Ent("Boolean: Boolean"));
        yield return ("Entities.primitive-name.name", "str", "R", Ent("\"Number\": Text"));
        yield return ("Entities.primitive-name.name", "mis-ident", "R", Ent("Numbers: Text"));
        yield return ("Entities.primitive-name.type-check", "known", "R", Ent("Number: Decimal"));
        yield return ("Entities.primitive-name.type-check", "mis-int", "R", Ent("Number: Int"));
        yield return ("Entities.primitive-name.type-check", "mis-unknown", "R", Ent("Number: Foo"));
        yield return ("Entities.primitive-name.type-check", "str", "R", Ent("Number: \"Text\""));
        yield return ("Entities.primitive-name.type-check", "mis-number", "R", Ent("Number: 5"));
        yield return ("Entities.primitive-name.type-check", "mis-punct", "R", Ent("Number: ->"));
        yield return ("Entities.primitive-name.type-check", "mis-eof", "R", "domain A\nX: entity {\n  Number:");
        yield return ("Entities.primitive-name.error", "mis-keyword", "R", Ent("Text: many"));
        yield return ("Entities.primitive-name.error", "mis-brace", "R", Ent("Text: }"));
        yield return ("Entities.primitive-name.named-type", "ok", "R", Ent("Text: Decimal"));
        yield return ("Entities.primitive-name.named-type", "temporal", "R", "domain A\nuses temporal\nX: entity {\n  Number: Date\n}\n");
        yield return ("Entities.primitive-name.named-type", "mis-primitive", "R", Ent("Text: Text"));
        yield return ("Entities.fallback.name", "ok", "R", Ent("Go: { }"));
        yield return ("Entities.fallback.name", "when", "R", Doc("X: entity {\n  Kids: many Kid\n  Go: when Kids Done { }\n}\n" + Kid));
        yield return ("Entities.fallback.name", "require", "R", Ent("P: policy { true }\n  Go: require P { }"));
        yield return ("Entities.fallback.name", "unicode", "R", Ent("\u540D\u524D: { }"));
        yield return ("Entities.fallback.name", "str", "R", Ent("\"Go\": { }"));
        yield return ("Entities.fallback.unsupported", "str", "R", Ent("Go: \"actor\""));
        yield return ("Entities.fallback.unsupported", "str-case", "R", Ent("Go: \"FUNCTION\""));
        yield return ("Entities.fallback.unsupported", "str-other", "R", Ent("Go: \"Text\""));
        yield return ("Entities.fallback.unsupported", "mis-number", "R", Ent("Go: 5"));
        yield return ("Entities.fallback.unsupported", "mis-punct", "R", Ent("Go: ->"));
        yield return ("Entities.fallback.unsupported", "mis-keyword", "R", Ent("Go: for"));
        yield return ("Entities.fallback.unsupported", "mis-eof", "R", "domain A\nX: entity {\n  Go:");
        yield return ("Entities.body.error", "mis-number", "R", Ent("5"));
        yield return ("Entities.body.error", "mis-string", "R", Ent("\"x\""));
        yield return ("Entities.body.error", "mis-punct", "R", Ent("->"));
        yield return ("Entities.body.error", "mis-keyword", "R", Ent("entity"));
        yield return ("Entities.body.error", "mis-ident", "R", Ent("Lonely"));
        yield return ("Entities.body.error", "mis-eof", "R", "domain A\nX: entity {\n");
        yield return ("Entities.tail.unknown-annotation", "ok", "R", Ent("N: Text frob(\"x\")"));
        yield return ("Entities.tail.unknown-annotation", "number", "R", Ent("N: Text frob(1)"));
        yield return ("Entities.tail.unknown-annotation", "after-constraint", "R", Ent("N: Text optional frob(null)"));
        yield return ("Entities.annotation.keyword", "ok", "R", Doc("X: entity iri(\"http://example.org/X\") label(\"Ex\") {\n  N: Text description(\"the n\")\n}"));
        yield return ("Entities.annotation.keyword", "storage", "R", Ent("N: Text column(\"n_col\")"));
        yield return ("Entities.annotation.keyword", "case", "R", Ent("N: Text Label(\"x\")"));
        yield return ("Entities.annotation.keyword", "str", "R", Ent("N: Text \"label\"(\"x\")"));
        yield return ("Entities.annotation.keyword", "term-misplaced", "R", Ent("N: Text term(\"n\")"));
        yield return ("Entities.annotation.keyword", "duplicate", "R", Ent("N: Text label(\"a\") label(\"b\")"));
        yield return ("Entities.annotation.string", "escapes", "R", Ent("N: Text label(\"a\\\"b\\\\c\")"));
        yield return ("Entities.annotation.string", "unknown-escape", "R", Ent("N: Text label(\"\\d\\n\")"));
        yield return ("Entities.annotation.string", "empty", "R", Ent("N: Text label(\"\")"));
        yield return ("Entities.annotation.string", "unicode", "R", Ent("N: Text label(\"caf\u00E9 \u540D\u524D\")"));
        yield return ("Entities.annotation.string", "trailing-comma", "R", Ent("N: Text label(\"a\",)"));
        yield return ("Entities.annotation.string", "iri-curie", "R", Ent("N: Text iri(\"ex:n\")"));
        yield return ("Entities.annotation.string", "mis-bool", "R", Ent("N: Text label(true)"));
        yield return ("Entities.annotation.number", "ok", "R", Ent("N: Text label(1.5)"));
        yield return ("Entities.annotation.number", "big", "R", Ent("N: Text label(123456789012345678901234567890)"));
        yield return ("Entities.annotation.number", "leading-zero", "R", Ent("N: Text label(007)"));
        yield return ("Entities.annotation.number", "arabic-indic", "R", Ent("N: Text label(\u0663)"));
        yield return ("Entities.annotation.number", "mixed", "R", Ent("N: Text column(\"c\", 2, true, false, null)"));
        yield return ("Entities.annotation.number", "str", "R", Ent("N: Text label(\"1.5\")"));
        yield return ("Entities.annotation.error", "mis-ident", "R", Ent("N: Text label(\"a\", abc)"));
        yield return ("Entities.annotation.error", "mis-punct", "R", Ent("N: Text label(\"a\", ->)"));
        yield return ("Entities.annotation.error", "mis-keyword", "R", Ent("N: Text label(\"a\", entity)"));
        yield return ("Entities.annotation.error", "mis-eof", "R", "domain A\nX: entity {\n  N: Text label(\"a\", ");
        yield return ("Entities.annotation.separator", "mis-string", "R", Ent("N: Text label(\"a\" \"b\")"));
        yield return ("Entities.annotation.separator", "mis-number", "R", Ent("N: Text label(\"a\" 5)"));
        yield return ("Entities.annotation.separator", "mis-ident", "R", Ent("N: Text label(\"a\" b)"));
        yield return ("Entities.annotation.separator", "mis-eof", "R", "domain A\nX: entity {\n  N: Text label(\"a\"");

        // Relationships
        yield return ("Relationships.navline.text", "ok", "R", Doc("X: entity {\n  Owner: Y\n}\nY: entity { }"));
        yield return ("Relationships.navline.text", "unsupported", "R", Ent("Owner: actor"));
        yield return ("Relationships.navline.text", "case", "R", Ent("Owner: PARALLEL"));
        yield return ("Relationships.navline.text", "unknown-target", "R", Ent("Owner: Ghost"));
        yield return ("Relationships.navline.text", "str", "R", Ent("Owner: \"Y\""));
        yield return ("Relationships.navline.primitive-hint", "ok", "R", Ent("Owners: many Text"));
        yield return ("Relationships.navline.primitive-hint", "one", "R", Ent("Owner: one Number"));
        yield return ("Relationships.navline.primitive-hint", "owned", "R", Ent("Owner: owned Boolean"));
        yield return ("Relationships.navline.unexpected", "mis-number", "R", Ent("Owners: many 5"));
        yield return ("Relationships.navline.unexpected", "mis-string", "R", Ent("Owners: many \"Y\""));
        yield return ("Relationships.navline.unexpected", "mis-punct", "R", Ent("Owners: many ->"));
        yield return ("Relationships.navline.unexpected", "mis-brace", "R", Ent("Owners: many }"));
        yield return ("Relationships.navline.unexpected", "mis-eof", "R", "domain A\nX: entity {\n  Owners: many");
        yield return ("Relationships.via.name", "ok", "R", Doc("X: entity {\n  Kids: many Y via parent\n}\nY: entity {\n  parent: X via Kids\n}"));
        yield return ("Relationships.via.name", "unicode", "R", Doc("X: entity {\n  Kids: many Y via Gr\u00F6\u00DFe\n}\nY: entity { }"));
        yield return ("Relationships.via.name", "repeat", "R", Doc("X: entity {\n  Kids: many Y via a via b\n}\nY: entity { }"));
        yield return ("Relationships.via.name", "mis-number", "R", Doc("X: entity {\n  Kids: many Y via 123\n}\nY: entity { }"));
        yield return ("Relationships.via.name", "mis-string", "R", Doc("X: entity {\n  Kids: many Y via \"p\"\n}\nY: entity { }"));
        yield return ("Relationships.via.word", "name", "R", Ent("via: Text"));
        yield return ("Relationships.via.word", "action-name", "R", Ent("via(n: Number): action { }"));
        yield return ("Relationships.via.word", "case", "R", Doc("X: entity {\n  Kids: many Y VIA parent\n}\nY: entity { }"));
        yield return ("Relationships.via.word", "str", "R", Doc("X: entity {\n  Kids: many Y \"via\" parent\n}\nY: entity { }"));
        yield return ("Relationships.via.word", "colon-after", "R", Doc("X: entity {\n  Kid: Y\n  via: Text\n}\nY: entity { }"));

        // Stages
        yield return ("Stages.prev", "ok", "R", Ent("S: stage prev { }"));
        yield return ("Stages.prev", "case", "R", Ent("S: stage Prev { }"));
        yield return ("Stages.prev", "str", "R", Ent("S: stage \"prev\" { }"));
        yield return ("Stages.prev", "require", "R", Ent("P: policy { true }\n  S: stage require not P, P { }"));
        yield return ("Stages.entry.repeat", "ok", "R", Ent("S: stage {\n    entry { }\n    entry { }\n  }"));
        yield return ("Stages.exit.repeat", "ok", "R", Ent("S: stage {\n    exit { }\n    exit { }\n  }"));
        yield return ("Stages.entry-exit.late", "attempt", "R", Ent("S: stage {\n    Go: action { }\n    entry { }\n  }"));
        yield return ("Stages.quantifier.any", "ok", "R", Sub("when any Kids Done { }"));
        yield return ("Stages.quantifier.any", "case", "R", Sub("when ANY Kids Done { }"));
        yield return ("Stages.quantifier.any", "name", "R", Sub("when Kids Done, Done as k { }"));
        yield return ("Stages.quantifier.any", "str", "R", Sub("when \"any\" Kids Done { }"));
        yield return ("Stages.quantifier.all", "ok", "R", Sub("when all Kids Done { transition to Done }"));
        yield return ("Stages.quantifier.all", "case", "R", Sub("when All Kids Done { }"));
        yield return ("Stages.quantifier.all", "rel-named-all", "R", Sub("when all Done { }"));
        yield return ("Stages.quantifier.all", "str", "R", Sub("when \"all\" Kids Done { }"));

        // TopLevel
        yield return ("TopLevel.marked.error", "mis-number", "R", Doc("public 5"));
        yield return ("TopLevel.marked.error", "str", "R", Doc("public \"value\" V { }"));
        yield return ("TopLevel.marked.error", "mis-keyword", "R", Doc("public enum S { }"));
        yield return ("TopLevel.marked.error", "mis-eof", "R", "domain A\npublic");
        yield return ("TopLevel.marked.error", "ok-entity", "R", Doc("public entity E { N: Text }"));
        yield return ("TopLevel.marked.error", "ok-value", "R", Doc("public value V { N: Text }"));
        yield return ("TopLevel.rejection.name", "mis-number", "R", Doc("X: 5"));
        yield return ("TopLevel.rejection.name", "mis-string", "R", Doc("X: \"entity\""));
        yield return ("TopLevel.rejection.unsupported", "ok", "R", Doc("X: actor"));
        yield return ("TopLevel.rejection.unsupported", "case", "R", Doc("X: Function { }"));
        yield return ("TopLevel.rejection.unsupported", "str", "R", Doc("X: \"actor\""));
        yield return ("TopLevel.rejection.unsupported-message", "ok", "R", Doc("X: schedule { }"));
        yield return ("TopLevel.rejection.unsupported-message", "str", "R", Doc("X: \"schedule\" { }"));
        yield return ("TopLevel.rejection.inheritance", "ok", "R", Doc("X: Base entity { }"));
        yield return ("TopLevel.rejection.inheritance-define", "unicode", "R", Doc("X: Gr\u00F6\u00DFe entity { }"));
        yield return ("TopLevel.rejection.inheritance-define", "mis-number", "R", Doc("X: 5 entity { }"));
        yield return ("TopLevel.rejection.inheritance", "str", "R", Doc("X: \"Base\" entity { }"));
        yield return ("TopLevel.rejection.expected", "mis-ident", "R", Doc("X Y"));
        yield return ("TopLevel.rejection.expected", "mis-punct", "R", Doc("X: ->"));
        yield return ("TopLevel.rejection.expected", "mis-unknown-word", "R", Doc("X: widget { }"));
        yield return ("TopLevel.rejection.expected", "mis-eof", "R", "domain A\nX:");
        yield return ("TopLevel.named.kind", "value", "R", Doc("V: value { A: Text B: Number range(0, 9) }"));
        yield return ("TopLevel.named.kind", "contract", "R", Contract("external src v1", "E: inbound event Text"));
        yield return ("TopLevel.named.kind", "bind", "R", Doc("B: bind C E to Go p"));
        yield return ("TopLevel.named.kind", "case", "R", Doc("V: Value { A: Text }"));
        yield return ("TopLevel.named.kind", "str", "R", Doc("V: \"value\" { A: Text }"));
        yield return ("TopLevel.enum.member", "ok", "R", Doc("S: enum { A, B, C }"));
        yield return ("TopLevel.enum.member", "unicode", "R", Doc("S: enum { Gr\u00F6\u00DFe, \u540D\u524D }"));
        yield return ("TopLevel.enum.member", "words", "R", Doc("S: enum { where, any, via, initial, key, value }"));
        yield return ("TopLevel.enum.member", "str", "R", Doc("S: enum { \"A\" }"));
        yield return ("TopLevel.enum.member", "mis-number", "R", Doc("S: enum { 5 }"));
        yield return ("TopLevel.enum.member-annotation", "ok", "R", Doc("S: enum {\n  A iri(\"http://example.org/a\") term(\"a\") label(\"Ay\"),\n  B\n}"));
        yield return ("TopLevel.enum.member-annotation", "unknown", "R", Doc("S: enum { A frob(\"x\") }"));
        yield return ("TopLevel.enum.member-annotation", "storage", "R", Doc("S: enum { A column(\"x\") }"));
        yield return ("TopLevel.enum.member-annotation", "header", "R", Doc("S: enum label(\"Status\") { A }"));
        yield return ("TopLevel.enum.member-annotation", "str", "R", Doc("S: enum { A \"label\"(\"x\") }"));
        yield return ("TopLevel.enum.reserved.word", "keyword", "R", Doc("S: enum { A, Number }"));
        yield return ("TopLevel.enum.reserved.word", "keyword-first", "R", Doc("S: enum { entity }"));
        yield return ("TopLevel.enum.reserved.word", "str", "R", Doc("S: enum { Red \"abc\" }"));
        yield return ("TopLevel.enum.reserved.length", "str-empty", "R", Doc("S: enum { Red \"\" }"));
        yield return ("TopLevel.enum.reserved.letter", "str-digit", "R", Doc("S: enum { Red \"5x\" }"));
        yield return ("TopLevel.enum.reserved.letter", "mis-number", "R", Doc("S: enum { A 5 }"));
        yield return ("TopLevel.enum.reserved.letter", "mis-punct", "R", Doc("S: enum { A -> }"));
        yield return ("TopLevel.enum.reserved.letter", "mis-colon", "R", Doc("S: enum { A: B }"));
        yield return ("TopLevel.enum.reserved.length", "mis-eof", "R", "domain A\nS: enum { A");
        yield return ("TopLevel.function.start", "ok", "R", Doc("function Twice(a: Number): Number = a * 2"));
        yield return ("TopLevel.function.start", "name-form", "R", Doc("function: entity { N: Text }"));
        yield return ("TopLevel.function.start", "case", "R", Doc("Function Twice(a: Number): Number = a"));
        yield return ("TopLevel.function.start", "str", "R", Doc("\"function\" Twice(a: Number): Number = a"));
        yield return ("TopLevel.function.name-error", "mis-number", "R", Doc("function 5(a: Number): Number = a"));
        yield return ("TopLevel.function.name-error", "mis-string", "R", Doc("function \"F\"(a: Number): Number = a"));
        yield return ("TopLevel.function.name-error", "mis-punct", "R", Doc("function (a: Number): Number = a"));
        yield return ("TopLevel.function.name-error", "mis-eof", "R", "domain A\nfunction");
        yield return ("TopLevel.function.name", "unicode", "R", Doc("function Gr\u00F6\u00DFe(): Text = \"a\\\"b\""));
        yield return ("TopLevel.function.name", "trailing-comma", "R", Doc("function F(a: Number,): Number = a"));
        yield return ("TopLevel.function.name", "mis-keyword", "R", Doc("function Text(a: Number): Number = a"));
        yield return ("TopLevel.function.separator", "mis-ident", "R", Doc("function F(a: Number b: Number): Number = a"));
        yield return ("TopLevel.function.separator", "mis-number", "R", Doc("function F(a: Number 5): Number = a"));
        yield return ("TopLevel.function.equals", "mis-number", "R", Doc("function F(a: Number): Number 5"));
        yield return ("TopLevel.function.equals", "mis-string", "R", Doc("function F(): Text \"x\""));
        yield return ("TopLevel.function.equals", "mis-eof", "R", "domain A\nfunction F(): Number");
        yield return ("TopLevel.value.primitive", "ok", "R", Doc("V: value { A: Text B: Number C: Boolean D: Int }"));
        yield return ("TopLevel.value.primitive", "optional", "R", Doc("V: value { A: Text optional }"));
        yield return ("TopLevel.value.primitive", "str", "R", Doc("V: value { A: \"Text\" }"));
        yield return ("TopLevel.contract.external", "ok", "R", Contract("external src v1", "E: inbound operation Text"));
        yield return ("TopLevel.contract.external", "case", "R", Contract("External src v1", "E: inbound operation Text"));
        yield return ("TopLevel.contract.external", "str", "R", Contract("\"external\" src v1", "E: inbound operation Text"));
        yield return ("TopLevel.contract.internal", "ok", "R", Contract("internal billing v2", "E: outbound event Number"));
        yield return ("TopLevel.contract.internal", "mis-number", "R", Contract("5 billing v2", "E: outbound event Number"));
        yield return ("TopLevel.contract.source-string", "ok", "R", Contract("external \"https://x.example/a\\\"b\" \"2024-11-20\"", "E: inbound operation Text"));
        yield return ("TopLevel.contract.source-string", "mis-number", "R", Contract("external 5 v1", "E: inbound operation Text"));
        yield return ("TopLevel.contract.version-string", "ok", "R", Contract("external src \"v\\\\1\"", "E: inbound operation Text"));
        yield return ("TopLevel.contract.version-string", "mis-number", "R", Contract("external src 1", "E: inbound operation Text"));
        yield return ("TopLevel.contract.value-word", "ok", "R", Contract("internal billing v1", "P: value { A: Text }\n  E: outbound operation P"));
        yield return ("TopLevel.contract.value-word", "case", "R", Contract("internal billing v1", "P: Value { A: Text }"));
        yield return ("TopLevel.contract.value-word", "str", "R", Contract("internal billing v1", "P: \"value\" { A: Text }"));
        yield return ("TopLevel.contract.inbound", "ok", "R", Contract("external src v1", "E: inbound operation Text"));
        yield return ("TopLevel.contract.inbound", "case", "R", Contract("external src v1", "E: Inbound operation Text"));
        yield return ("TopLevel.contract.inbound", "str", "R", Contract("external src v1", "E: \"inbound\" operation Text"));
        yield return ("TopLevel.contract.outbound", "ok", "R", Contract("external src v1", "E: outbound event Text"));
        yield return ("TopLevel.contract.outbound", "str", "R", Contract("external src v1", "E: \"outbound\" event Text"));
        yield return ("TopLevel.contract.operation", "ok", "R", Contract("external src v1", "E: inbound operation Boolean"));
        yield return ("TopLevel.contract.operation", "str", "R", Contract("external src v1", "E: inbound \"operation\" Text"));
        yield return ("TopLevel.contract.event", "ok", "R", Contract("external src v1", "E: inbound event P"));
        yield return ("TopLevel.contract.event", "case", "R", Contract("external src v1", "E: inbound Event Text"));
        yield return ("TopLevel.contract.event", "str", "R", Contract("external src v1", "E: inbound \"event\" Text"));
        yield return ("TopLevel.contract.payload-primitive", "ok", "R", Contract("external src v1", "E: inbound event Number"));
        yield return ("TopLevel.contract.payload-primitive", "mis-string", "R", Contract("external src v1", "E: inbound event \"Text\""));
        yield return ("TopLevel.contract.payload-primitive", "duplicate", "R", Contract("external src v1", "E: inbound event Text") + "C: contract external src v1 { }\n");
        yield return ("TopLevel.word.check", "bind", "R", Doc("B: bind C E to Go p"));
        yield return ("TopLevel.word.error", "attempt", "R", Doc("V: value"));

        // Types
        yield return ("Types.expected", "mis-number", "R", Doc("function F(a: 5): Number = a"));
        yield return ("Types.expected", "mis-string", "R", Doc("function F(a: \"Text\"): Number = a"));
        yield return ("Types.expected", "mis-keyword", "R", Doc("function F(a: Number): entity = a"));
        yield return ("Types.expected", "mis-eof", "R", "domain A\nfunction F(a:");
        yield return ("Types.expected", "ok", "R", Doc("function F(a: Text, b: Date2): Boolean = true"));

        // Expression parser, fragment, folds
        yield return ("Fragment.trailing", "mis-punct", "F", "a )");
        yield return ("Fragment.trailing", "mis-number", "F", "a 5");
        yield return ("Fragment.trailing", "mis-string", "F", "a \"s\"");
        yield return ("Fragment.trailing", "mis-keyword", "F", "a entity");
        yield return ("Expression.NameOf", "exists", "F", "Kids exists");
        yield return ("Expression.NameOf", "count", "F", "count Kids > 1");
        yield return ("Expression.NameOf", "sum", "F", "sum Kids Price");
        yield return ("Expression.NameOf", "quant-where", "F", "ANY Kids where Qty > 1");
        yield return ("Expression.NameOf", "where-nav", "F", "Kids where Qty > 1");
        yield return ("Expression.NameOf", "path", "F", "Order Customer Gr\u00F6\u00DFe");
        yield return ("Expression.NameOf", "path-cmp", "F", "Order Price >= 3");
        yield return ("Expression.NameOf", "duration", "F", "3 Days");
        yield return ("Expression.NameOf", "duration-fraction", "F", "1.5 Days");
        yield return ("Expression.NameOf", "duration-overflow", "F", "99999999999999999999 Days");
        yield return ("Expression.NameOf", "unknown-unit", "F", "3 Dayz");
        yield return ("Expression.NameOf", "call", "F", "Twice(2, \"x\")");
        yield return ("Expression.NameOf", "match-empty-arm", "F", "match { , }");
        yield return ("Expression.NameOf", "match-no-arrow", "F", "match { a b }");
        yield return ("Expression.NameOf", "match-no-arrow-string", "F", "match { \"s\" b }");
        yield return ("Expression.NameOf", "resolver", "R", Ent("N: Number\n  P: policy { count Kids > N and Kids exists }\n  Kids: many X"));
        yield return ("Expression.Describe", "eof", "F", "a +");
        yield return ("Expression.Describe", "punct", "F", ")");
        yield return ("Expression.Describe", "group", "F", "(a b)");
        yield return ("Expression.Describe", "call", "F", "F(1 2)");
        yield return ("Expression.Describe", "call-empty", "F", "F(1, )");
        yield return ("Expression.Describe", "match-condition", "F", "match { a b -> 1 }");
        yield return ("Expression.Describe", "match-value", "F", "match { a -> 1 2 }");
        yield return ("Expression.Describe", "resolver", "R", Ent("P: policy { -> }"));
        yield return ("Fold.text", "string", "R", Ent("N: Text default(\"a\\\"b\\\\c\")"));
        yield return ("Fold.text", "string-unknown-escape", "R", Ent("N: Text default(\"\\d\")"));
        yield return ("Fold.text", "ident", "R", Ent("N: Text default(Other)"));
        yield return ("Fold.text", "fragment-string", "F", "\"caf\u00E9\\\"\"");
        yield return ("Fold.text", "fragment-ident", "F", "Gr\u00F6\u00DFe");
        yield return ("Fold.number", "long", "R", Ent("N: Number default(42)"));
        yield return ("Fold.number", "decimal", "R", Ent("N: Decimal default(1.50)"));
        yield return ("Fold.number", "integral-decimal", "R", Ent("N: Number default(1.0)"));
        yield return ("Fold.number", "long-overflow", "R", Ent("N: Decimal default(9223372036854775808)"));
        yield return ("Fold.number", "decimal-overflow", "R", Ent("N: Decimal default(99999999999999999999999999999)"));
        yield return ("Fold.number", "arabic-indic", "R", Ent("N: Number default(\u0661\u0662)"));
        yield return ("Fold.number", "equals", "R", Ent("N: Number equals(7)"));
        yield return ("Fold.number", "fragment", "F", "0.10 + 007");
        yield return ("Fold.number", "str", "R", Ent("N: Number default(\"42\")"));
        yield return ("Temporal.amount.parse", "ok", "R", Temporal("Now - 12 Days > Due"));
        yield return ("Temporal.amount.parse", "singular", "R", Temporal("Due + 1 Day > Now"));
        yield return ("Temporal.amount.parse", "str", "R", Temporal("Now - \"12\" Days > Due"));
        yield return ("Temporal.amount.message", "fraction", "R", Temporal("Now - 1.5 Days > Due"));
        yield return ("Temporal.amount.message", "overflow", "R", Temporal("Now - 99999999999999999999 Days > Due"));
        yield return ("Temporal.amount.message", "mis-ident", "R", Temporal("Now - Twelve Days > Due"));
        yield return ("Temporal.unit.lookup", "ms", "R", Temporal("Now - 5 ms > Due"));
        yield return ("Temporal.unit.lookup", "unknown", "R", Temporal("Now - 5 Dayz > Due"));
        yield return ("Temporal.unit.lookup", "str", "R", Temporal("Now - 5 \"Days\" > Due"));
        yield return ("Temporal.unit.message", "attempt", "R", Temporal("Now - 5 days > Due"));

        // Outline (runs on broken documents)
        yield return ("Outline.domain", "ok", "O", Doc("X: entity { N: Text }"));
        yield return ("Outline.domain", "str", "O", "domain \"A\\\"b\"\nX: entity { }\n");
        yield return ("Outline.domain", "mis-number", "O", "domain 5\n");
        yield return ("Outline.domain", "mis-eof", "O", "domain");
        yield return ("Outline.public-value", "ok", "O", Doc("public value V { A: Text }"));
        yield return ("Outline.public-value", "entity", "O", Doc("public entity E { A: Text }"));
        yield return ("Outline.public-value", "str", "O", Doc("public \"value\" V { A: Text }"));
        yield return ("Outline.public-value", "case", "O", Doc("public Value V { A: Text }"));
        yield return ("Outline.public-value", "mis-eof", "O", "domain A\npublic value");
        yield return ("Outline.function", "ok", "O", Doc("function F(a: Number): Number = a\nX: entity { }"));
        yield return ("Outline.function", "name-form", "O", Doc("function: entity { }"));
        yield return ("Outline.function", "case", "O", Doc("Function F(a: Number): Number = a"));
        yield return ("Outline.function", "str", "O", Doc("\"function\" F(): Number = 1"));
        yield return ("Outline.top.name", "ok", "O", Doc("X: entity { N: Text }\nS: enum { A }\nV: value { }\nC: contract external s v { }\nB: bind C E to Go p"));
        yield return ("Outline.top.name", "unicode", "O", Doc("Gr\u00F6\u00DFe: entity { }"));
        yield return ("Outline.top.name", "str", "O", Doc("\"X\": entity { N: Text }"));
        yield return ("Outline.top.kind", "str", "O", Doc("X: \"enum\" { A }"));
        yield return ("Outline.top.kind", "str-entity", "O", Doc("X: \"entity\" { N: Text }"));
        yield return ("Outline.top.kind", "mis-number", "O", Doc("X: 5"));
        yield return ("Outline.top.kind", "mis-eof", "O", "domain A\nX:");
        yield return ("Outline.top.kind", "case", "O", Doc("X: Enum { A }"));
        yield return ("Outline.marked.kind", "ok", "O", Doc("public entity E { N: Text }"));
        yield return ("Outline.marked.kind", "value", "O", Doc("public value V { N: Text }"));
        yield return ("Outline.marked.name", "str", "O", Doc("public \"value\" \"V\" { N: Text }"));
        yield return ("Outline.marked.name", "mis-number", "O", Doc("public value 5 { }"));
        yield return ("Outline.marked.name", "mis-punct", "O", Doc("public entity -> { }"));
        yield return ("Outline.member.unnamed", "ok", "O", Doc("X: entity {\n  Kids: many Kid\n  when Kids Done { }\n  Done: stage {\n    entry { }\n    exit { }\n  }\n  Go: action require P { }\n}"));
        yield return ("Outline.member.unnamed", "str", "O", Ent("\"when\" Kids Done { }"));
        yield return ("Outline.member.public-action", "ok", "O", Ent("public action Go { }"));
        yield return ("Outline.member.public-action", "str", "O", Ent("public action \"Go\" { }"));
        yield return ("Outline.member.public-action", "mis-number", "O", Ent("public action 5 { }"));
        yield return ("Outline.member.public-action", "mis-eof", "O", "domain A\nX: entity {\n  public action");
        yield return ("Outline.member.name", "ok", "O", Ent("N: Text\n  Number: Text\n  S: stage { }\n  P: policy { true }\n  K: many Kid\n  Go: action { }"));
        yield return ("Outline.member.name", "unicode", "O", Ent("\u540D\u524D: Text"));
        yield return ("Outline.member.name", "str", "O", Ent("\"N\": Text"));
        yield return ("Outline.member.legacy-action", "ok", "O", Ent("Go(n: Number): action { }"));
        yield return ("Outline.member.legacy-action", "annotation", "O", Ent("N: Text\n  label(\"x\"): Text"));
        yield return ("Outline.member.legacy-action", "str", "O", Ent("\"Go\"(n: Number): action { }"));

        // Edit
        yield return ("Edit.marked-name", "entity", "E", AddTop("public entity E { N: Text }"));
        yield return ("Edit.marked-name", "value", "E", AddTop("public value V { N: Text }"));
        yield return ("Edit.marked-name", "action", "E", AddMember("public action Go { }"));
        yield return ("Edit.marked-name", "keyword-name", "E", AddMember("public action Text { }"));
        yield return ("Edit.marked-name", "mis-string", "E", AddTop("public entity \"E\" { }"));
        yield return ("Edit.marked-name", "str-kind", "E", AddTop("public \"value\" V { }"));
        yield return ("Edit.name", "ok", "E", AddTop("Y: entity { N: Text }"));
        yield return ("Edit.name", "member", "E", AddMember("M: Number"));
        yield return ("Edit.name", "keyword-name", "E", AddMember("Number: Text"));
        yield return ("Edit.name", "duplicate", "E", AddMember("N: Number"));
        yield return ("Edit.name", "replace", "E", ReplaceTop("X", "X: entity { M: Text }"));
        yield return ("Edit.name", "mis-string", "E", AddTop("\"Y\": entity { }"));
        yield return ("Edit.top-kind", "enum", "E", AddTop("T: enum { A }"));
        yield return ("Edit.top-kind", "str", "E", AddTop("T: \"enum\" { A }"));
        yield return ("Edit.top-kind", "value", "E", AddTop("T: value { A: Text }"));
        yield return ("Edit.top-kind", "contract", "E", AddTop("T: contract external s v { }"));
        yield return ("Edit.top-kind", "bind", "E", AddTop("T: bind C E to Go p"));
        yield return ("Edit.top-kind", "mis-number", "E", AddTop("T: 5"));
        yield return ("Edit.top-kind", "case", "E", AddTop("T: Enum { A }"));
        yield return ("Edit.top-kind", "uses", "E", AddTop("uses temporal"));

        // Compiler extension diagnostics
        yield return ("Compiler.extension", "unknown", "C", Doc("uses nope\nX: entity { }"));
        yield return ("Compiler.extension", "unknown-second", "C", Doc("uses temporal\nuses nope\nX: entity { }"));
        yield return ("Compiler.extension", "str-later", "C", "domain A\nuses nope\nX: entity { }\nuses \"nope\"\n");
        yield return ("Compiler.extension", "ok", "C", Doc("uses temporal\nX: entity { Due: Date }"));
    }

    // ── the mutation sweep (documents that jointly contain every reader group) ──────

    internal static readonly (string Name, string Text)[] SweepDocuments = [
        ("entities", "domain A\nuses temporal\nS: enum { A iri(\"http://e.org/a\"), B }\nX: entity iri(\"http://e.org/x\") key(Code) {\n  Code: Text length(3, 5) pattern(\"^a\\\"b$\")\n  N: Number range(-5, 10) default(1.5)\n  Number: Text\n  St: S\n  Due: Date optional label(\"due\")\n  Kids: many Y via parent\n}\nY: entity {\n  parent: X\n}\n"),
        ("stages", "domain A\nX: entity initial(Draft) {\n  Kids: many Y\n  Draft: stage require P {\n    entry { assign N to 2 }\n    when any Kids Done { transition to Done }\n    Go: action { transition to Done }\n  }\n  Done: stage { }\n  N: Number\n  P: policy { N > 1 }\n}\nY: entity {\n  Done: stage { }\n}\n"),
        ("effects", "domain A\nX: entity {\n  N: Number\n  Kids: many Y\n  Go: action (n: Number) {\n    if (n > 1) { assign N to n } else { create in Kids { Q: 1 } }\n    for k in Kids where Q > 0 { link Kids to k }\n    for Kids as k where k Ok invoke k.Pay(q: 2)\n    create Y { Q: \"a\\\"b\" }\n    invoke Done\n  }\n  Done: action { }\n}\nY: entity {\n  Q: Number\n  Ok: policy { Q > 0 }\n  Pay: action (q: Number) { }\n}\n"),
        ("toplevel", "domain A\nfunction Twice(a: Number): Number = a * 2\nV: value { A: Text B: Number }\npublic value W { C: Boolean }\nC: contract external \"src\" v1 {\n  P: value { A: Text }\n  E: inbound operation P\n  F: outbound event Text\n}\npublic entity X {\n  Go: action (p: P) { }\n  public action Run { }\n}\nB: bind C E to Go p\n"),
        ("expressions", "domain A\nuses temporal\nX: entity {\n  Due: Date\n  Kids: many Y\n  Late: policy { Now - 3 Days > Due and count Kids > 2 }\n  Big: policy { sum Kids Q >= 10 or any Kids where Q > 1 }\n  M: policy { match { Kids exists -> true, else -> false } }\n}\nY: entity {\n  Q: Number\n}\n"),
    ];

    private static readonly (string Name, string Text)[] SweepFillers = [
        ("ident", "zz"), ("keyword", "entity"), ("text", "Text"), ("number", "7"), ("string", "\"q\\\"r\""), ("punct", "->"), ("eof", ""),
    ];

    // ── doors ─────────────────────────────────────────────────────────────────────────

    private static string Run(string door, string input) => door switch {
        "R" => Resolve(input),
        "O" => Outline(input),
        "E" => EditResult(input),
        "C" => CompilerResult(input),
        "F" => FragmentResult(input),
        _ => throw new InvalidOperationException($"Unknown door '{door}'."),
    };

    private static HashSet<string>? _seed;

    /// <summary>The changes every document carries (built-in primitives), dropped from each row so it shows only what the input built.</summary>
    private static HashSet<string> Seed() {
        if (_seed is not null)
            return _seed;
        const string empty = "domain A\n";
        var session = DomainSession.ForSource(empty, ExtensionCatalog.ProductAuthoring, ProductCatalog.Catalog);
        var seed = new DomainResolver(DslSyntaxTree.Parse(empty), session).Resolve()
            .Select(Canonical).Where(p => !p.StartsWith("SetDomainNameChange(", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        return _seed = seed;
    }

    internal static List<string> Changes(string text) {
        var session = DomainSession.ForSource(text, ExtensionCatalog.ProductAuthoring, ProductCatalog.Catalog);
        return [.. new DomainResolver(DslSyntaxTree.Parse(text), session).Resolve().Select(Canonical)];
    }

    internal static string Resolve(string text) {
        try {
            var seed = Seed();
            return "OK " + string.Join(" ; ", Changes(text).Where(p => !seed.Contains(p)));
        }
        catch (Exception ex) {
            return Failure(ex);
        }
    }

    private static string Failure(Exception ex) => ex switch {
        GrammarException g => $"ERR {g.Detail} @{g.Line}:{g.Column}",
        FormatException f => $"ERR FormatException: {f.Message}",
        _ => $"ERR! {ex.GetType().Name}: {ex.Message}",
    };

    internal static string Outline(string text) {
        try {
            var sb = new StringBuilder("OK ");
            foreach (var node in DslOutline.Build(text).Roots)
                PrintOutline(node, sb);
            return sb.ToString();
        }
        catch (Exception ex) {
            return Failure(ex);
        }
    }

    private static void PrintOutline(OutlineNode n, StringBuilder sb) {
        sb.Append('<').Append(n.Kind).Append(':').Append(Quote(n.Name)).Append(" path=").Append(Quote(n.Path))
            .Append(" off=").Append(n.StartOffset).Append('-').Append(n.EndOffset)
            .Append(" lines=").Append(n.StartLine).Append('-').Append(n.EndLine)
            .Append(" indent=").Append(Quote(n.Indent)).Append(n.Addressable ? " addr" : "")
            .Append(" braces=").Append(n.OpenBrace).Append(',').Append(n.CloseBrace)
            .Append(" shared=").Append(n.SharedLine);
        foreach (var child in n.Children)
            PrintOutline(child, sb);
        sb.Append('>');
    }

    private static string EditResult(string input) {
        var split = input.IndexOf('\u0001');
        try {
            return "OK " + Canonical(PolyEdit.Apply(input[..split], input[(split + 1)..]));
        }
        catch (Exception ex) {
            return Failure(ex);
        }
    }

    private static PolyCompiler? _compiler;

    private static string CompilerResult(string text) {
        try {
            _compiler ??= new PolyCompiler(ProductCatalog.Catalog, new SessionOptions(ProductCatalog.Catalog, ExtensionCatalog.ProductAuthoring));
            var result = _compiler.Analyze(text, "baseline.poly");
            return "OK " + string.Join(" ; ", result.Diagnostics.Select(Canonical));
        }
        catch (Exception ex) {
            return Failure(ex);
        }
    }

    private static string FragmentResult(string text) {
        try {
            return "OK " + Canonical(DslExpressionFragment.ParseExpressionFragment(text));
        }
        catch (Exception ex) {
            return Failure(ex);
        }
    }

    // ── canonical print: type, then every public property by name; culture-invariant ─────

    private static readonly Dictionary<Type, PropertyInfo[]> PropertyCache = [];

    internal static string Canonical(object? value) {
        var sb = new StringBuilder();
        Print(value, sb, 0);
        return sb.ToString();
    }

    private static string Quote(string s) {
        var sb = new StringBuilder("\"");
        foreach (var ch in s) {
            switch (ch) {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                default:
                    if (ch < ' ' || ch > '~')
                        sb.Append("\\u").Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(ch);
                    break;
            }
        }

        return sb.Append('"').ToString();
    }

    private static void Print(object? value, StringBuilder sb, int depth) {
        if (depth > 60)
            throw new InvalidOperationException("Canonical print is deeper than 60 levels; a cycle or an unexpected graph.");
        switch (value) {
            case null:
                sb.Append("null");
                return;
            case string s:
                sb.Append(Quote(s));
                return;
            case bool b:
                sb.Append(b ? "true" : "false");
                return;
            case Enum e:
                sb.Append(e.GetType().Name).Append('.').Append(e);
                return;
            case Type t:
                sb.Append("typeof(").Append(t.Name).Append(')');
                return;
            case DslToken or SourceText or Delegate:
                throw new InvalidOperationException($"A built value holds a {value.GetType().Name}.");
            case IFormattable f when value.GetType().IsPrimitive || value is decimal:
                sb.Append(value.GetType().Name).Append(':').Append(f.ToString(null, CultureInfo.InvariantCulture));
                return;
            case IDictionary dictionary: {
                    var entries = new List<string>();
                    foreach (DictionaryEntry entry in dictionary)
                        entries.Add(Canonical(entry.Key) + "=" + Canonical(entry.Value));
                    entries.Sort(StringComparer.Ordinal);
                    sb.Append('{').Append(string.Join(",", entries)).Append('}');
                    return;
                }
            case IEnumerable list: {
                    var items = new List<string>();
                    foreach (var item in list) {
                        var inner = new StringBuilder();
                        Print(item, inner, depth + 1);
                        items.Add(inner.ToString());
                    }
                    if (IsUnordered(value.GetType()))
                        items.Sort(StringComparer.Ordinal);
                    sb.Append('[').Append(string.Join(",", items)).Append(']');
                    return;
                }
        }

        var type = value.GetType();
        PropertyInfo[] props;
        lock (PropertyCache) {
            if (!PropertyCache.TryGetValue(type, out props!)) {
                props = [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    // A NodeId is minted per run, so it is not part of what the input built.
                    .Where(p => p.GetIndexParameters().Length == 0 && p.Name != "EqualityContract" && p.PropertyType != typeof(NodeId))
                    .OrderBy(p => p.Name, StringComparer.Ordinal)];
                PropertyCache[type] = props;
            }
        }

        sb.Append(type.Name).Append('(');
        for (var i = 0; i < props.Length; i++) {
            if (i > 0) sb.Append(',');
            sb.Append(props[i].Name).Append('=');
            object? inner;
            try {
                inner = props[i].GetValue(value);
            }
            catch (TargetInvocationException ex) {
                sb.Append('!').Append(ex.InnerException?.GetType().Name);
                continue;
            }
            Print(inner, sb, depth + 1);
        }
        sb.Append(')');
    }

    private static bool IsUnordered(Type type) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISet<>))
        || type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReadOnlySet<>));

    private static string Sha(string text, int length) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..length];

    // ── derived records ───────────────────────────────────────────────────────────────

    private static string Escape(string s) => DslTokenStreamBaselineTests.Escape(s);

    private static string Unescape(string s) => DslTokenStreamBaselineTests.Unescape(s);

    /// <summary>Mutation-sweep outcomes for one token of a sweep document: the resolver's and the outline's result per filler.</summary>
    internal static List<(string Filler, string Door, string Outcome)> SweepOutcomes(string text, DslToken token) {
        var outcomes = new List<(string, string, string)>();
        foreach (var (name, filler) in SweepFillers) {
            var mutated = filler.Length == 0
                ? text[..token.Start]
                : text[..token.Start] + filler + text[(token.Start + token.Length)..];
            outcomes.Add((name, "R", Resolve(mutated)));
            outcomes.Add((name, "O", Outline(mutated)));
        }

        return outcomes;
    }

    /// <summary>N rows, then one X row per outcome that is not a positioned grammar error or a success (its exact message).</summary>
    private static IEnumerable<string> SweepRecords(Dictionary<string, int> exceptionTypes) {
        var unusual = new List<string>();
        foreach (var (name, text) in SweepDocuments) {
            var tokens = DslSyntaxTree.Parse(text).Tokens;
            for (var i = 0; i < tokens.Count - 1; i++) {
                var outcomes = SweepOutcomes(text, tokens[i]);
                foreach (var (filler, door, outcome) in outcomes) {
                    if (Tally(outcome, exceptionTypes) is not (null or "GrammarException"))
                        unusual.Add($"X\t{name}\t{i}\t{filler}\t{door}\t{Escape(outcome)}");
                }
                yield return $"N\t{name}\t{i}\t{tokens[i].Kind}\t{Sha(string.Join("\n", outcomes.Select(o => o.Outcome)), 16)}";
            }
        }

        foreach (var row in unusual)
            yield return row;
    }

    private static string? Tally(string outcome, Dictionary<string, int> exceptionTypes) {
        var type = outcome.StartsWith("ERR! ", StringComparison.Ordinal) ? outcome[5..outcome.IndexOf(':')]
            : outcome.StartsWith("ERR FormatException:", StringComparison.Ordinal) ? "FormatException"
            : outcome.StartsWith("ERR ", StringComparison.Ordinal) ? "GrammarException"
            : null;
        if (type is not null)
            exceptionTypes[type] = exceptionTypes.GetValueOrDefault(type) + 1;
        return type;
    }

    private static IEnumerable<string> CorpusRecords() {
        foreach (var domain in GoldenDomains.Lowerable) {
            var text = File.ReadAllText(Path.Combine(GoldenDomains.RepoRoot, domain.Path));
            var changes = Changes(text);
            yield return $"D\t{domain.Name}\t{Sha(text, 16)}\t{changes.Count}";
            for (var i = 0; i < changes.Count; i++) {
                var head = changes[i];
                yield return $"M\t{domain.Name}\t{i}\t{head[..head.IndexOf('(')]}\t{Sha(head, 12)}";
            }
        }
    }

    private static IEnumerable<(string Key, string Text)> OutlineInputs() {
        foreach (var domain in GoldenDomains.Lowerable)
            yield return ("golden:" + domain.Name, File.ReadAllText(Path.Combine(GoldenDomains.RepoRoot, domain.Path)));
        foreach (var (name, text) in SweepDocuments)
            yield return ("sweep:" + name, text);
    }

    private static string OutlineRecord(string text) {
        var outline = Outline(text);
        return outline.Length <= 400 ? outline : $"len={outline.Length} sha={Sha(outline, 16)}";
    }

    private static List<string> Generate() {
        var lines = new List<string> {
            "# G-13a frozen consumer baseline, written while every consumer read DslToken.Text. See ResolverBaselineTests.",
            "# S site<TAB>case<TAB>door<TAB>input<TAB>result | D doc<TAB>textSha<TAB>changes | M doc<TAB>index<TAB>type<TAB>sha | O key<TAB>input<TAB>outline | N doc<TAB>token<TAB>kind<TAB>sha | X doc<TAB>token<TAB>filler<TAB>door<TAB>outcome | T exceptionType<TAB>count",
        };
        foreach (var (site, @case, door, input) in Cases())
            lines.Add($"S\t{site}\t{@case}\t{door}\t{Escape(input)}\t{Escape(Run(door, input))}");
        lines.AddRange(CorpusRecords());
        foreach (var (key, text) in OutlineInputs())
            lines.Add($"O\t{key}\t{Escape(text)}\t{Escape(OutlineRecord(text))}");
        var exceptionTypes = new Dictionary<string, int>(StringComparer.Ordinal);
        lines.AddRange(SweepRecords(exceptionTypes));
        foreach (var (type, count) in exceptionTypes.OrderBy(p => p.Key, StringComparer.Ordinal))
            lines.Add($"T\t{type}\t{count}");
        return lines;
    }

    private sealed record Row(string Kind, string[] Fields);

    private static List<Row> ReadBaseline() {
        var rows = new List<Row>();
        foreach (var line in File.ReadAllLines(Path.Combine(GuideBlocks.RepoRoot(), BaselinePath))) {
            if (line.Length == 0 || line[0] == '#')
                continue;
            var fields = line.Split('\t');
            var expected = fields[0] switch {
                "S" => 6,
                "D" => 4,
                "M" => 5,
                "O" => 4,
                "N" => 5,
                "T" => 3,
                "X" => 6,
                _ => throw new InvalidOperationException($"Unknown resolver baseline record kind '{fields[0]}'."),
            };
            if (fields.Length != expected)
                throw new InvalidOperationException($"Resolver baseline record has {fields.Length} fields, expected {expected}: {line}");
            rows.Add(new Row(fields[0], fields[1..]));
        }

        return rows;
    }

    // ── tests ─────────────────────────────────────────────────────────────────────────

    /// <summary>Writes the frozen baseline from the CURRENT consumers. Inert unless <c>POLY_REGEN_RESOLVER_BASELINE</c> is set.</summary>
    [Test]
    public async Task Regenerate_WritesBaselineFromCurrentConsumers() {
        if (Environment.GetEnvironmentVariable("POLY_REGEN_RESOLVER_BASELINE") is null)
            return;

        var lines = Generate();
        File.WriteAllLines(Path.Combine(GuideBlocks.RepoRoot(), BaselinePath), lines);
        await Assert.That(lines.Count).IsGreaterThan(1000);
    }

    [Test]
    public async Task Baseline_IsLargeAndCoversEveryFamilyAndSite() {
        var rows = ReadBaseline();
        var siteRows = rows.Where(r => r.Kind == "S").ToList();

        await Assert.That(siteRows.Count).IsGreaterThan(400);
        await Assert.That(rows.Count(r => r.Kind == "D")).IsEqualTo(GoldenDomains.Lowerable.Count);
        await Assert.That(rows.Count(r => r.Kind == "M")).IsGreaterThan(1000);
        await Assert.That(rows.Count(r => r.Kind == "O")).IsEqualTo(GoldenDomains.Lowerable.Count + SweepDocuments.Length);
        await Assert.That(rows.Count(r => r.Kind == "N")).IsGreaterThan(400);
        await Assert.That(rows.Count(r => r.Kind == "T")).IsGreaterThan(0);
        await Assert.That(siteRows.Count(r => r.Fields[4].StartsWith("ERR", StringComparison.Ordinal))).IsGreaterThan(150);
        await Assert.That(siteRows.Count(r => r.Fields[4].StartsWith("OK", StringComparison.Ordinal))).IsGreaterThan(100);

        // Every reader site has a row; every guarded site a kind-mismatch or string row unless no input can reach it.
        foreach (var (label, _, guarded, unreachable) in Sites) {
            var mine = siteRows.Where(r => r.Fields[0] == label).ToList();
            await Assert.That(mine).IsNotEmpty().Because(label);
            if (guarded && unreachable is null && !GuardAlwaysHolds.ContainsKey(label))
                await Assert.That(mine.Any(r => r.Fields[1].StartsWith("mis", StringComparison.Ordinal) || r.Fields[1].StartsWith("str", StringComparison.Ordinal)))
                    .IsTrue().Because(label);
        }

        await Assert.That(Sites.Length).IsEqualTo(114);
        await Assert.That(GuardAlwaysHolds.Keys.Except(Sites.Where(s => s.Guarded).Select(s => s.Label))).IsEmpty();
        await Assert.That(Sites.Select(s => s.Label).Distinct().Count()).IsEqualTo(Sites.Length);
        await Assert.That(siteRows.Select(r => r.Fields[0]).Except(Sites.Select(s => s.Label))).IsEmpty();
        await Assert.That(siteRows.Select(r => (r.Fields[0], r.Fields[1])).Distinct().Count()).IsEqualTo(siteRows.Count);
    }

    [Test]
    public async Task Cases_MatchTheFrozenKeys() {
        var frozen = ReadBaseline().Where(r => r.Kind == "S").Select(r => (r.Fields[0], r.Fields[1], r.Fields[2], Unescape(r.Fields[3]))).ToList();
        var current = Cases().Select(c => (c.Site, c.Case, c.Door, c.Input)).ToList();

        await Assert.That(current).IsEquivalentTo(frozen);
    }

    [Test]
    public async Task SiteRows_MatchFrozenBaseline() {
        var failures = new List<string>();
        foreach (var row in ReadBaseline().Where(r => r.Kind == "S")) {
            var actual = Run(row.Fields[2], Unescape(row.Fields[3]));
            var expected = Unescape(row.Fields[4]);
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
                failures.Add($"{row.Fields[0]}/{row.Fields[1]}\n    expected {expected}\n    actual   {actual}");
        }

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task CorpusAndOutline_MatchFrozenBaseline() {
        var rows = ReadBaseline();
        var expected = rows.Where(r => r.Kind is "D" or "M").Select(r => r.Kind + "\t" + string.Join('\t', r.Fields)).ToList();
        var actual = CorpusRecords().ToList();
        var outlineFailures = new List<string>();
        foreach (var row in rows.Where(r => r.Kind == "O")) {
            var result = OutlineRecord(Unescape(row.Fields[1]));
            if (!string.Equals(result, Unescape(row.Fields[2]), StringComparison.Ordinal))
                outlineFailures.Add($"{row.Fields[0]}\n    expected {Unescape(row.Fields[2])}\n    actual   {result}");
        }

        await Assert.That(actual.Except(expected)).IsEmpty();
        await Assert.That(expected.Except(actual)).IsEmpty();
        await Assert.That(actual.Count).IsEqualTo(expected.Count);
        await Assert.That(outlineFailures).IsEmpty();
    }

    /// <summary>
    /// The fail-closed proof for error recovery: no token substitution or truncation in the sweep documents
    /// makes a consumer throw from <c>Name()</c> or <c>Unescape()</c>, and the sweep's outcomes and exception
    /// types are the frozen ones.
    /// </summary>
    [Test]
    public async Task MutationSweep_MatchesFrozenBaselineAndNeverReadsAWrongKind() {
        var rows = ReadBaseline();
        var exceptionTypes = new Dictionary<string, int>(StringComparer.Ordinal);
        var actual = SweepRecords(exceptionTypes).ToList();
        var expected = rows.Where(r => r.Kind is "N" or "X").Select(r => r.Kind + "\t" + string.Join('\t', r.Fields)).ToList();
        var wrongKind = new List<string>();
        foreach (var (name, text) in SweepDocuments) {
            var tokens = DslSyntaxTree.Parse(text).Tokens;
            for (var i = 0; i < tokens.Count - 1; i++) {
                foreach (var (_, _, outcome) in SweepOutcomes(text, tokens[i])) {
                    if (outcome.Contains("Name() reads", StringComparison.Ordinal) || outcome.Contains("Unescape() reads", StringComparison.Ordinal))
                        wrongKind.Add($"{name}#{i}: {outcome}");
                }
            }
        }

        await Assert.That(wrongKind).IsEmpty();
        await Assert.That(actual).IsEquivalentTo(expected);
        await Assert.That(exceptionTypes.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"T\t{p.Key}\t{p.Value}"))
            .IsEquivalentTo(rows.Where(r => r.Kind == "T").Select(r => "T\t" + string.Join('\t', r.Fields)));
    }
}