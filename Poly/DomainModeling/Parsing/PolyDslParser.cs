using System.Globalization;

using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Effects;
using Poly.DomainModeling.Evolution;
using Poly.Grammar;

using Token = Poly.Grammar.Token<Poly.DomainModeling.Parsing.DslTokenKind>;
// GI-3: this file is ported onto the grammar engine. The legacy names are
// aliased so the parse logic reads unchanged; GI-7 removes the legacy
// enum/struct and these aliases.
using TokenKind = Poly.DomainModeling.Parsing.DslTokenKind;

namespace Poly.DomainModeling.Parsing;

/// <summary>
/// Grammar-table-driven parser for the product Poly DSL.
/// Structure/annotations: Matcher over <see cref="DslGrammar"/>.
/// Expressions: <see cref="DslExpressionParser"/> (E1 — open forms via
/// <see cref="ExpressionFormRegistry"/>; precedence Pratt/RD layers).
/// </summary>
public sealed class PolyDslParser : IDslParseCursor {
    private readonly Grammar<DslTokenKind> _grammar;
    private readonly DslTokenReader _tokenReader;
    private readonly Matcher<DslTokenKind> _matcher;
    private readonly DslExpressionParser _expressions;
    private Token _current;
    private string _domainName = "";
    private int _entityIndex;
    private bool _primitivesAdded;

    // Accumulated changes for the current entity
    private string _currentEntityName = "";
    private readonly Dictionary<string, DomainExpression> _entityPolicies = new(StringComparer.Ordinal);

    // Pending requires that must be resolved after the full entity body is parsed
    private readonly List<PendingRequire> _pendingRequires = new();

    // Pending navigation properties (N1 form) resolved after all entities are known
    private readonly List<PendingNav> _pendingNavs = new();

    // Entity names collected during parsing, for nav target resolution
    private readonly HashSet<string> _entityNames = new(StringComparer.Ordinal);

    // Enum type names, for distinguishing typed properties from nav lines
    private readonly HashSet<string> _enumTypeNames = new(StringComparer.Ordinal);

    // Property names per entity, for collision detection with navs
    private readonly Dictionary<string, HashSet<string>> _entityPropertyNames = new(StringComparer.Ordinal);

    // Relationship names from N1 nav lines, for duplicate detection
    private readonly HashSet<string> _relationshipNames = new(StringComparer.Ordinal);

    // Q1′′′.5 / Q1'''''.2: Prevents recursive `Rel where ...` parsing inside a where body.
    private bool _inWhereBody;

    // Explicit parse inputs / annotation support
    private readonly DomainParserInputs? _parserInputs;

    private readonly record struct PendingRequire(
        string ActionName,
        string? StageName,
        string PolicyName,
        bool Negated);

    private readonly record struct PendingNav(
        string SourceEntityName,
        string PropertyName,
        string TargetTypeName,
        RelationshipCardinality Cardinality,
        bool SourceOwnsTarget);

    public PolyDslParser(string text) : this(text, parserInputs: null) {
    }

    /// <summary>
    /// Creates a parser with optional parser inputs for pack-aware parsing.
    /// When a context is provided, its registered <see cref="IAnnotationSyntax"/> handlers
    /// are consulted for property-tail and entity-header annotations; pack grammar
    /// contributors are applied when building the session pattern table (GI-5).
    /// </summary>
    public PolyDslParser(string text, DomainParserInputs? parserInputs) {
        _parserInputs = parserInputs;
        _grammar = DslGrammar.Build(g => {
            parserInputs?.Annotations.ContributePatterns(g);
            parserInputs?.ExpressionForms.ContributeGrammarPatterns(g);
        });
        _tokenReader = new DslTokenReader(text);
        _matcher = new Matcher<DslTokenKind>(_grammar, _tokenReader);
        _current = _tokenReader.Read();
        _expressions = new DslExpressionParser(this, parserInputs?.ExpressionForms);
    }

    Token IDslParseCursor.Current => _current;
    void IDslParseCursor.Advance() => Advance();
    Token IDslParseCursor.Expect(DslTokenKind kind) => Expect(kind);
    string IDslParseCursor.ExpectIdentifier(DslTokenKind kind, string context) => ExpectIdentifier(kind, context);
    bool IDslParseCursor.PeekIs(DslTokenKind kind) => PeekIs(kind);
    Token IDslParseCursor.Peek(int n) => _tokenReader.Peek(n);
    MatchResult<DslTokenKind>? IDslParseCursor.MatchRule(string ruleName) => MatchRule(ruleName);
    Exception IDslParseCursor.Error(string message) => Error(message);
    bool IDslParseCursor.InWhereBody {
        get => _inWhereBody;
        set => _inWhereBody = value;
    }

    private DomainExpression ParseExpression() => _expressions.ParseExpression();

    /// <summary>
    /// Parses the complete .poly text and returns a list of <see cref="DomainChange"/>
    /// that, when applied via evolution, produces the declared domain.
    /// </summary>
    public List<DomainChange> Parse() {
        var changes = new List<DomainChange>();

        // ── Domain header ─────────────────────────────────────
        Expect(TokenKind.Domain);
        _domainName = ExpectIdentifier(TokenKind.Identifier, "domain name");
        changes.Add(new SetDomainNameChange(_domainName));

        // ── Enum type definitions + entity definitions ─────────
        // Parse entities and enum types in order; enum types must precede entities
        // that reference them for property type resolution.
        // Dispatch is matcher-driven over the "top" rule; unmodeled shapes get
        // the same targeted diagnostics ParseEntity would produce.
        while (_current.Kind == TokenKind.Identifier) {
            switch (MatchRule("top")?.PatternName) {
                case "enum":
                    ParseEnumType(changes);
                    break;
                case "entity":
                    ParseEntity(changes);
                    break;
                default:
                    throw TopLevelRejection();
            }
        }

        // ── Resolve N1 navigation properties ───────────────────
        ResolvePendingNavs(changes);

        if (_current.Kind == TokenKind.Relationship)
            throw N2RelationshipNotSupported();

        Expect(TokenKind.EndOfFile);
        return changes;
    }

    private void EnsurePrimitivesOnce(List<DomainChange> changes) {
        if (_primitivesAdded) return;
        _primitivesAdded = true;
        foreach (var p in new[] { ("Text", TypeCategory.Text), ("Number", TypeCategory.Integer),
            ("Boolean", TypeCategory.Boolean), ("DateTime", TypeCategory.DateTime),
            ("Date", TypeCategory.Primitive | TypeCategory.Temporal) }) {
            changes.Add(new AddPrimitiveTypeChange(p.Item1, p.Item2, []));
        }
    }

    private void ParseEntity(List<DomainChange> changes) {
        var entityName = ExpectIdentifier(TokenKind.Identifier, "entity name");
        _currentEntityName = entityName;
        _entityPolicies.Clear();
        _pendingRequires.Clear();
        _entityIndex++;
        Expect(TokenKind.Colon);

        // The "top" rule guarantees this is 'entity'; unsupported-keyword and
        // removed-inheritance shapes never reach this method (TopLevelRejection).
        Expect(TokenKind.Entity);

        _entityNames.Add(entityName);
        changes.Add(new AddEntityChange(entityName, []));

        // ── Entity header facets (pack-registered annotations) ──
        while (TryParseRegisteredAnnotation(out var headerFacet)) {
            changes.Add(new AddFacetToDomainTypeChange(entityName, headerFacet));
        }
        if (LooksLikeAnnotationCall()) {
            throw Error(
                $"Unknown or unregistered annotation '{_current.Text}'. " +
                "Enable a pack that registers this keyword, or remove the annotation.");
        }

        Expect(TokenKind.LBrace);

        EnsurePrimitivesOnce(changes);

        while (_current.Kind != TokenKind.RBrace) {
            if (_current.Kind == TokenKind.Relationship) {
                throw N2RelationshipNotSupported();
            }

            var match = MatchRule("entity-body");
            if (match is not null) {
                switch (match.PatternName) {
                    case "entity-subscription":
                        // Entity-level subscription: when RelName TargetStage { effects }
                        ParseEntitySubscription(changes);
                        continue;

                    case "stage": {
                            var name = ExpectIdentifier(TokenKind.Identifier, "stage name");
                            Expect(TokenKind.Colon);
                            ParseStage(name, changes);
                            continue;
                        }

                    case "action": {
                            var name = ExpectIdentifier(TokenKind.Identifier, "action name");
                            Expect(TokenKind.Colon);
                            ParseStandaloneAction(name, changes);
                            continue;
                        }

                    case "policy": {
                            var name = ExpectIdentifier(TokenKind.Identifier, "policy name");
                            Expect(TokenKind.Colon);
                            ParsePolicy(name, changes);
                            continue;
                        }

                    case "legacy-action": {
                            // Legacy: Name(params): action { … }
                            var name = ExpectIdentifier(TokenKind.Identifier, "action name");
                            var actionParams = ParseActionParameterList();
                            Expect(TokenKind.Colon);
                            if (_current.Kind is not (TokenKind.Action or TokenKind.LBrace or TokenKind.When or TokenKind.Require)) {
                                throw Error($"Expected action after '{name}(...)', got '{_current.Text}'");
                            }
                            ParseActionBody(name, changes, stageName: null, actionParams);
                            continue;
                        }

                    case "typed-line": {
                            // Enum-typed property or bare (N1) navigation — resolved by
                            // known enum names, mirroring the legacy IsNavLine dispatch.
                            var name = ExpectIdentifier(TokenKind.Identifier, "property name");
                            Expect(TokenKind.Colon);
                            if (_enumTypeNames.Contains(_current.Text)) {
                                // Typed property referencing an enum type, with optional constraints/facets
                                var typeName = ExpectIdentifier(TokenKind.Identifier, "enum type name");
                                TrackPropertyName(_currentEntityName, name);
                                changes.Add(new AddPropertyToEntityChange(_currentEntityName,
                                    new Property(name, new DomainTypeReference(typeName), [])));
                                ParsePropertyTail(name, changes);
                            }
                            else if (IsNavLine()) {
                                ParseNavLine(name);
                            }
                            else {
                                CheckUnsupportedKeyword(name, _current.Text);
                                throw Error($"Expected type, stage, action, policy, or navigation property after '{name}:'");
                            }
                            continue;
                        }

                    case "property": {
                            var name = ExpectIdentifier(TokenKind.Identifier, "property name");
                            Expect(TokenKind.Colon);
                            ParseProperty(name, _current.Kind, changes);
                            continue;
                        }

                    case "nav-many":
                    case "nav-one":
                    case "nav-owned": {
                            var name = ExpectIdentifier(TokenKind.Identifier, "navigation property name");
                            Expect(TokenKind.Colon);
                            ParseNavLine(name);
                            continue;
                        }

                    case "primitive-name": {
                            // Primitive keyword used as property name (e.g. "Number: Text")
                            var name = _current.Text;
                            Advance(); // consume type keyword (e.g. 'Number')
                            Expect(TokenKind.Colon);
                            if (IsPrimitiveType(_current.Kind)) {
                                ParseProperty(name, _current.Kind, changes);
                            }
                            else {
                                throw Error($"Expected type after '{name}:', got '{_current.Text}'");
                            }
                            continue;
                        }
                }
            }

            // Fallback: legacy action forms not modeled by the grammar table —
            // 'Name: { … }', 'Name: when …', 'Name: require …' (the printer no
            // longer emits these; kept for backward parse compatibility).
            if (_current.Kind == TokenKind.Identifier && PeekIs(TokenKind.Colon)) {
                var name = _current.Text;
                Advance(); // consume identifier
                Expect(TokenKind.Colon);
                if (_current.Kind is TokenKind.LBrace or TokenKind.When or TokenKind.Require) {
                    ParseStandaloneAction(name, changes);
                }
                else {
                    CheckUnsupportedKeyword(name, _current.Text);
                    throw Error($"Expected type, stage, action, policy, or navigation property after '{name}:'");
                }
                continue;
            }

            throw Error($"Expected property, stage, action, or policy, got '{_current.Text}'");
        }

        Expect(TokenKind.RBrace);

        // ── Resolve pending requires (now all policies for this entity are known) ──
        ResolvePendingRequires(changes);
    }

    /// <summary>
    /// Resolves all collected require references against known entity policies.
    /// Errors on missing policies — no silent Literal(true) fallback.
    /// </summary>
    private void ResolvePendingRequires(List<DomainChange> changes) {
        foreach (var pr in _pendingRequires) {
            if (pr.Negated) {
                // require not PolicyName
                if (!_entityPolicies.TryGetValue(pr.PolicyName, out var expr)) {
                    throw Error($"Action '{pr.ActionName}' requires policy '{pr.PolicyName}' " +
                        $"which is not defined on entity '{_currentEntityName}'.");
                }
                var policyName = $"not_{pr.PolicyName}";
                changes.Add(new AddPolicyToActionChange(_currentEntityName, pr.ActionName,
                    new Policy(policyName, DomainExpression.Not(expr))));
            }
            else {
                // require PolicyName
                if (!_entityPolicies.TryGetValue(pr.PolicyName, out var expr)) {
                    throw Error($"Action '{pr.ActionName}' requires policy '{pr.PolicyName}' " +
                        $"which is not defined on entity '{_currentEntityName}'.");
                }
                changes.Add(new AddPolicyToActionChange(_currentEntityName, pr.ActionName,
                    new Policy(pr.PolicyName, expr)));
            }
        }
    }

    private void ParseProperty(string name, TokenKind typeKind, List<DomainChange> changes) {
        Advance(); // consume type

        TrackPropertyName(_currentEntityName, name);

        var typeName = typeKind switch {
            TokenKind.Text => "Text",
            TokenKind.NumberType => "Number",
            TokenKind.BooleanType => "Boolean",
            TokenKind.DateTimeType => "DateTime",
            TokenKind.DateType => "Date",
            _ => throw Error($"Unknown type '{typeKind}'"),
        };

        changes.Add(new AddPropertyToEntityChange(_currentEntityName,
            new Property(name, new DomainTypeReference(typeName), [])));

        ParsePropertyTail(name, changes);
    }

    private void TrackPropertyName(string entityName, string propertyName) {
        if (!_entityPropertyNames.TryGetValue(entityName, out var props)) {
            props = new HashSet<string>(StringComparer.Ordinal);
            _entityPropertyNames[entityName] = props;
        }
        props.Add(propertyName);
    }

    /// <summary>
    /// Parses optional constraints then pack annotations on a property tail.
    /// Constraints and annotations may interleave in any order.
    /// Registered annotations are consumed here. Unregistered annotation-shaped
    /// <c>keyword(literal…)</c> forms fail closed. Legacy <c>Name(params): action</c>
    /// (identifier args / trailing <c>:</c>) is left for the entity body loop.
    /// </summary>
    private void ParsePropertyTail(string propertyName, List<DomainChange> changes) {
        while (IsConstraint(_current.Kind)
               || (_current.Kind == TokenKind.Identifier && PeekIs(TokenKind.LParen))) {
            if (IsConstraint(_current.Kind)) {
                var constraint = ParseConstraint();
                if (constraint is not null) {
                    changes.Add(new AddConstraintToPropertyChange(
                        _currentEntityName, propertyName, constraint));
                }
                continue;
            }

            // Annotation-shaped identifier(…) — grammar match or fail-closed RD (GI-5).
            if (TryParseRegisteredAnnotation(out var facet)) {
                changes.Add(new AddFacetToPropertyChange(
                    _currentEntityName, propertyName, facet));
                continue;
            }

            // Fail closed for annotation-shaped args (literals / empty / trailing comma),
            // not legacy action heads like Checkout(days: Number): action { … }.
            if (LooksLikeAnnotationCall()) {
                throw Error(
                    $"Unknown or unregistered annotation '{_current.Text}'. " +
                    "Enable a pack that registers this keyword, or remove the annotation.");
            }

            break;
        }
    }

    /// <summary>
    /// Pack-registered annotation: Matcher recognizes valid shapes; invalid shapes
    /// (e.g. trailing comma) still enter <see cref="ParseAnnotation"/> for fail-closed
    /// diagnostics when the keyword is registered and the call is annotation-shaped.
    /// </summary>
    private bool TryParseRegisteredAnnotation(out Facet facet) {
        facet = null!;
        if (_current.Kind != TokenKind.Identifier)
            return false;

        var keyword = _current.Text;
        if (_parserInputs?.Annotations.CanAccept(keyword) != true)
            return false;

        // Valid grammar shape or annotation-shaped call (including trailing comma).
        if (MatchRule("annotation") is null && !LooksLikeAnnotationCall())
            return false;

        Advance();
        facet = ParseAnnotation(keyword);
        return true;
    }

    /// <summary>
    /// True when the current identifier is followed by <c>(</c> and an annotation
    /// argument list (literals / empty), not a legacy action parameter list.
    /// </summary>
    private bool LooksLikeAnnotationCall() {
        if (_current.Kind != TokenKind.Identifier || _tokenReader.Peek(1).Kind != TokenKind.LParen)
            return false;

        var firstArg = _tokenReader.Peek(2).Kind;
        return firstArg is TokenKind.StringLiteral
            or TokenKind.Number
            or TokenKind.True
            or TokenKind.False
            or TokenKind.Null
            or TokenKind.RParen;
    }

    private void ParseStage(string name, List<DomainChange> changes) {
        Advance(); // consume 'stage'

        // P2′′′′.3: Clear error if someone tries the removed 'prev' keyword
        if (_current.Kind == TokenKind.Identifier &&
            string.Equals(_current.Text, "prev", StringComparison.Ordinal)) {
            throw Error("'prev' is no longer supported. Stage hierarchy has been removed; all stages are flat.");
        }

        changes.Add(new AddStageChange(_currentEntityName, name));
        Expect(TokenKind.LBrace);

        // P2.4: Parse entry/exit effect blocks before actions and subscriptions
        bool parsedEntry = false;
        bool parsedExit = false;

        while (_current.Kind != TokenKind.RBrace) {
            var match = MatchRule("stage-body");
            if (match is not null) {
                switch (match.PatternName) {
                    case "entry":
                        if (parsedEntry)
                            throw Error($"'{_current.Text}' must appear at the beginning of the stage block, before actions and subscriptions.");
                        parsedEntry = true;
                        Advance(); // consume 'entry'
                        Expect(TokenKind.LBrace);
                        while (_current.Kind != TokenKind.RBrace) {
                            var effect = ParseEffect();
                            changes.Add(new AddOnEntryEffectToStageChange(_currentEntityName, name, effect));
                        }
                        Expect(TokenKind.RBrace);
                        continue;

                    case "exit":
                        if (parsedExit)
                            throw Error($"'{_current.Text}' must appear at the beginning of the stage block, before actions and subscriptions.");
                        parsedExit = true;
                        Advance(); // consume 'exit'
                        Expect(TokenKind.LBrace);
                        while (_current.Kind != TokenKind.RBrace) {
                            var effect = ParseEffect();
                            changes.Add(new AddOnExitEffectToStageChange(_currentEntityName, name, effect));
                        }
                        Expect(TokenKind.RBrace);
                        continue;

                    case "subscription":
                        // Subscription: when RelName TargetStage { ... }
                        ParseSubscription(name, changes);
                        continue;
                }
            }

            // Stage-local action (or unmodeled token — fail closed).
            if ((_current.Kind == TokenKind.Entry || _current.Kind == TokenKind.Exit) && _current.Kind != TokenKind.Identifier) {
                throw Error($"'{_current.Text}' must appear at the beginning of the stage block, before actions and subscriptions.");
            }
            var actionName = ExpectIdentifier(TokenKind.Identifier, "action name");
            // Stage members also use Name: kind. Legacy Name(params): action accepted.
            List<(string Name, string TypeName)>? stageActionParams = null;
            if (_current.Kind == TokenKind.LParen)
                stageActionParams = ParseActionParameterList();
            Expect(TokenKind.Colon);
            ParseActionBody(actionName, changes, name, stageActionParams);
        }

        Expect(TokenKind.RBrace);
    }

    private void ParseStandaloneAction(string name, List<DomainChange> changes) {
        // standalone action declared at entity level
        if (_current.Kind == TokenKind.Action)
            Advance(); // consume optional 'action'

        ParseActionBody(name, changes, stageName: null, preParsedParams: null);
    }

    /// <summary>
    /// Parses <c>(name: Type, ...)</c>. Canonical placement is after the kind:
    /// <c>Name: action (params)</c>. Also used by legacy <c>Name(params): action</c>.
    /// </summary>
    private List<(string Name, string TypeName)> ParseActionParameterList() {
        Expect(TokenKind.LParen);
        var list = new List<(string, string)>();
        while (_current.Kind != TokenKind.RParen) {
            var paramName = ExpectIdentifier(TokenKind.Identifier, "parameter name");
            Expect(TokenKind.Colon);
            var paramType = ParseTypeName();
            list.Add((paramName, paramType));
            if (_current.Kind == TokenKind.Comma)
                Advance();
        }
        Expect(TokenKind.RParen);
        return list;
    }

    private void ParseActionBody(
        string actionName,
        List<DomainChange> changes,
        string? stageName,
        List<(string Name, string TypeName)>? preParsedParams) {
        // Optional 'action' keyword
        if (_current.Kind == TokenKind.Action)
            Advance();

        // Canonical: Name: action (params) -> RetType [require …] { … }
        // Params immediately after the kind keep Name: kind uniform.
        var paramList = preParsedParams;
        if (paramList is null && _current.Kind == TokenKind.LParen)
            paramList = ParseActionParameterList();

        // Optional return type: -> TypeName
        InvocationResult? actionResult = null;
        if (_current.Kind == TokenKind.Arrow) {
            Advance(); // consume ->
            var returnTypeName = ParseTypeName();
            actionResult = new InvocationResult([
                new InvocationResult.Member("Result",
                    new DomainTypeReference(returnTypeName), [])
            ]);
        }

        // Stage gates and require policies (collected — resolved after entity body)
        while (_current.Kind == TokenKind.When || _current.Kind == TokenKind.Require) {
            if (_current.Kind == TokenKind.When) {
                Advance(); // consume 'when'
                // Stage gates are not runtime-enforced in Phase 1a (BR.3.2).
                // Silently consume the gate names so the parser advances past them.
                ParseIdentifierList();
            }
            else {
                Advance(); // consume 'require'
                bool negated = false;
                if (_current.Kind == TokenKind.Not) {
                    negated = true;
                    Advance();
                }
                var policies = ParseIdentifierList();
                foreach (var p in policies) {
                    _pendingRequires.Add(new PendingRequire(actionName, stageName, p, negated));
                }
            }
        }

        // Create the action BEFORE parameters or effects
        // (AddParameterToActionChange references the action by name, so it must exist first)
        if (stageName is not null) {
            changes.Add(new AddActionToStageChange(_currentEntityName, stageName, actionName));
        }
        else {
            changes.Add(new AddActionChange(_currentEntityName, actionName));
        }

        if (paramList is not null) {
            foreach (var (paramName, paramType) in paramList) {
                changes.Add(new AddParameterToActionChange(
                    _currentEntityName, actionName,
                    new Property(paramName, new DomainTypeReference(paramType), [])));
            }
        }

        if (actionResult is not null) {
            changes.Add(new SetActionResultChange(
                _currentEntityName, actionName, actionResult));
        }

        Expect(TokenKind.LBrace);

        // Parse effects
        var effects = new List<Effect>();
        while (_current.Kind != TokenKind.RBrace) {
            effects.Add(ParseEffect());
        }

        foreach (var e in effects) {
            changes.Add(new AddEffectToActionChange(_currentEntityName, actionName, e));
        }

        Expect(TokenKind.RBrace);
    }

    private Effect ParseEffect() {
        if (_current.Kind == TokenKind.Transition) {
            Advance(); // consume 'transition'
            Expect(TokenKind.To);
            var target = ExpectIdentifier(TokenKind.Identifier, "stage name");
            return new StageTransitionEffect(new StageReference(target));
        }

        if (_current.Kind == TokenKind.Assign) {
            Advance(); // consume 'assign'
            var propName = ExpectIdentifier(TokenKind.Identifier, "property name");
            Expect(TokenKind.To);
            var expr = ParseExpression();
            return new AssignEffect(DomainExpression.Property(propName), expr);
        }

        if (_current.Kind == TokenKind.Create) {
            return ParseCreateEffect();
        }

        if (_current.Kind == TokenKind.Delete) {
            // E1: Soft-delete the current entity instance.
            Advance(); // consume 'delete'
            return new DeleteEntityInstance(new DomainTypeReference(_currentEntityName));
        }

        if (_current.Kind == TokenKind.When) {
            // Subscription effect — this is handled differently
            // (embedded in StageSubscription, not in a standalone action)
            // If we reach here it's a parsing error
            throw Error("Unexpected 'when' inside action body (subscriptions are stage-level)");
        }

        // E3a/E3b: invoke [any|all] [RelName.]ActionName(args) [where expr]
        //   any/all   → collection quantifier (E3b only, requires RelName)
        //   RelName.  → cross-entity invoke (E3b, resolve via relationship)
        //   bare      → self-only invoke (E3a)
        //   where …  → optional filter predicate on target instances
        if (_current.Kind == TokenKind.Invoke) {
            Advance(); // consume 'invoke'

            // Optional quantifier: any / all (identifier text match)
            StageSubscriptionQuantifier? quantifier = null;
            if (_current.Kind == TokenKind.Identifier &&
                string.Equals(_current.Text, "any", StringComparison.OrdinalIgnoreCase)) {
                quantifier = StageSubscriptionQuantifier.Any;
                Advance();
            }
            else if (_current.Kind == TokenKind.Identifier &&
                     string.Equals(_current.Text, "all", StringComparison.OrdinalIgnoreCase)) {
                quantifier = StageSubscriptionQuantifier.All;
                Advance();
            }

            var firstId = ExpectIdentifier(TokenKind.Identifier, "action or relationship name");

            string? targetRelationship = null;
            string actionName;

            if (_current.Kind == TokenKind.Dot) {
                // E3b: invoke RelName.ActionName(args)
                targetRelationship = firstId;
                Advance(); // consume '.'
                actionName = ExpectIdentifier(TokenKind.Identifier, "action name");
            }
            else {
                // E3a: invoke ActionName(args) — self-only
                actionName = firstId;
            }

            // Optional parameter bindings: (name: expr, ...)
            var bindings = new List<PropertyBinding>();
            if (_current.Kind == TokenKind.LParen) {
                Advance(); // consume '('
                while (_current.Kind != TokenKind.RParen) {
                    var paramName = ExpectIdentifier(TokenKind.Identifier, "parameter name");
                    Expect(TokenKind.Colon);
                    var paramExpr = ParseExpression();
                    bindings.Add(new PropertyBinding(paramName, paramExpr));
                    if (_current.Kind == TokenKind.Comma)
                        Advance(); // consume ','
                }
                Expect(TokenKind.RParen);
            }

            // Optional filter: where expr — local shape only (domain cardinality is analyzer).
            DomainExpression? filter = null;
            if (_current.Kind == TokenKind.Identifier &&
                string.Equals(_current.Text, "where", StringComparison.OrdinalIgnoreCase)) {
                Advance(); // consume 'where'
                filter = ParseExpression();
            }

            // Fail-closed local syntax (DMEFF007 mirror): do not accept shapes we will always reject later.
            if (quantifier is not null && targetRelationship is null)
                throw Error(
                    $"'invoke {quantifier.Value.ToString().ToLowerInvariant()}' requires RelName.ActionName " +
                    "(collection cross-entity only; self-invoke cannot use any/all)");
            if (filter is not null && quantifier is null)
                throw Error(
                    "'invoke ... where' requires 'any' or 'all' on a collection relationship " +
                    "(e.g. invoke any Rel.Action where …)");
            if (filter is not null && targetRelationship is null)
                throw Error(
                    "'invoke ... where' requires a relationship target (not self-invoke)");

            return new InvokeActionEffect(actionName, bindings, targetRelationship, quantifier, filter);
        }

        // E4 / E6.4: if (expr) { effects } [else if (expr) { ... }]* [else { effects }]
        if (_current.Kind == TokenKind.If) {
            return ParseConditionalEffect();
        }

        // Check for unsupported effect keywords
        if (_current.Kind == TokenKind.Identifier && _unsupportedKeywords.Contains(_current.Text)) {
            throw new FormatException(
                $"'{_current.Text}' is not supported in Phase 1a");
        }

        throw Error($"Expected effect (transition, assign, create, delete, invoke, if), got '{_current.Text}'");
    }

    /// <summary>
    /// Parses <c>if (cond) { … } [else if (cond) { … }]* [else { … }]</c>.
    /// Chains of <c>else if</c> lower to nested <see cref="ConditionalEffect"/> nodes.
    /// </summary>
    private Effect ParseConditionalEffect() {
        Advance(); // consume 'if'
        Expect(TokenKind.LParen);
        var condition = ParseExpression();
        Expect(TokenKind.RParen);
        Expect(TokenKind.LBrace);
        var thenEffects = new List<Effect>();
        while (_current.Kind != TokenKind.RBrace)
            thenEffects.Add(ParseEffect());
        Expect(TokenKind.RBrace);

        List<Effect>? elseEffects = null;
        if (_current.Kind == TokenKind.Else) {
            Advance(); // consume 'else'
            if (_current.Kind == TokenKind.If) {
                // else if → nest another ConditionalEffect as the sole else branch
                elseEffects = [ParseConditionalEffect()];
            }
            else {
                Expect(TokenKind.LBrace);
                elseEffects = new List<Effect>();
                while (_current.Kind != TokenKind.RBrace)
                    elseEffects.Add(ParseEffect());
                Expect(TokenKind.RBrace);
            }
        }

        return new ConditionalEffect(condition, thenEffects, elseEffects);
    }

    private Effect ParseCreateEffect() {
        Advance(); // consume 'create'

        string? relationshipName = null;

        if (_current.Kind == TokenKind.In) {
            // create in relName { ... }
            Advance(); // consume 'in'
            relationshipName = ExpectIdentifier(TokenKind.Identifier, "relationship name");
            Expect(TokenKind.LBrace);
            var initializers = ParsePropertyInitializers();
            return new CreateEntityInRelationshipEffect(relationshipName, initializers);
        }

        // create EntityName { ... }
        var entityTypeName = ExpectIdentifier(TokenKind.Identifier, "entity type name");
        Expect(TokenKind.LBrace);
        var initList = ParsePropertyInitializers();
        return new CreateEntityInstance(
            new DomainTypeReference(entityTypeName),
            initList,
            null);
    }

    private List<PropertyBinding> ParsePropertyInitializers() {
        var initializers = new List<PropertyBinding>();
        while (_current.Kind != TokenKind.RBrace) {
            var propName = ExpectIdentifier(TokenKind.Identifier, "property name");
            Expect(TokenKind.Colon);
            var expr = ParseExpression();
            initializers.Add(new PropertyBinding(propName, expr));
        }
        Expect(TokenKind.RBrace);
        return initializers;
    }

    private void ParseSubscription(string stageName, List<DomainChange> changes) {
        Advance(); // consume 'when'

        // P4-1: optional subscription quantifier: when [any|all] Rel Stage…
        var quantifier = ParseSubscriptionQuantifier();

        var relName = ExpectIdentifier(TokenKind.Identifier, "relationship name");
        // P2.5: Accept comma-separated stage names: "when Rel Active, Done"
        var targetStages = new List<string>();
        targetStages.Add(ExpectIdentifier(TokenKind.Identifier, "target stage name"));
        while (_current.Kind == TokenKind.Comma) {
            Advance(); // consume ','
            targetStages.Add(ExpectIdentifier(TokenKind.Identifier, "target stage name"));
        }
        // Optional peer binder: when Rel Stage as name { … }
        string? peerBinding = null;
        if (_current.Kind == TokenKind.As) {
            Advance();
            peerBinding = ExpectIdentifier(TokenKind.Identifier, "peer binding name");
        }
        Expect(TokenKind.LBrace);

        var effects = new List<Effect>();
        while (_current.Kind != TokenKind.RBrace) {
            effects.Add(ParseEffect());
        }
        Expect(TokenKind.RBrace);

        var subscription = new StageSubscription(relName, targetStages, quantifier, effects, peerBinding);
        changes.Add(new AddStageSubscriptionChange(_currentEntityName, stageName, subscription));
    }

    /// <summary>
    /// Parses an optional subscription quantifier (<c>any</c> / <c>all</c>)
    /// following <c>when</c>. Omitted quantifier defaults to
    /// <see cref="StageSubscriptionQuantifier.Each"/> (current product default).
    /// Mirrors the <c>invoke any|all</c> pattern (identifier text match).
    /// </summary>
    private StageSubscriptionQuantifier ParseSubscriptionQuantifier() {
        if (_current.Kind == TokenKind.Identifier &&
            string.Equals(_current.Text, "any", StringComparison.OrdinalIgnoreCase)) {
            Advance(); // consume 'any'
            return StageSubscriptionQuantifier.Any;
        }
        if (_current.Kind == TokenKind.Identifier &&
            string.Equals(_current.Text, "all", StringComparison.OrdinalIgnoreCase)) {
            Advance(); // consume 'all'
            return StageSubscriptionQuantifier.All;
        }
        return StageSubscriptionQuantifier.Each;
    }

    /// <summary>
    /// Parses an entity-level <c>when RelName TargetStage [as name] { effects }</c> block.
    /// Called when <c>when</c> is encountered at the entity body level (outside any stage).
    /// </summary>
    private void ParseEntitySubscription(List<DomainChange> changes) {
        Advance(); // consume 'when'

        // P4-1: optional subscription quantifier: when [any|all] Rel Stage…
        var quantifier = ParseSubscriptionQuantifier();

        var relName = ExpectIdentifier(TokenKind.Identifier, "relationship name");
        var targetStages = new List<string>();
        targetStages.Add(ExpectIdentifier(TokenKind.Identifier, "target stage name"));
        while (_current.Kind == TokenKind.Comma) {
            Advance(); // consume ','
            targetStages.Add(ExpectIdentifier(TokenKind.Identifier, "target stage name"));
        }
        string? peerBinding = null;
        if (_current.Kind == TokenKind.As) {
            Advance();
            peerBinding = ExpectIdentifier(TokenKind.Identifier, "peer binding name");
        }
        Expect(TokenKind.LBrace);

        var effects = new List<Effect>();
        while (_current.Kind != TokenKind.RBrace) {
            effects.Add(ParseEffect());
        }
        Expect(TokenKind.RBrace);

        var subscription = new StageSubscription(relName, targetStages, quantifier, effects, peerBinding);
        changes.Add(new AddEntitySubscriptionChange(_currentEntityName, subscription));
    }

    private Exception N2RelationshipNotSupported() =>
        Error(
            "The 'relationship Name from Source to Target one|many' form is not supported. " +
            "Use a navigation property on the source entity (e.g. 'orders: many Order').");

    /// <summary>
    /// Returns true if the current token starts a navigation property line (N1 form).
    /// Patterns: "many [owned] Type", "one [owned] Type", "owned Type", "Type" (bare entity name).
    /// </summary>
    /// <summary>
    /// Fail-closed rejection for top-level shapes the grammar table does not
    /// model: unsupported keywords (e.g. 'actor') and the removed inheritance
    /// form ('Parent: Base entity'). Mirrors the diagnostics ParseEntity
    /// previously produced at the same position.
    /// </summary>
    private Exception TopLevelRejection() {
        if (PeekIs(TokenKind.Colon)) {
            var name = _current.Text;
            var typeWord = _tokenReader.Peek(2);
            if (typeWord.Kind == TokenKind.Identifier && _unsupportedKeywords.Contains(typeWord.Text)) {
                return new FormatException(
                    $"'{typeWord.Text}' is not supported in Phase 1a (use 'entity' instead)");
            }
            if (typeWord.Kind == TokenKind.Identifier
                && _tokenReader.Peek(3).Kind == TokenKind.Entity) {
                return new FormatException(
                    $"Entity inheritance ('{name}: {typeWord.Text} entity') is no longer supported. " +
                    $"Define '{typeWord.Text}' properties directly on '{name}'.");
            }
        }
        return Error($"Expected 'entity' or 'enum' definition, got '{_current.Text}'");
    }

    /// <summary>
    /// Parses a top-level enum type declaration: <c>Name: enum { Member1, Member2 }</c>.
    /// Enum types must be declared before entities that reference them.
    /// </summary>
    private void ParseEnumType(List<DomainChange> changes) {
        var name = ExpectIdentifier(TokenKind.Identifier, "enum type name");
        Expect(TokenKind.Colon);
        Expect(TokenKind.Enum);
        Expect(TokenKind.LBrace);

        var members = new List<string>();
        while (_current.Kind == TokenKind.Identifier) {
            members.Add(_current.Text);
            Advance();
            if (_current.Kind == TokenKind.Comma)
                Advance();
        }

        Expect(TokenKind.RBrace);

        _enumTypeNames.Add(name);
        changes.Add(new AddEnumTypeChange(name, members));
    }

    private bool IsNavLine() {
        // TokenKind.Many and TokenKind.One are unambiguous nav starts
        if (_current.Kind == TokenKind.Many || _current.Kind == TokenKind.One)
            return true;

        // "owned" as the first token after : → nav (must be followed by TypeName)
        if (_current.Kind == TokenKind.Owned)
            return true;

        // Bare identifier that isn't a primitive type, keyword, or reserved construct
        if (_current.Kind == TokenKind.Identifier && !IsPrimitiveType(_current.Kind)) {
            var text = _current.Text;
            // Exclude known keywords that aren't primitives but shouldn't be nav targets
            return !_unsupportedKeywords.Contains(text)
                && text != "entity" && text != "stage" && text != "action"
                && text != "policy" && text != "relationship" && text != "when"
                && text != "require" && text != "transition" && text != "assign"
                && text != "from" && text != "to"
                && text != "null" && text != "true" && text != "false"
                && text != "owned" // handled above
                && text != "not" && text != "and" && text != "or";
        }

        return false;
    }

    /// <summary>
    /// Parses an N1 navigation property line after "name :".
    /// Consumes tokens and queues a <see cref="PendingNav"/> for deferred resolution.
    /// </summary>
    private void ParseNavLine(string name) {
        var cardinality = RelationshipCardinality.OneToOne;
        var owned = false;

        // Check for cardinality keyword
        if (_current.Kind == TokenKind.Many) {
            cardinality = RelationshipCardinality.OneToMany;
            Advance();
        }
        else if (_current.Kind == TokenKind.One) {
            Advance(); // consume 'one'
        }

        // Check for optional 'owned'
        if (_current.Kind == TokenKind.Owned) {
            owned = true;
            Advance();
        }

        // Remaining identifier is the target type name
        // Must be an identifier (not a primitive type keyword)
        if (_current.Kind != TokenKind.Identifier) {
            var hint = IsPrimitiveType(_current.Kind)
                ? $" '{_current.Text}' is a primitive type, not an entity. Use a primitive property declaration instead."
                : $" unexpected token '{_current.Text}'";
            throw Error($"Navigation property '{name}' requires an entity type as target:{hint}");
        }
        var targetType = ExpectIdentifier(TokenKind.Identifier, "target entity name");

        _pendingNavs.Add(new PendingNav(_currentEntityName, name, targetType, cardinality, owned));
    }

    /// <summary>
    /// Resolves all pending navigation properties against known entity names.
    /// Called after all entities have been parsed. Errors if a target type is
    /// unknown or is a primitive type.
    /// </summary>
    private void ResolvePendingNavs(List<DomainChange> changes) {
        foreach (var nav in _pendingNavs) {
            if (IsPrimitiveTypeToken(nav.TargetTypeName)) {
                throw Error($"Navigation property '{nav.PropertyName}': '{nav.TargetTypeName}' is a primitive type, not an entity. Use a primitive property declaration instead.");
            }
            if (!_entityNames.Contains(nav.TargetTypeName)) {
                throw Error($"Navigation property '{nav.PropertyName}' references unknown entity '{nav.TargetTypeName}'. No entity with that name was found in the domain.");
            }

            // Check property name collision
            if (_entityPropertyNames.TryGetValue(nav.SourceEntityName, out var props) && props.Contains(nav.PropertyName)) {
                throw Error($"Navigation property '{nav.PropertyName}' on '{nav.SourceEntityName}' conflicts with an existing property of the same name.");
            }

            // Check duplicate relationship name (among navs or with N2 lines already emitted)
            if (!_relationshipNames.Add(nav.PropertyName)) {
                throw Error($"Relationship '{nav.PropertyName}' is defined more than once. Relationship names must be unique within a domain.");
            }

            changes.Add(new AddRelationshipChange(
                nav.PropertyName,
                new DomainTypeReference(nav.SourceEntityName),
                new DomainTypeReference(nav.TargetTypeName),
                nav.Cardinality, [], nav.SourceOwnsTarget));
        }
        _pendingNavs.Clear();
    }

    private static bool IsPrimitiveTypeToken(string typeName) => typeName switch {
        "Text" or "Number" or "Boolean" or "DateTime" or "Date" => true,
        _ => false,
    };

    private void ParsePolicy(string name, List<DomainChange> changes) {
        Advance(); // consume 'policy'
        Expect(TokenKind.LBrace);
        var expr = ParseExpression();
        changes.Add(new AddPolicyToEntityChange(_currentEntityName, new Policy(name, expr)));
        _entityPolicies[name] = expr;
        Expect(TokenKind.RBrace);
    }

    // ── Annotation parser ────────────────────────────────────

    /// <summary>
    /// Parses a parenthesized annotation after the keyword has been consumed.
    /// Syntax: <c>keyword("arg1", "arg2")</c> or <c>keyword(42)</c>.
    /// Produces an <see cref="Annotation"/> with positional arguments keyed
    /// as <c>"0"</c>, <c>"1"</c>, etc.
    /// </summary>
    private Facet ParseAnnotation(string keyword) {
        Expect(TokenKind.LParen);

        var args = new Dictionary<string, AnnotationValue>();
        int positionalIndex = 0;

        while (_current.Kind != TokenKind.RParen) {
            if (_current.Kind == TokenKind.StringLiteral) {
                args[positionalIndex.ToString()] = new AnnotationString(_current.Text);
                Advance();
            }
            else if (_current.Kind == TokenKind.Number) {
                args[positionalIndex.ToString()] = new AnnotationNumber(
                    double.Parse(_current.Text, CultureInfo.InvariantCulture));
                Advance();
            }
            else if (_current.Kind == TokenKind.True) {
                args[positionalIndex.ToString()] = new AnnotationBool(true);
                Advance();
            }
            else if (_current.Kind == TokenKind.False) {
                args[positionalIndex.ToString()] = new AnnotationBool(false);
                Advance();
            }
            else if (_current.Kind == TokenKind.Null) {
                args[positionalIndex.ToString()] = new AnnotationNull();
                Advance();
            }
            else {
                throw Error($"Expected annotation argument (string, number, bool, null), got '{_current.Text}'");
            }

            positionalIndex++;

            if (_current.Kind == TokenKind.Comma) {
                Advance();
                if (_current.Kind == TokenKind.RParen) {
                    throw Error($"Trailing comma in annotation '{keyword}(...)' arguments");
                }
            }
            else if (_current.Kind != TokenKind.RParen) {
                throw Error(
                    $"Expected ',' or ')' in annotation '{keyword}(...)', got '{_current.Text}'");
            }
        }

        Expect(TokenKind.RParen);
        return new Annotation(keyword, args);
    }

    // ── Constraint parser ─────────────────────────────────────

    private Constraint? ParseConstraint() {
        switch (_current.Kind) {
            case TokenKind.Required:
                Advance();
                return new RequiredConstraint();

            case TokenKind.Unique:
                Advance();
                return new UniqueConstraint();

            case TokenKind.Equals:
                Advance();
                Expect(TokenKind.LParen);
                var dvExpr = ParseExpression();
                Expect(TokenKind.RParen);
                return new DefaultValueConstraint(dvExpr);

            case TokenKind.Enum:
                throw Error("Inline enum(...) constraints are no longer supported. " +
                    "Use a top-level enum type declaration: Name: enum { Member1, Member2 }");

            case TokenKind.Range:
                Advance();
                Expect(TokenKind.LParen);
                object? min = null, max = null;
                if (_current.Kind == TokenKind.Number) {
                    min = double.Parse(_current.Text, CultureInfo.InvariantCulture);
                    Advance();
                }
                if (_current.Kind == TokenKind.Comma) {
                    Advance();
                }
                if (_current.Kind == TokenKind.Number) {
                    max = double.Parse(_current.Text, CultureInfo.InvariantCulture);
                    Advance();
                }
                Expect(TokenKind.RParen);
                return new RangeConstraint(min, max);

            case TokenKind.Length:
                Advance();
                Expect(TokenKind.LParen);
                var lenMin = int.Parse(_current.Text, CultureInfo.InvariantCulture);
                Advance();
                var lenMax = lenMin; // default to same value for single-arg form "length(3)"
                if (_current.Kind == TokenKind.Comma) {
                    Advance();
                    if (_current.Kind == TokenKind.Number) {
                        lenMax = int.Parse(_current.Text, CultureInfo.InvariantCulture);
                        Advance();
                    }
                }
                Expect(TokenKind.RParen);
                return new LengthConstraint(lenMin, lenMax);

            case TokenKind.Pattern:
                Advance();
                Expect(TokenKind.LParen);
                var pattern = Expect(TokenKind.StringLiteral).Text;
                Expect(TokenKind.RParen);
                return new PatternConstraint(pattern);

            default:
                return null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────

    private string ParseTypeName() {
        if (IsPrimitiveType(_current.Kind)) {
            var typeName = _current.Kind switch {
                TokenKind.Text => "Text",
                TokenKind.NumberType => "Number",
                TokenKind.BooleanType => "Boolean",
                TokenKind.DateTimeType => "DateTime",
                TokenKind.DateType => "Date",
                _ => throw Error($"Unknown type '{_current.Kind}'"),
            };
            Advance();
            return typeName;
        }
        if (_current.Kind == TokenKind.Identifier) {
            return ExpectIdentifier(TokenKind.Identifier, "type name");
        }
        throw Error($"Expected a type name, got '{_current.Text}'");
    }

    /// <summary>
    /// Matcher peeks the reader buffer; this parser holds the head token in
    /// <see cref="_current"/>. Unread so Peek(1) is the head, then restore.
    /// </summary>
    private MatchResult<DslTokenKind>? MatchRule(string ruleName) {
        _tokenReader.Unread(_current);
        var match = _matcher.TryMatch(ruleName);
        _current = _tokenReader.Read();
        return match;
    }

    private void Advance() {
        _current = _tokenReader.Read();
    }

    private Token Expect(TokenKind kind) {
        if (_current.Kind != kind)
            throw Error($"Expected {kind}, got '{_current.Text}' ({_current.Kind})");
        var t = _current;
        Advance();
        return t;
    }

    private string ExpectIdentifier(TokenKind kind, string context) {
        if (_current.Kind != kind)
            throw Error($"Expected {context}, got '{_current.Text}'");
        var t = _current.Text;
        Advance();
        return t;
    }

    private bool PeekIs(TokenKind kind) {
        return _tokenReader.Peek(1).Kind == kind;
    }

    private List<string> ParseIdentifierList() {
        var list = new List<string>();
        list.Add(ExpectIdentifier(TokenKind.Identifier, "identifier"));
        while (_current.Kind == TokenKind.Comma) {
            Advance();
            list.Add(ExpectIdentifier(TokenKind.Identifier, "identifier"));
        }
        return list;
    }

    private static bool IsPrimitiveType(TokenKind kind) => kind switch {
        TokenKind.Text or TokenKind.NumberType or TokenKind.BooleanType
            or TokenKind.DateTimeType or TokenKind.DateType => true,
        _ => false,
    };

    private static bool IsConstraint(TokenKind kind) => kind switch {
        TokenKind.Required or TokenKind.Unique or TokenKind.Range
            or TokenKind.Length or TokenKind.Pattern
            or TokenKind.Equals or TokenKind.Enum => true,
        _ => false,
    };

    private static readonly HashSet<string> _unsupportedKeywords = new(StringComparer.OrdinalIgnoreCase) {
        "actor", "value", "schedule", "parallel", "for", "function"
    };

    /// <summary>
    /// Throws a specific "not supported in Phase 1a" error if <paramref name="keyword"/>
    /// is a known unsupported construct keyword. Otherwise returns without throwing.
    /// </summary>
    private static void CheckUnsupportedKeyword(string name, string keyword) {
        if (_unsupportedKeywords.Contains(keyword)) {
            throw new FormatException(
                $"'{keyword}' is not supported in Phase 1a (used as type for '{name}')");
        }
    }

    private Exception Error(string message) =>
        new GrammarException(message, _current.Line, _current.Col);
}