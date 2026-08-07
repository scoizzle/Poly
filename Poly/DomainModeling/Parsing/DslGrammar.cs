using Poly.Grammar;

namespace Poly.DomainModeling.Parsing;

/// <summary>
/// GI-2: the Phase 1a Poly DSL described as a <see cref="Grammar{TKind}"/>
/// pattern table. The DSL scanner (<see cref="DslTokenReader"/>) feeds the
/// same token stream the parser consumed interactively; the parser (GI-3)
/// uses this table for matcher-driven construct dispatch and the printer
/// (GI-5) uses it as its structural skeleton.
///
/// Element-set gaps (documented per plan rule): expression sub-grammar
/// (binary operators, quantifiers, path-prefix), effect bodies, action
/// parameter lists / require gates / arrow returns are <b>not</b> modeled as
/// patterns — they are parsed by the interactive handler methods
/// (recursive-descent fallback). The table describes the structural envelope;
/// the handlers remain the precise validators (fail-closed).
/// </summary>
public static class DslGrammar {
    /// <summary>True when <paramref name="kind"/> is a primitive property type keyword.</summary>
    public static bool IsPrimitiveTypeKind(DslTokenKind kind) => kind is
        DslTokenKind.Text or DslTokenKind.NumberType or DslTokenKind.BooleanType
        or DslTokenKind.DateTimeType or DslTokenKind.DateType;

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

        // GI-4 expression gap (intentional hybrid): no binary/quantifier/path patterns —
        // PolyDslParser RD owns expression bodies until E1 (temporal pack admit).

        configure?.Invoke(g);
        return g;
    }
}