using Poly.Grammar;

namespace Poly.DomainModeling.Parsing;

/// <summary>
/// Product Poly DSL as a <see cref="Grammar{TKind}"/> pattern table.
/// Structure dispatch (top / entity-body / stage-body / annotation), expression
/// span + op rules (gpure), and effect <b>head</b> patterns live here. Handlers
/// in <see cref="PolyDslParser"/> / <see cref="DslExpressionParser"/> fold
/// matches and tails into IR (Option A ladder for expr; effect bodies via
/// nested <c>MatchRule("effect")</c> loops — B1). Residual non-table pieces:
/// quantifiers / path-prefix after ident primary, action parameter lists /
/// require gates / arrow returns, printer (table-parity deferred).
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

    /// <summary>
    /// Builds the DSL grammar. The annotation rule models the generic
    /// parenthesized-call shape; pack keywords are gated by the handler via
    /// <see cref="AnnotationRegistry.CanAccept"/> (grammar elements match kinds,
    /// and every annotation keyword tokenizes as <see cref="DslTokenKind.Identifier"/>).
    /// </summary>
    /// <param name="configure">
    /// Optional pack hook (GI-5): register extra annotation/shape patterns after
    /// the product table is built. Generic <c>column(...)</c> needs no extra patterns.
    /// </param>
    public static Grammar<DslTokenKind> Build(Action<Grammar<DslTokenKind>>? configure = null) {
        var g = new Grammar<DslTokenKind>();

        // ── top: domain body declarations ──────────────────────
        g.Define("top")
            .Pattern("enum")
                .Value(DslTokenKind.Identifier).Token(DslTokenKind.Colon).Token(DslTokenKind.Enum)
                .Commit()
            .Pattern("entity")
                .Value(DslTokenKind.Identifier).Token(DslTokenKind.Colon).Token(DslTokenKind.Entity)
                .Commit();

        // ── entity-body: members of an entity block ────────────
        g.Define("entity-body")
            .Pattern("entity-subscription")
                .Token(DslTokenKind.When).Value(DslTokenKind.Identifier)
                .Commit()
            .Pattern("stage")
                .Value(DslTokenKind.Identifier).Token(DslTokenKind.Colon).Token(DslTokenKind.Stage)
                .Commit()
            .Pattern("action")
                .Value(DslTokenKind.Identifier).Token(DslTokenKind.Colon).Token(DslTokenKind.Action)
                .Commit()
            .Pattern("policy")
                .Value(DslTokenKind.Identifier).Token(DslTokenKind.Colon).Token(DslTokenKind.Policy)
                .Commit()
            .Pattern("legacy-action")
                .Value(DslTokenKind.Identifier).Token(DslTokenKind.LParen)
                .Commit()
            .Pattern("typed-line")
                // enum-typed property or bare (N1) navigation — handler resolves
                // via known enum names, mirroring IsNavLine semantics.
                .Value(DslTokenKind.Identifier).Token(DslTokenKind.Colon).Value(DslTokenKind.Identifier)
                .Commit()
            .Pattern("property")
                .Value(DslTokenKind.Identifier).Token(DslTokenKind.Colon).Predicate(IsPrimitiveTypeKind, "primitive-type")
                .Commit()
            .Pattern("nav-many")
                .Value(DslTokenKind.Identifier).Token(DslTokenKind.Colon).Token(DslTokenKind.Many)
                .Commit()
            .Pattern("nav-one")
                .Value(DslTokenKind.Identifier).Token(DslTokenKind.Colon).Token(DslTokenKind.One)
                .Commit()
            .Pattern("nav-owned")
                .Value(DslTokenKind.Identifier).Token(DslTokenKind.Colon).Token(DslTokenKind.Owned)
                .Commit()
            .Pattern("primitive-name")
                // Primitive keyword used as a property name: "Number: Text".
                .Predicate(IsPrimitiveTypeKind, "primitive-type").Token(DslTokenKind.Colon)
                .Commit();

        // ── stage-body: members of a stage block ───────────────
        g.Define("stage-body")
            .Pattern("entry")
                .Token(DslTokenKind.Entry)
                .Commit()
            .Pattern("exit")
                .Token(DslTokenKind.Exit)
                .Commit()
            .Pattern("subscription")
                .Token(DslTokenKind.When).Value(DslTokenKind.Identifier)
                .Commit()
            .Pattern("stage-action")
                .Value(DslTokenKind.Identifier)
                .Commit();

        // ── annotation: parenthesized keyword calls ────────────
        // Recognition surface only; argument validity (types, commas, trailing
        // comma, registered keyword) is enforced by the handler (fail-closed).
        // Pack keywords all tokenize as Identifier, so keyword identity is
        // resolved by the handler via AnnotationRegistry.CanAccept — the grammar
        // cannot distinguish 'column' from 'table' by kind.
        g.Define("annotation")
            .Pattern("no-args")
                .Value(DslTokenKind.Identifier).Token(DslTokenKind.LParen).Token(DslTokenKind.RParen)
                .Commit()
            .Pattern("with-args")
                .Value(DslTokenKind.Identifier).Token(DslTokenKind.LParen).Many("annotation-args").Token(DslTokenKind.RParen)
                .Commit();

        // One argument per match; Optional(Comma) models separators between args.
        g.Define("annotation-args")
            .Pattern("str").Optional(DslTokenKind.Comma).Token(DslTokenKind.StringLiteral).Commit()
            .Pattern("num").Optional(DslTokenKind.Comma).Token(DslTokenKind.Number).Commit()
            .Pattern("tru").Optional(DslTokenKind.Comma).Token(DslTokenKind.True).Commit()
            .Pattern("fal").Optional(DslTokenKind.Comma).Token(DslTokenKind.False).Commit()
            .Pattern("nul").Optional(DslTokenKind.Comma).Token(DslTokenKind.Null).Commit();

        // GI-5 extension: packs that declare argument shapes beyond the
        // generic quoted-literal form register additional patterns on the
        // "annotation" rule via <paramref name="configure"/> / AnnotationRegistry.ContributePatterns
        // (see DslGrammarTests Annotation_CustomShapeExtension_RegistersPattern).
        // ParseAnnotation remains the strict validator (fail-closed).

        // E1: product expression precedence layers (gpure-3). Mirrors
        // DslExpressionParser: or / and / not / compare / add / mul / primary.
        // 'not' operand is the ADD layer (B3 pin — product ParseNot binds at
        // ParseAdd, not comparison). Comparison is a single optional second
        // half, not a chain. Path-prefix / quantifiers stay handler-side after
        // the ident primary (inventory §A1).
        g.Define("expr")
            .Pattern("top").Rule("expr-or").Commit();

        g.Define("expr-or")
            .Pattern("chain").LeftAssoc("expr-and", DslTokenKind.Or).Commit();

        g.Define("expr-and")
            .Pattern("chain").LeftAssoc("expr-not", DslTokenKind.And).Commit();

        g.Define("expr-not")
            .Pattern("not").Token(DslTokenKind.Not).Rule("expr-add").Commit()
            // B3: comparison LHS uses the no-not operand layer, so pass-through
            // can never shadow 'not'-led input (`not a > b` must leave `> b`
            // unconsumed, like product ParseNot; not a comparison of (not a)).
            .Pattern("pass-through").Rule("expr-compare").Commit();

        g.Define("expr-compare")
            .Pattern("bare").Rule("expr-add-no-not").Commit()
            .Pattern("with-op")
                .Rule("expr-add-no-not")
                .Predicate(IsCompareOpKind, "compare-op")
                .Rule("expr-add").Commit();

        // Comparison LHS chain is no-not END TO END (not just first-token):
        // product `not` re-enters only via primary-Not/group, so this chain
        // also rejects `a + not b` / `a + not b > c` on the SPAN side even
        // though the live fold accepts them (S1 pin in DslExprParityTests;
        // tracking note in gpure-inventory-notes §A1 — reconcile when the span
        // tables gain a live consumer). `(not a) > b` / `x > not y` still work
        // (the not sits inside a group / on the RHS, which uses the full expr).
        g.Define("expr-add-no-not")
            .Pattern("chain").LeftAssoc("expr-mul-no-not", DslTokenKind.Plus, DslTokenKind.Minus).Commit();

        g.Define("expr-mul-no-not")
            .Pattern("chain").LeftAssoc("expr-primary-no-not", DslTokenKind.Star, DslTokenKind.Slash).Commit();

        g.Define("expr-primary-no-not")
            .Pattern("number").Token(DslTokenKind.Number).Commit()
            .Pattern("string").Token(DslTokenKind.StringLiteral).Commit()
            .Pattern("true").Token(DslTokenKind.True).Commit()
            .Pattern("false").Token(DslTokenKind.False).Commit()
            .Pattern("null").Token(DslTokenKind.Null).Commit()
            .Pattern("group").Token(DslTokenKind.LParen).Rule("expr").Token(DslTokenKind.RParen).Commit()
            .Pattern("ident").Value(DslTokenKind.Identifier).Commit();

        g.Define("expr-add")
            .Pattern("chain").LeftAssoc("expr-mul", DslTokenKind.Plus, DslTokenKind.Minus).Commit();

        g.Define("expr-mul")
            .Pattern("chain").LeftAssoc("expr-primary", DslTokenKind.Star, DslTokenKind.Slash).Commit();

        // Operator rules: the live parser (gpure-4) folds chains via these
        // table matches instead of raw kind while-loops.
        g.Define("expr-or-op")
            .Pattern("or").Token(DslTokenKind.Or).Commit();

        g.Define("expr-and-op")
            .Pattern("and").Token(DslTokenKind.And).Commit();

        g.Define("expr-not-op")
            .Pattern("not").Token(DslTokenKind.Not).Commit();

        g.Define("expr-add-op")
            .Pattern("plus").Token(DslTokenKind.Plus).Commit()
            .Pattern("minus").Token(DslTokenKind.Minus).Commit();

        g.Define("expr-mul-op")
            .Pattern("star").Token(DslTokenKind.Star).Commit()
            .Pattern("slash").Token(DslTokenKind.Slash).Commit();

        g.Define("expr-compare-op")
            .Pattern("op").Predicate(IsCompareOpKind, "compare-op").Commit();

        // ── effect: head-only statement patterns (gpure-5) ────
        // B1: patterns match the statement HEAD (keyword + fixed syntax through
        // the condition/target header), never the body blocks — bodies are
        // consumed by handler loops over MatchRule("effect"). `when` is NOT a
        // pattern: it must stay rejected inside effect bodies (F7).
        g.Define("effect")
            .Pattern("transition")
                .Token(DslTokenKind.Transition).Token(DslTokenKind.To).Value(DslTokenKind.Identifier).Commit()
            .Pattern("assign")
                .Token(DslTokenKind.Assign).Value(DslTokenKind.Identifier).Token(DslTokenKind.To).Commit()
            .Pattern("create-in")
                .Token(DslTokenKind.Create).Token(DslTokenKind.In).Value(DslTokenKind.Identifier).Commit()
            .Pattern("create")
                .Token(DslTokenKind.Create).Value(DslTokenKind.Identifier).Commit()
            .Pattern("delete")
                .Token(DslTokenKind.Delete).Commit()
            .Pattern("invoke")
                .Token(DslTokenKind.Invoke).Commit()
            .Pattern("if")
                .Token(DslTokenKind.If).Token(DslTokenKind.LParen).Commit();

        // E1: primary first-token surface (precedence stays in DslExpressionParser layers).
        // Packs add open forms via ExpressionFormRegistry + configure (e.g. unit duration).
        g.Define("expr-primary")
            .Pattern("number").Token(DslTokenKind.Number).Commit()
            .Pattern("string").Token(DslTokenKind.StringLiteral).Commit()
            .Pattern("true").Token(DslTokenKind.True).Commit()
            .Pattern("false").Token(DslTokenKind.False).Commit()
            .Pattern("null").Token(DslTokenKind.Null).Commit()
            .Pattern("group").Token(DslTokenKind.LParen).Rule("expr").Token(DslTokenKind.RParen).Commit()
            .Pattern("not").Token(DslTokenKind.Not).Rule("expr-add").Commit()
            .Pattern("ident").Value(DslTokenKind.Identifier).Commit();

        configure?.Invoke(g);
        return g;
    }
}