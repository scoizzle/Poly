using System.Globalization;

using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Bootstrap;
using Poly.DomainModeling.Ontology.Contract;
using Poly.DomainModeling.Runtime;
using Poly.Grammar;

using TokenKind = Poly.DomainModeling.Language.DslTokenKind;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Language;

/// <summary>
/// Applies the loaded session's meaning to a parsed <see cref="DslSyntaxTree"/>.
/// Structure/annotations: Matcher over <see cref="DslGrammar"/>.
/// Expressions: <see cref="DslExpressionParser"/> (product shapes + session concept folds).
/// Cursor mechanics (Current/Advance/Expect/MatchRule/Consume/Error) come from
/// <see cref="DslCursor"/> — the reader owns the committed position.
/// </summary>
public sealed class DomainResolver : DslCursor {
    private readonly DslExpressionParser _expressions;
    private int _entityIndex;
    private bool _primitivesAdded;

    // Accumulated changes for the current entity
    private string _currentEntityName = "";
    private readonly Dictionary<string, DomainExpression> _entityPolicies = new(StringComparer.Ordinal);

    // Pending requires that must be resolved after the full entity body is parsed
    private readonly List<PendingRequire> _pendingRequires = new();
    private readonly List<PendingStageRequire> _pendingStageRequires = new();

    // Pending navigation properties (N1 form) resolved after all entities are known
    private readonly List<PendingNav> _pendingNavs = new();

    // Entity names collected during parsing, for nav target resolution
    private readonly HashSet<string> _entityNames = new(StringComparer.Ordinal);

    // Enum type names, for distinguishing typed properties from nav lines
    private readonly HashSet<string> _enumTypeNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _valueTypeNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _primitiveTypeNames = new(StringComparer.Ordinal);

    // Property names per entity, for collision detection with navs
    private readonly Dictionary<string, HashSet<string>> _entityPropertyNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _entityStageNames = new(StringComparer.Ordinal);

    // Relationship names from N1 nav lines, for duplicate detection
    private readonly Dictionary<string, HashSet<string>> _relationshipNamesByEntity = new(StringComparer.Ordinal);

    // Q1′′′.5 / Q1'''''.2: Prevents recursive `Rel where ...` parsing inside a where body.
    // (Flag lives on DslParseCursorBase as _inWhereBody.)

    // Explicit parse inputs / annotation support
    private readonly AnnotationRegistry? _annotations;
    private readonly DomainSession _session;
    private readonly DslSyntaxTree _tree;

    // Contract names seeded from a `uses` id, and names a body block already declared.
    private readonly Dictionary<string, string> _seededContracts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _authoredContracts = new(StringComparer.Ordinal);

    private readonly record struct PendingRequire(
        string ActionName,
        string? StageName,
        string PolicyName,
        bool Negated);

    private readonly record struct PendingStageRequire(
        string StageName,
        string PolicyName,
        bool Negated,
        DslToken At);

    private readonly record struct PendingNav(
        string SourceEntityName,
        string PropertyName,
        string TargetTypeName,
        RelationshipCardinality Cardinality,
        bool SourceOwnsTarget,
        DslToken At);

    public DomainResolver(DslSyntaxTree tree, DomainSession session)
        : base(TokenReaderFor(tree), reader => new Matcher<DslToken, DslTokenKind>(session.Language.Grammar, reader)) {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(session);
        _tree = tree;
        _session = session;
        _annotations = session.Annotations;
        _expressions = new DslExpressionParser(this, session.ExpressionForms, session.Folds);
    }

    private static ListTokenReader<DslToken, DslTokenKind> TokenReaderFor(DslSyntaxTree tree) {
        ArgumentNullException.ThrowIfNull(tree);
        return new(tree.Tokens, kind => kind == DslTokenKind.EndOfFile);
    }

    private DomainExpression ParseExpression() =>
        DslExpressionFragment.RejectUnresolvedDuration(_expressions.ParseExpression());

    /// <summary>
    /// Resolves the document and returns a list of <see cref="DomainChange"/>
    /// that, when applied via evolution, produces the declared domain.
    /// </summary>
    public List<DomainChange> Resolve() {
        var changes = new List<DomainChange>();

        // ── Domain header ─────────────────────────────────────
        Expect(TokenKind.Domain);
        var domainName = ExpectIdentifier(TokenKind.Identifier, "domain name");
        changes.Add(new SetDomainNameChange(domainName));

        // The reader owns the header shape (including an optional same-line pin); the cursor
        // only has to move past the tokens it consumed, so the two cannot disagree.
        for (var headerIndex = 2; headerIndex < _tree.HeaderEnd; headerIndex++)
            Advance();

        for (var useIndex = 0; useIndex < _tree.Uses.Count; useIndex++) {
            var extensionId = _tree.Uses[useIndex];
            var pin = useIndex < _tree.UsesVersions.Count ? _tree.UsesVersions[useIndex] : null;
            changes.Add(new AddDomainExtensionChange(extensionId));

            var contribution = _session.FindContractContribution(extensionId);
            if (contribution is not null) {
                _seededContracts[contribution.Surface.Name] = extensionId;
                changes.AddRange(contribution.ToChanges(pin));
            }
            else if (pin is not null) {
                throw ErrorAtUse(extensionId,
                    $"Extension '{extensionId}' pins surface version '{pin}' but contributed no contract.");
            }
        }

        // ── Enum type definitions + entity definitions ─────────
        // Parse entities and enum types in order; enum types must precede entities
        // that reference them for property type resolution.
        // Dispatch is matcher-driven over the "top" rule; unmodeled shapes get
        // the same targeted diagnostics ParseEntity would produce.
        while (Current.Kind == TokenKind.Identifier || Current.Kind == TokenKind.Public) {
            if (TryTakeVisibility(out var visibility)) {
                ParseMarkedTop(changes, visibility);
                continue;
            }
            if (IsFunctionDeclarationStart()) {
                ParseFunction(changes);
                continue;
            }
            switch (MatchRule("top")?.PatternName) {
                case "enum":
                    ParseEnumType(changes);
                    break;
                case "entity":
                    ParseEntity(changes);
                    break;
                default:
                    if (TryParseNamedTopLevel(changes))
                        break;
                    throw TopLevelRejection();
            }
        }

        // ── Resolve N1 navigation properties ───────────────────
        ResolvePendingNavs(changes);

        if (Current.Kind == TokenKind.Relationship)
            throw N2RelationshipNotSupported();

        Expect(TokenKind.EndOfFile);
        return changes;
    }

    /// <summary>Positions a header finding on the <c>uses</c> id token for <paramref name="extensionId"/>.</summary>
    private FormatException ErrorAtUse(string extensionId, string message) {
        for (var i = 2; i + 1 < _tree.Tokens.Count && i < _tree.HeaderEnd; i++) {
            if (_tree.Tokens[i].Kind != TokenKind.Uses
                || _tree.Tokens[i + 1].Kind != TokenKind.Identifier
                || !string.Equals(_tree.Tokens[i + 1].Text, extensionId, StringComparison.Ordinal))
                continue;
            var token = _tree.Tokens[i + 1];
            return GrammarError.Error(message, token.Line, token.Col);
        }
        return GrammarError.Error(message);
    }

    private void EnsurePrimitivesOnce(List<DomainChange> changes) {
        if (_primitivesAdded) return;
        _primitivesAdded = true;
        foreach (var change in CanonicalBuiltInTypeCatalog.CreateChanges())
            changes.Add(change);
        var adding = changes.OfType<AddDomainExtensionChange>()
            .Select(c => c.ExtensionId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var (libraryId, name, semantics) in _session.PrimitiveSeeds) {
            if (adding.Contains(libraryId))
                continue;
            changes.Add(new AddPrimitiveTypeChange(name, semantics, []));
        }
        IndexPrimitiveNames(changes);
    }

    private void IndexPrimitiveNames(List<DomainChange> changes) {
        foreach (var def in CanonicalBuiltInTypeCatalog.Definitions)
            _primitiveTypeNames.Add(def.Name);
        foreach (var (_, name, _) in _session.PrimitiveSeeds)
            _primitiveTypeNames.Add(name);
        foreach (var add in changes.OfType<AddDomainExtensionChange>()) {
            if (!ExtensionCatalog.Core.Contains(add.ExtensionId))
                continue;
            foreach (var (name, _) in ExtensionCatalog.Core.Resolve(add.ExtensionId).PrimitiveSeeds)
                _primitiveTypeNames.Add(name);
        }
    }

    private bool IsKnownPrimitiveName(string name) =>
        _primitiveTypeNames.Contains(name);

    private void ParseEntity(List<DomainChange> changes) {
        var entityName = ExpectIdentifier(TokenKind.Identifier, "entity name");
        Expect(TokenKind.Colon);

        // The "top" rule guarantees this is 'entity'; unsupported-keyword and
        // removed-inheritance shapes never reach this method (TopLevelRejection).
        Expect(TokenKind.Entity);
        ParseEntityAfterKind(changes, entityName, Visibility.Internal);
    }

    private const string KeyKeyword = "key";

    /// <summary>
    /// Parses <c>key(a, b)</c> after the header annotations. The member names are checked by
    /// analysis, not here; a malformed list (empty, doubled or trailing comma, unclosed) fails closed.
    /// </summary>
    private List<string> ParseKeyClause() {
        Advance(); // 'key'
        Expect(TokenKind.LParen);
        if (Current.Kind == TokenKind.RParen)
            throw Error("A 'key(...)' clause must name at least one member; found an empty key().");

        var members = new List<string>();
        while (true) {
            members.Add(ExpectIdentifier(TokenKind.Identifier, "key member name"));
            if (Current.Kind == TokenKind.Comma) {
                Advance();
                if (Current.Kind == TokenKind.RParen)
                    throw Error("Trailing comma in 'key(...)' member list");
                continue;
            }
            if (Current.Kind != TokenKind.RParen)
                throw Error($"Expected ',' or ')' in 'key(...)', got '{Current.Text}'");
            break;
        }
        Expect(TokenKind.RParen);
        return members;
    }

    private void ParseEntityAfterKind(List<DomainChange> changes, string entityName, Visibility visibility) {
        _currentEntityName = entityName;
        _entityPolicies.Clear();
        _pendingRequires.Clear();
        _pendingStageRequires.Clear();
        _entityIndex++;

        _entityNames.Add(entityName);
        changes.Add(new AddEntityChange(entityName, [], visibility));

        // ── Entity header facets (pack-registered annotations) ──
        var termKeywords = new HashSet<string>(StringComparer.Ordinal);
        while (TryParseRegisteredAnnotation(out var headerFacet)) {
            ValidateTermFacet(headerFacet, termKeywords, $"entity '{entityName}'", allowTerm: false);
            changes.Add(new AddFacetToDomainTypeChange(entityName, headerFacet));
        }
        // #405 key clause: the last header element, immediately before '{'.
        if (Current.Kind == TokenKind.Identifier && Current.Text == KeyKeyword && PeekIs(TokenKind.LParen)) {
            changes.Add(new SetEntityKeyChange(entityName, ParseKeyClause()));
        }
        if (LooksLikeAnnotationCall()) {
            throw Error(
                $"Unknown or unregistered annotation '{Current.Text}'. " +
                "Enable a pack that registers this keyword, or remove the annotation.");
        }
        if (Current.Kind == TokenKind.Identifier && Current.Text == KeyKeyword && PeekIs(TokenKind.LParen)) {
            throw Error($"Entity '{entityName}' declares more than one 'key(...)' clause.");
        }

        RejectRetiredRequiredWord();
        Expect(TokenKind.LBrace);

        EnsurePrimitivesOnce(changes);

        while (Current.Kind != TokenKind.RBrace) {
            if (Current.Kind == TokenKind.Relationship) {
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

                    case "marked-action": {
                            Expect(TokenKind.Public);
                            Expect(TokenKind.Action);
                            var name = ExpectIdentifier(TokenKind.Identifier, "action name");
                            ParseActionBody(name, changes, stageName: null, preParsedParams: null, Visibility.Public);
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
                            if (Current.Kind is not (TokenKind.Action or TokenKind.LBrace or TokenKind.When or TokenKind.Require)) {
                                throw Error($"Expected action after '{name}(...)', got '{Current.Text}'");
                            }
                            ParseActionBody(name, changes, stageName: null, actionParams);
                            continue;
                        }

                    case "typed-line": {
                            // Enum-typed property or bare (N1) navigation — resolved by
                            // known enum names, mirroring the legacy IsNavLine dispatch.
                            var name = ExpectIdentifier(TokenKind.Identifier, "property name");
                            Expect(TokenKind.Colon);
                            if (_enumTypeNames.Contains(Current.Text)
                                || _valueTypeNames.Contains(Current.Text)
                                || IsKnownPrimitiveName(Current.Text)) {
                                var typeName = ExpectIdentifier(TokenKind.Identifier, "type name");
                                ParseNamedTypeProperty(name, typeName, changes);
                            }
                            else if (IsNavLine()) {
                                ParseNavLine(name);
                            }
                            else {
                                CheckUnsupportedKeyword(name, Current.Text);
                                throw Error($"Expected type, stage, action, policy, or navigation property after '{name}:'");
                            }
                            continue;
                        }

                    case "property": {
                            var name = ExpectIdentifier(TokenKind.Identifier, "property name");
                            Expect(TokenKind.Colon);
                            ParseProperty(name, Current.Kind, changes);
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
                            // Primitive keyword used as property name (e.g. "Number: Text"): the type
                            // must be a primitive too (DslTokenReader.IsPropertyName gives MCP the same rule).
                            var name = Current.Text;
                            Advance(); // consume type keyword (e.g. 'Number')
                            Expect(TokenKind.Colon);
                            if (!DslGrammar.IsPrimitiveType(Current.Kind, Current.Text, IsKnownPrimitiveName))
                                throw Error($"Expected type after '{name}:', got '{Current.Text}'");
                            if (IsPrimitiveType(Current.Kind)) {
                                ParseProperty(name, Current.Kind, changes);
                            }
                            else {
                                var typeName = Current.Text;
                                Advance();
                                ParseNamedTypeProperty(name, typeName, changes);
                            }
                            continue;
                        }
                }
            }

            // Fallback: legacy action forms not modeled by the grammar table —
            // 'Name: { … }', 'Name: when …', 'Name: require …' (the printer no
            // longer emits these; kept for backward parse compatibility).
            if (Current.Kind == TokenKind.Identifier && PeekIs(TokenKind.Colon)) {
                var name = Current.Text;
                Advance(); // consume identifier
                Expect(TokenKind.Colon);
                if (Current.Kind is TokenKind.LBrace or TokenKind.When or TokenKind.Require) {
                    ParseStandaloneAction(name, changes);
                }
                else {
                    CheckUnsupportedKeyword(name, Current.Text);
                    throw Error($"Expected type, stage, action, policy, or navigation property after '{name}:'");
                }
                continue;
            }

            throw Error($"Expected property, stage, action, or policy, got '{Current.Text}'");
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
                    new Policy(policyName, DomainExpression.Not(expr)), StageName: pr.StageName));
            }
            else {
                // require PolicyName
                if (!_entityPolicies.TryGetValue(pr.PolicyName, out var expr)) {
                    throw Error($"Action '{pr.ActionName}' requires policy '{pr.PolicyName}' " +
                        $"which is not defined on entity '{_currentEntityName}'.");
                }
                changes.Add(new AddPolicyToActionChange(_currentEntityName, pr.ActionName,
                    new Policy(pr.PolicyName, expr), StageName: pr.StageName));
            }
        }

        foreach (var sr in _pendingStageRequires) {
            if (!_entityPolicies.TryGetValue(sr.PolicyName, out var expr)) {
                throw GrammarError.Error(
                    $"Stage '{sr.StageName}' requires policy '{sr.PolicyName}' " +
                    $"which is not defined on entity '{_currentEntityName}'.",
                    sr.At.Line, sr.At.Col);
            }
            var policy = sr.Negated
                ? new Policy($"not_{sr.PolicyName}", DomainExpression.Not(expr))
                : new Policy(sr.PolicyName, expr);
            changes.Add(new AddPolicyToStageChange(_currentEntityName, sr.StageName, policy));
        }
    }

    private void ParseProperty(string name, TokenKind typeKind, List<DomainChange> changes) {
        Advance(); // consume type

        var typeName = typeKind switch {
            TokenKind.Text => "Text",
            TokenKind.NumberType => "Number",
            TokenKind.BooleanType => "Boolean",
            _ => throw Error($"Unknown type '{typeKind}'"),
        };

        ParseNamedTypeProperty(name, typeName, changes);
    }

    private void ParseNamedTypeProperty(string name, string typeName, List<DomainChange> changes) {
        TrackPropertyName(_currentEntityName, name);
        changes.Add(new AddPropertyToEntityChange(_currentEntityName,
            new Property(name, new DomainTypeReference(typeName), [])));
        ParsePropertyTail(name, typeName, changes);
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
    private void ParsePropertyTail(string propertyName, string typeName, List<DomainChange> changes) {
        var termKeywords = new HashSet<string>(StringComparer.Ordinal);
        while (IsConstraint(Current.Kind)
               || (Current.Kind == TokenKind.Identifier && PeekIs(TokenKind.LParen))) {
            if (IsConstraint(Current.Kind)) {
                var constraint = ParseConstraint(typeName);
                if (constraint is not null) {
                    changes.Add(new AddConstraintToPropertyChange(
                        _currentEntityName, propertyName, constraint));
                }
                continue;
            }

            // Annotation-shaped identifier(…) — grammar match or fail-closed RD (GI-5).
            if (TryParseRegisteredAnnotation(out var facet)) {
                ValidateTermFacet(
                    facet, termKeywords, $"property '{_currentEntityName}.{propertyName}'", allowTerm: false);
                changes.Add(new AddFacetToPropertyChange(
                    _currentEntityName, propertyName, facet));
                continue;
            }

            // Fail closed for annotation-shaped args (literals / empty / trailing comma),
            // not legacy action heads like Checkout(days: Number): action { … }.
            if (LooksLikeAnnotationCall()) {
                throw Error(
                    $"Unknown or unregistered annotation '{Current.Text}'. " +
                    "Enable a pack that registers this keyword, or remove the annotation.");
            }

            break;
        }

        RejectRetiredRequiredWord();
    }

    /// <summary>
    /// Term metadata is one value per key, and <c>term</c> names an enum member rather
    /// than an entity or property. A repeat or a misplaced <c>term</c> is a typo, not
    /// metadata, so both fail closed at the position.
    /// </summary>
    private void ValidateTermFacet(Facet facet, HashSet<string> seen, string target, bool allowTerm) {
        if (facet is not Annotation annotation || !TermFacets.IsTermKeyword(annotation.Name))
            return;
        if (!allowTerm && annotation.Name == TermFacets.Term)
            throw Error($"Term annotation 'term(...)' applies to enum members only; found on {target}.");
        if (!seen.Add(annotation.Name))
            throw Error($"Duplicate term annotation '{annotation.Name}(...)' on {target}.");
    }

    /// <summary>
    /// Pack-registered annotation: Matcher recognizes valid shapes; invalid shapes
    /// (e.g. trailing comma) still enter <see cref="ParseAnnotation"/> for fail-closed
    /// diagnostics when the keyword is registered and the call is annotation-shaped.
    /// </summary>
    private bool TryParseRegisteredAnnotation(out Facet facet) {
        facet = null!;
        if (Current.Kind != TokenKind.Identifier)
            return false;

        var keyword = Current.Text;
        if (_annotations?.CanAccept(keyword) != true)
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
        if (Current.Kind != TokenKind.Identifier || Peek(1).Kind != TokenKind.LParen)
            return false;

        var firstArg = Peek(2).Kind;
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
        if (Current.Kind == TokenKind.Identifier &&
            string.Equals(Current.Text, "prev", StringComparison.Ordinal)) {
            throw Error("'prev' is no longer supported. Stage hierarchy has been removed; all stages are flat.");
        }

        changes.Add(new AddStageChange(_currentEntityName, name));
        // Duplicate stage names previously surfaced as an opaque catalog
        // ArgumentException ("same key already added") — fail loud at parse instead.
        if (!_entityStageNames.TryGetValue(_currentEntityName, out var stages)) {
            stages = new HashSet<string>(StringComparer.Ordinal);
            _entityStageNames[_currentEntityName] = stages;
        }
        if (!stages.Add(name))
            throw Error($"Duplicate stage name '{name}' on entity '{_currentEntityName}'.");

        // Stage require clauses sit before '{', like an action's (resolved after the entity body).
        while (Current.Kind == TokenKind.Require) {
            var requireToken = Current;
            Advance(); // consume 'require'
            var negated = false;
            if (Current.Kind == TokenKind.Not) {
                negated = true;
                Advance();
            }
            foreach (var policy in ParseIdentifierList())
                _pendingStageRequires.Add(new PendingStageRequire(name, policy, negated, requireToken));
        }
        Expect(TokenKind.LBrace);

        // P2.4: Parse entry/exit effect blocks before actions and subscriptions
        bool parsedEntry = false;
        bool parsedExit = false;

        while (Current.Kind != TokenKind.RBrace) {
            var match = MatchRule("stage-body");
            if (match is not null) {
                switch (match.PatternName) {
                    case "entry":
                        if (parsedEntry)
                            throw Error($"'{Current.Text}' must appear at the beginning of the stage block, before actions and subscriptions.");
                        parsedEntry = true;
                        Advance(); // consume 'entry'
                        Expect(TokenKind.LBrace);
                        while (Current.Kind != TokenKind.RBrace) {
                            var effect = ParseEffect();
                            changes.Add(new AddOnEntryEffectToStageChange(_currentEntityName, name, effect));
                        }
                        Expect(TokenKind.RBrace);
                        continue;

                    case "exit":
                        if (parsedExit)
                            throw Error($"'{Current.Text}' must appear at the beginning of the stage block, before actions and subscriptions.");
                        parsedExit = true;
                        Advance(); // consume 'exit'
                        Expect(TokenKind.LBrace);
                        while (Current.Kind != TokenKind.RBrace) {
                            var effect = ParseEffect();
                            changes.Add(new AddOnExitEffectToStageChange(_currentEntityName, name, effect));
                        }
                        Expect(TokenKind.RBrace);
                        continue;

                    case "subscription":
                        // Subscription: when RelName TargetStage { ... }
                        ParseSubscription(name, changes);
                        continue;

                    case "marked-stage-action":
                        Expect(TokenKind.Public);
                        Expect(TokenKind.Action);
                        var markedName = ExpectIdentifier(TokenKind.Identifier, "action name");
                        ParseActionBody(markedName, changes, name, preParsedParams: null, Visibility.Public);
                        continue;
                }
            }

            // Stage-local action (or unmodeled token — fail closed).
            if (Current.Kind == TokenKind.Require)
                throw Error("'require' must appear before the stage's '{', as in 'Name: stage require Policy {'.");
            if ((Current.Kind == TokenKind.Entry || Current.Kind == TokenKind.Exit) && Current.Kind != TokenKind.Identifier) {
                throw Error($"'{Current.Text}' must appear at the beginning of the stage block, before actions and subscriptions.");
            }
            var actionName = ExpectIdentifier(TokenKind.Identifier, "action name");
            // Stage members also use Name: kind. Legacy Name(params): action accepted.
            List<(string Name, string TypeName)>? stageActionParams = null;
            if (Current.Kind == TokenKind.LParen)
                stageActionParams = ParseActionParameterList();
            Expect(TokenKind.Colon);
            ParseActionBody(actionName, changes, name, stageActionParams);
        }

        Expect(TokenKind.RBrace);
    }

    /// <summary>
    /// The optional visibility slot. Absent means <see cref="Visibility.Internal"/>.
    /// The grammar rule is the plug point for a later level.
    /// </summary>
    private bool TryTakeVisibility(out Visibility visibility) {
        var match = MatchRule("visibility");
        if (match is null) {
            visibility = Visibility.Internal;
            return false;
        }
        visibility = match.PatternName switch {
            "public" => Visibility.Public,
            _ => throw Error($"Unknown visibility '{match.PatternName}'."),
        };
        Consume(match);
        return true;
    }

    /// <summary><c>public entity Name</c> or <c>public value Name</c>. The trailing mark is not accepted.</summary>
    private void ParseMarkedTop(List<DomainChange> changes, Visibility visibility) {
        if (Current.Kind == TokenKind.Entity) {
            Advance();
            var name = ExpectIdentifier(TokenKind.Identifier, "entity name");
            ParseEntityAfterKind(changes, name, visibility);
            return;
        }
        if (DslGrammar.IsValueWord(Current)) {
            Advance();
            var name = ExpectIdentifier(TokenKind.Identifier, "value type name");
            FinishValueType(changes, name, visibility);
            return;
        }
        throw Error($"Expected 'entity' or 'value' after '{visibility.ToString().ToLowerInvariant()}', got '{Current.Text}'");
    }

    private void ParseStandaloneAction(string name, List<DomainChange> changes) {
        // standalone action declared at entity level
        if (Current.Kind == TokenKind.Action)
            Advance(); // consume optional 'action'

        ParseActionBody(name, changes, stageName: null, preParsedParams: null, Visibility.Internal);
    }

    /// <summary>
    /// Parses <c>(name: Type, ...)</c>. Canonical placement is after the kind:
    /// <c>Name: action (params)</c>. Also used by legacy <c>Name(params): action</c>.
    /// </summary>
    private List<(string Name, string TypeName)> ParseActionParameterList() {
        Expect(TokenKind.LParen);
        var list = new List<(string, string)>();
        while (Current.Kind != TokenKind.RParen) {
            var paramName = ExpectIdentifier(TokenKind.Identifier, "parameter name");
            Expect(TokenKind.Colon);
            var paramType = ParseTypeName();
            list.Add((paramName, paramType));
            if (Current.Kind == TokenKind.Comma)
                Advance();
        }
        Expect(TokenKind.RParen);
        return list;
    }

    private void ParseActionBody(
        string actionName,
        List<DomainChange> changes,
        string? stageName,
        List<(string Name, string TypeName)>? preParsedParams,
        Visibility visibility = Visibility.Internal) {
        // Optional 'action' keyword. The marked form already consumed it.
        if (Current.Kind == TokenKind.Action)
            Advance();

        // Canonical: Name: action (params) -> RetType [require …] { … }
        // Params immediately after the kind keep Name: kind uniform.
        var paramList = preParsedParams;
        if (paramList is null && Current.Kind == TokenKind.LParen)
            paramList = ParseActionParameterList();

        // Optional return type: -> TypeName
        InvocationResult? actionResult = null;
        if (Current.Kind == TokenKind.Arrow) {
            Advance(); // consume ->
            var returnTypeName = ParseTypeName();
            actionResult = new InvocationResult([
                new InvocationResult.Member("Result",
                    new DomainTypeReference(returnTypeName), [])
            ]);
        }

        // Stage gates and require policies (collected — resolved after entity body)
        while (Current.Kind == TokenKind.When || Current.Kind == TokenKind.Require) {
            if (Current.Kind == TokenKind.When) {
                Advance(); // consume 'when'
                // Stage gates are not runtime-enforced in Phase 1a (BR.3.2).
                // Silently consume the gate names so the parser advances past them.
                ParseIdentifierList();
            }
            else {
                Advance(); // consume 'require'
                bool negated = false;
                if (Current.Kind == TokenKind.Not) {
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
            changes.Add(new AddActionToStageChange(_currentEntityName, stageName, actionName, visibility));
        }
        else {
            changes.Add(new AddActionChange(_currentEntityName, actionName, visibility));
        }

        if (paramList is not null) {
            foreach (var (paramName, paramType) in paramList) {
                changes.Add(new AddParameterToActionChange(
                    _currentEntityName, actionName,
                    new Property(paramName, new DomainTypeReference(paramType), []),
                    StageName: stageName));
            }
        }

        if (actionResult is not null) {
            changes.Add(new SetActionResultChange(
                _currentEntityName, actionName, actionResult, StageName: stageName));
        }

        Expect(TokenKind.LBrace);

        // Parse effects
        var effects = new List<Effect>();
        while (Current.Kind != TokenKind.RBrace) {
            effects.Add(ParseEffect());
        }

        foreach (var e in effects) {
            changes.Add(new AddEffectToActionChange(_currentEntityName, actionName, e, StageName: stageName));
        }

        Expect(TokenKind.RBrace);
    }

    private Effect ParseEffect() {
        var match = MatchRule("effect");
        if (match is null) {
            // F7: 'when' must stay rejected inside effect bodies (subscriptions
            // are stage-level; no 'when' pattern exists under "effect").
            if (Current.Kind == TokenKind.When) {
                throw Error("Unexpected 'when' inside action body (subscriptions are stage-level)");
            }

            if (Current.Kind == TokenKind.Identifier && _unsupportedKeywords.Contains(Current.Text)) {
                throw new FormatException(
                    $"'{Current.Text}' is not supported in Phase 1a");
            }

            // N4 (DX only): the head keyword matched but its tail pattern
            // failed — fold the tail expectation into the error.
            var tailHint = Current.Kind switch {
                TokenKind.Transition => " — expected 'to <stage>'",
                TokenKind.Assign => " — expected '<property> to'",
                TokenKind.Create => " — expected '<type> { … }' or 'in <relationship> { … }'",
                TokenKind.If => " — expected '(condition)'",
                _ => "",
            };
            throw Error($"Expected effect (transition, assign, create, invoke, for, if){tailHint}, got '{Current.Text}'");
        }

        switch (match.PatternName) {
            case "transition": {
                    var target = match.Tokens[2].Text;
                    Consume(match);
                    return new StageTransitionEffect(new StageReference(target));
                }

            case "assign": {
                    var propName = match.Tokens[1].Text;
                    Consume(match);
                    var expr = ParseExpression();
                    return new AssignEffect(DomainExpression.Property(propName), expr);
                }

            case "create-in": {
                    var relationshipName = match.Tokens[2].Text;
                    Consume(match);
                    Expect(TokenKind.LBrace);
                    var initializers = ParsePropertyInitializers();
                    return new CreateEntityInRelationshipEffect(relationshipName, initializers);
                }

            case "create": {
                    var entityTypeName = match.Tokens[1].Text;
                    Consume(match);
                    Expect(TokenKind.LBrace);
                    var initList = ParsePropertyInitializers();
                    return new CreateEntityInstance(
                        new DomainTypeReference(entityTypeName),
                        initList,
                        null);
                }

            case "invoke":
                Consume(match);
                return ParseInvokeEffectTail();

            case "for":
                if (_forBodyDepth > 0)
                    throw Error("A 'for' inside a 'for' body is not supported");
                Consume(match);
                return ParseForEffect();

            case "if":
                // Head is `if (` — the condition and then/else bodies are parsed
                // by the handler (B1: the pattern never spans IR-bearing bodies).
                Consume(match);
                return ParseIfEffectCore();

            default:
                throw Error($"Unhandled effect pattern '{match.PatternName}'");
        }
    }

    /// <summary>
    /// Parses <c>if (cond) { … } [else if (cond) { … }]* [else { … }]</c>.
    /// Called with the cursor at the condition (the <c>if (</c> head already
    /// consumed). Chains of <c>else if</c> lower to nested
    /// <see cref="ConditionalEffect"/> nodes.
    /// </summary>
    private Effect ParseIfEffectCore() {
        var condition = ParseExpression();
        Expect(TokenKind.RParen);
        Expect(TokenKind.LBrace);
        var thenEffects = new List<Effect>();
        while (Current.Kind != TokenKind.RBrace)
            thenEffects.Add(ParseEffect());
        Expect(TokenKind.RBrace);

        List<Effect>? elseEffects = null;
        if (Current.Kind == TokenKind.Else) {
            Advance(); // consume 'else'
            if (Current.Kind == TokenKind.If) {
                // else if → nest another ConditionalEffect as the sole else branch
                elseEffects = [ParseEffect()];
            }
            else {
                Expect(TokenKind.LBrace);
                elseEffects = new List<Effect>();
                while (Current.Kind != TokenKind.RBrace)
                    elseEffects.Add(ParseEffect());
                Expect(TokenKind.RBrace);
            }
        }

        return new ConditionalEffect(condition, thenEffects, elseEffects);
    }

    /// <summary>
    /// Parses the invoke tail after the <c>invoke</c> keyword:
    /// <c>[RelName.]ActionName(args)</c> (E3a/E3b). Singular cross-entity (OneToOne)
    /// only; OneToMany fan-out uses the <c>for</c> effect.
    /// </summary>
    private Effect ParseInvokeEffectTail() {
        var firstId = ExpectIdentifier(TokenKind.Identifier, "action or relationship name");

        string? targetRelationship = null;
        string actionName;

        if (Current.Kind == TokenKind.Dot) {
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
        if (Current.Kind == TokenKind.LParen) {
            Advance(); // consume '('
            while (Current.Kind != TokenKind.RParen) {
                var paramName = ExpectIdentifier(TokenKind.Identifier, "parameter name");
                Expect(TokenKind.Colon);
                var paramExpr = ParseExpression();
                bindings.Add(new PropertyBinding(paramName, paramExpr));
                if (Current.Kind == TokenKind.Comma)
                    Advance(); // consume ','
            }
            Expect(TokenKind.RParen);
        }

        return new InvokeActionEffect(actionName, bindings, targetRelationship);
    }

    private int _forBodyDepth;

    /// <summary>
    /// Parses the general <c>for x in Rel [where cond] { effects }</c> (#409), with the cursor
    /// at the binder. The collection is a relationship name; the optional <c>where</c> is an
    /// inline expression; the body is any effects. A nested <c>for</c> is rejected (no bound
    /// story for nested fan-out yet).
    /// </summary>
    private Effect ParseGeneralForEffect() {
        var binderName = ExpectIdentifier(TokenKind.Identifier, "binder name");
        Expect(TokenKind.In);
        if (Current.Kind == TokenKind.Identifier &&
            string.Equals(Current.Text, "where", StringComparison.OrdinalIgnoreCase))
            throw Error($"Expected relationship name after 'for {binderName} in', got 'where'");
        var relName = ExpectIdentifier(TokenKind.Identifier, "relationship name");

        DomainExpression? where = null;
        if (Current.Kind == TokenKind.Identifier &&
            string.Equals(Current.Text, "where", StringComparison.OrdinalIgnoreCase)) {
            Advance(); // consume 'where'
            where = ParseExpression();
        }

        if (Current.Kind != TokenKind.LBrace)
            throw Error($"Expected '{{' to open the 'for' body, got '{Current.Text}'");
        Advance();

        var body = new List<Effect>();
        _forBodyDepth++;
        try {
            while (Current.Kind != TokenKind.RBrace)
                body.Add(ParseEffect());
        }
        finally {
            _forBodyDepth--;
        }
        Expect(TokenKind.RBrace);

        return new ForEachEffect(binderName, relName, where, body);
    }

    /// <summary>
    /// Parses the <c>for</c> effect:
    /// <c>for Rel as name [where name.Policy | where name in Stage] invoke name.Action(args)</c>.
    /// Iterates every record reachable via the OneToMany relationship and invokes the action
    /// on each (fail-fast, zero-matches fail). The predicate is a named policy or stage
    /// membership on the target entity — never an inline expression.
    /// </summary>
    private Effect ParseForEffect() {
        if (Current.Kind == TokenKind.Identifier) {
            var next = Peek(1).Kind;
            if (next == TokenKind.In)
                return ParseGeneralForEffect();
            if (next != TokenKind.As) {
                Advance();
                throw Error(
                    "Expected 'in' (for x in Rel { … }) or 'as' (for Rel as x invoke …) after the 'for' name, " +
                    $"got '{Current.Text}'");
            }
        }

        var relName = ExpectIdentifier(TokenKind.Identifier, "relationship name");
        Expect(TokenKind.As);
        var binderName = ExpectIdentifier(TokenKind.Identifier, "binder name (as name)");

        ForEachPredicate? predicate = null;
        if (Current.Kind == TokenKind.Identifier &&
            string.Equals(Current.Text, "where", StringComparison.OrdinalIgnoreCase)) {
            Advance(); // consume 'where'
            var subject = ExpectIdentifier(TokenKind.Identifier, "binder name in predicate");
            if (!string.Equals(subject, binderName, StringComparison.Ordinal))
                throw Error($"'for' predicate subject must be the binder '{binderName}', got '{subject}'");
            // Space-separated path-prefix convention (like `order Code`): `line IsPaid`
            // = named policy on the target; `line in Active` = stage membership.
            if (Current.Kind == TokenKind.In) {
                Advance();
                var stageName = ExpectIdentifier(TokenKind.Identifier, "stage name");
                predicate = new ForEachStageMembership(stageName);
            }
            else if (Current.Kind == TokenKind.Identifier) {
                var policyName = Current.Text;
                Advance();
                predicate = new ForEachNamedPolicy(policyName);
            }
            else {
                throw Error("'where' in a 'for' must be a named policy (name Policy) or stage membership (name in Stage)");
            }
        }

        Expect(TokenKind.Invoke);
        var target = ExpectIdentifier(TokenKind.Identifier, "binder name");
        if (!string.Equals(target, binderName, StringComparison.Ordinal))
            throw Error($"'for' invoke target must be the binder '{binderName}', got '{target}'");
        Expect(TokenKind.Dot);
        var actionName = ExpectIdentifier(TokenKind.Identifier, "action name");

        var bindings = new List<PropertyBinding>();
        if (Current.Kind == TokenKind.LParen) {
            Advance(); // consume '('
            while (Current.Kind != TokenKind.RParen) {
                var paramName = ExpectIdentifier(TokenKind.Identifier, "parameter name");
                Expect(TokenKind.Colon);
                var paramExpr = ParseExpression();
                bindings.Add(new PropertyBinding(paramName, paramExpr));
                if (Current.Kind == TokenKind.Comma)
                    Advance(); // consume ','
            }
            Expect(TokenKind.RParen);
        }

        return new ForEachInvokeEffect(relName, binderName, predicate, actionName, bindings);
    }

    /// <summary>
    /// A property name is an identifier or a primitive type keyword (<c>Text</c>,
    /// <c>Number</c>, <c>Boolean</c>), the same words the entity declaration accepts.
    /// </summary>
    private string ExpectPropertyName() {
        if (Current.Kind == TokenKind.Identifier || DslGrammar.IsPrimitiveTypeKind(Current.Kind)) {
            var name = Current.Text;
            Advance();
            return name;
        }
        throw Error($"Expected property name, got '{Current.Text}'");
    }

    private List<PropertyBinding> ParsePropertyInitializers() {
        var initializers = new List<PropertyBinding>();
        while (Current.Kind != TokenKind.RBrace) {
            var propName = ExpectPropertyName();
            Expect(TokenKind.Colon);
            // A bare-identifier value followed by the next initializer (`Name: newName
            // Content: ...`) must not be consumed as a path-prefix — the expression
            // parser stops path continuation at an `Identifier :` boundary in this mode.
            InPropertyInitializerValue = true;
            DomainExpression expr;
            try {
                expr = ParseExpression();
            }
            finally {
                InPropertyInitializerValue = false;
            }
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
        while (Current.Kind == TokenKind.Comma) {
            Advance(); // consume ','
            targetStages.Add(ExpectIdentifier(TokenKind.Identifier, "target stage name"));
        }
        // Optional peer binder: when Rel Stage as name { … }
        string? peerBinding = null;
        if (Current.Kind == TokenKind.As) {
            Advance();
            peerBinding = ExpectIdentifier(TokenKind.Identifier, "peer binding name");
        }
        Expect(TokenKind.LBrace);

        var effects = new List<Effect>();
        while (Current.Kind != TokenKind.RBrace) {
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
        if (Current.Kind == TokenKind.Identifier &&
            string.Equals(Current.Text, "any", StringComparison.OrdinalIgnoreCase)) {
            Advance(); // consume 'any'
            return StageSubscriptionQuantifier.Any;
        }
        if (Current.Kind == TokenKind.Identifier &&
            string.Equals(Current.Text, "all", StringComparison.OrdinalIgnoreCase)) {
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
        while (Current.Kind == TokenKind.Comma) {
            Advance(); // consume ','
            targetStages.Add(ExpectIdentifier(TokenKind.Identifier, "target stage name"));
        }
        string? peerBinding = null;
        if (Current.Kind == TokenKind.As) {
            Advance();
            peerBinding = ExpectIdentifier(TokenKind.Identifier, "peer binding name");
        }
        Expect(TokenKind.LBrace);

        var effects = new List<Effect>();
        while (Current.Kind != TokenKind.RBrace) {
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
            var name = Current.Text;
            var typeWord = Peek(2);
            if (typeWord.Kind == TokenKind.Identifier && _unsupportedKeywords.Contains(typeWord.Text)) {
                return new FormatException(
                    $"'{typeWord.Text}' is not supported in Phase 1a (use 'entity' instead)");
            }
            if (typeWord.Kind == TokenKind.Identifier
                && Peek(3).Kind == TokenKind.Entity) {
                return new FormatException(
                    $"Entity inheritance ('{name}: {typeWord.Text} entity') is no longer supported. " +
                    $"Define '{typeWord.Text}' properties directly on '{name}'.");
            }
        }
        return Error($"Expected 'entity', 'enum', 'value', 'contract', 'function', or 'bind' definition, got '{Current.Text}'");
    }

    /// <summary>
    /// Top-level <c>Name: value|contract|bind</c> — words stay identifiers so
    /// <c>action (value: Text)</c> does not collide with a reserved keyword.
    /// </summary>
    private bool TryParseNamedTopLevel(List<DomainChange> changes) {
        if (Current.Kind != TokenKind.Identifier || !PeekIs(TokenKind.Colon))
            return false;
        var kindTok = Peek(2);
        if (kindTok.Kind != TokenKind.Identifier)
            return false;
        switch (kindTok.Text) {
            case "value":
                ParseValueType(changes);
                return true;
            case "contract":
                ParseContract(changes);
                return true;
            case "bind":
                ParseBind(changes);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Parses a top-level enum type declaration: <c>Name: enum { Member1, Member2 }</c>.
    /// Enum types must be declared before entities that reference them.
    /// </summary>
    private void ParseEnumType(List<DomainChange> changes) {
        var name = ExpectIdentifier(TokenKind.Identifier, "enum type name");
        Expect(TokenKind.Colon);
        Expect(TokenKind.Enum);

        var typeFacets = new List<Facet>();
        var typeTermKeywords = new HashSet<string>(StringComparer.Ordinal);
        while (TryParseRegisteredAnnotation(out var headerFacet)) {
            ValidateTermFacet(headerFacet, typeTermKeywords, $"enum '{name}'", allowTerm: false);
            typeFacets.Add(headerFacet);
        }

        Expect(TokenKind.LBrace);

        var members = new List<string>();
        var memberFacets = new Dictionary<string, IReadOnlyList<Facet>>(StringComparer.Ordinal);
        while (Current.Kind == TokenKind.Identifier) {
            var member = Current.Text;
            members.Add(member);
            Advance();

            // Per-member term metadata: `Red iri("...") term("red")`.
            var facets = new List<Facet>();
            var memberTermKeywords = new HashSet<string>(StringComparer.Ordinal);
            while (Current.Kind == TokenKind.Identifier && PeekIs(TokenKind.LParen)) {
                var keyword = Current.Text;
                if (!TryParseRegisteredAnnotation(out var facet)) {
                    throw Error(
                        $"Unknown or unregistered annotation '{keyword}' on enum member '{member}'. " +
                        "Enum members accept only iri, term, label, or description.");
                }
                if (facet is not Annotation annotation || !TermFacets.IsTermKeyword(annotation.Name)) {
                    throw Error(
                        $"Only term annotations (iri, term, label, description) are allowed " +
                        $"on enum member '{member}'.");
                }
                ValidateTermFacet(facet, memberTermKeywords, $"enum member '{member}'", allowTerm: true);
                facets.Add(facet);
            }
            if (facets.Count > 0)
                memberFacets[member] = facets;

            if (Current.Kind == TokenKind.Comma)
                Advance();
        }

        // Enum members must be identifiers — a member named after a DSL keyword
        // (e.g. `Number`, `Text`, `Create`, `In`, `Stage`, `Entry`) lexes as that
        // keyword token and would otherwise fail cryptically on the RBrace expect
        // below. A letter-led token here that is not a valid member terminator is
        // necessarily a reserved word (symbols like `,`/`:` fall through to the
        // normal RBrace error; EndOfFile has no text).
        if (Current.Kind is not (TokenKind.RBrace or TokenKind.Comma or TokenKind.EndOfFile)
            && Current.Text.Length > 0
            && char.IsLetter(Current.Text[0])) {
            var word = Current.Text;
            throw Error(
                $"Enum member '{word}' is a reserved keyword and cannot be used as an enum member. " +
                "Rename it (e.g. 'Number' → 'Numeric').");
        }

        Expect(TokenKind.RBrace);

        _enumTypeNames.Add(name);
        changes.Add(new AddEnumTypeChange(
            name,
            members,
            memberFacets.Count > 0 ? memberFacets : null,
            typeFacets.Count > 0 ? typeFacets : null));
    }

    /// <summary>
    /// <c>Name: value { Prop: Type … }</c>. Value types must be declared before
    /// entities that reference them.
    /// </summary>
    private void ParseValueType(List<DomainChange> changes) {
        var name = ExpectIdentifier(TokenKind.Identifier, "value type name");
        Expect(TokenKind.Colon);
        ExpectWord("value");
        FinishValueType(changes, name, Visibility.Internal);
    }

    /// <summary>
    /// A top-level <c>function</c> declaration starts with the word and is not the
    /// legacy <c>function: entity</c> name form. A malformed <c>function (…)</c>
    /// head still enters the parser so the error names the missing function name.
    /// </summary>
    private bool IsFunctionDeclarationStart() =>
        Current.Kind == TokenKind.Identifier
        && string.Equals(Current.Text, "function", StringComparison.Ordinal)
        && !PeekIs(TokenKind.Colon);

    /// <summary>
    /// <c>function Name(p: T, …): T = expr</c> — one expression body. Parameter names
    /// bind inside the body as <see cref="ParameterAccess"/>, so parse → print →
    /// parse stays lossless beyond the text.
    /// </summary>
    private void ParseFunction(List<DomainChange> changes) {
        Advance(); // 'function'
        if (Current.Kind != TokenKind.Identifier)
            throw Error($"Expected function name after 'function', got '{Current.Text}'");
        var name = Current.Text;
        Advance();

        Expect(TokenKind.LParen);
        var parameters = new List<Property>();
        while (Current.Kind != TokenKind.RParen) {
            var parameterName = ExpectIdentifier(TokenKind.Identifier, "parameter name");
            Expect(TokenKind.Colon);
            var parameterType = ParseTypeName();
            parameters.Add(new Property(parameterName, new DomainTypeReference(parameterType), []));
            if (Current.Kind == TokenKind.Comma) {
                Advance();
                if (Current.Kind == TokenKind.RParen)
                    throw Error($"Trailing comma in function '{name}' parameter list");
            }
            else if (Current.Kind != TokenKind.RParen) {
                throw Error($"Expected ',' or ')' in function '{name}' parameter list, got '{Current.Text}'");
            }
        }
        Expect(TokenKind.RParen);
        Expect(TokenKind.Colon);
        var returnType = new DomainTypeReference(ParseTypeName());
        if (Current.Kind != TokenKind.SingleEq)
            throw Error($"Expected '=' after function '{name}' return type, got '{Current.Text}'");
        Advance();

        var parameterNames = parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var body = new ParameterBinder(parameterNames).Route(ParseExpression());
        changes.Add(new AddFunctionChange(name, parameters, returnType, body));
    }

    /// <summary>Rewrites body identifiers that name a declared parameter to parameter access.</summary>
    private sealed class ParameterBinder(ISet<string> parameterNames) : DomainExpressionRewriteBase {
        protected override DomainExpression PropertyAccess(PropertyAccess e) =>
            parameterNames.Contains(e.Name) ? DomainExpression.Parameter(e.Name) : e;
    }

    private void FinishValueType(List<DomainChange> changes, string name, Visibility visibility) {
        var properties = ParseValueTypeProperties();
        _valueTypeNames.Add(name);
        changes.Add(new AddValueTypeChange(name, properties, visibility));
    }

    private IReadOnlyList<Property> ParseValueTypeProperties() {
        Expect(TokenKind.LBrace);
        var properties = new List<Property>();
        while (Current.Kind != TokenKind.RBrace && Current.Kind != TokenKind.EndOfFile) {
            var propName = ExpectPropertyName();
            Expect(TokenKind.Colon);
            string typeName;
            if (IsPrimitiveType(Current.Kind)) {
                typeName = Current.Text;
                Advance();
            }
            else {
                typeName = ExpectIdentifier(TokenKind.Identifier, "type name");
            }
            var constraints = new List<Constraint>();
            while (IsConstraint(Current.Kind)) {
                if (Current.Kind == TokenKind.Optional) {
                    throw Error(
                        "'optional' applies to entity properties; " +
                        "value-type members are contract payload fields and are always required.");
                }
                var c = ParseConstraint(typeName);
                if (c is not null)
                    constraints.Add(c);
            }
            RejectRetiredRequiredWord();
            properties.Add(new Property(propName, new DomainTypeReference(typeName), constraints));
        }
        Expect(TokenKind.RBrace);
        return properties;
    }

    /// <summary>
    /// <c>Name: contract external|internal source version { Endpoint: inbound|outbound operation|event Type }</c>
    /// </summary>
    private void ParseContract(List<DomainChange> changes) {
        var nameToken = Current;
        var name = ExpectIdentifier(TokenKind.Identifier, "contract name");
        if (_seededContracts.TryGetValue(name, out var seededBy))
            throw GrammarError.Error(
                $"Contract '{name}' is contributed by `uses {seededBy}`. A hand-written contract block beside the seeded surface is a duplicate.",
                nameToken.Line, nameToken.Col);
        if (!_authoredContracts.Add(name))
            throw GrammarError.Error(
                $"Contract '{name}' is already declared. One contract block per name.",
                nameToken.Line, nameToken.Col);
        Expect(TokenKind.Colon);
        ExpectWord("contract");

        ContractSourceKind sourceKind;
        if (Current.Kind == TokenKind.Identifier && Current.Text == "external") {
            Advance();
            sourceKind = ContractSourceKind.ExternalProvider;
        }
        else if (Current.Kind == TokenKind.Identifier && Current.Text == "internal") {
            Advance();
            sourceKind = ContractSourceKind.InternalDomain;
        }
        else {
            throw Error($"Expected 'external' or 'internal' after contract '{name}'");
        }

        string sourceId;
        if (Current.Kind == TokenKind.StringLiteral) {
            sourceId = Current.Text;
            Advance();
        }
        else {
            sourceId = ExpectIdentifier(TokenKind.Identifier, "contract source");
        }

        string version;
        if (Current.Kind == TokenKind.StringLiteral) {
            version = Current.Text;
            Advance();
        }
        else {
            version = ExpectIdentifier(TokenKind.Identifier, "contract version");
        }

        changes.Add(new AddImportedContractChange(name, sourceKind, sourceId, version));
        Expect(TokenKind.LBrace);

        // Contract bodies declare value types that reference primitives (e.g. `Amount: Number`),
        // so the built-in primitive types must be ensured even when the domain has no entity
        // (a pure ACL / used-sub-domain domain). Mirrors ParseEntity's EnsurePrimitivesOnce.
        EnsurePrimitivesOnce(changes);

        while (Current.Kind != TokenKind.RBrace && Current.Kind != TokenKind.EndOfFile) {
            if (Current.Kind == TokenKind.Identifier
                && PeekIs(TokenKind.Colon)
                && Peek(2).Kind == TokenKind.Identifier
                && string.Equals(Peek(2).Text, "value", StringComparison.Ordinal)) {
                var vtName = ExpectIdentifier(TokenKind.Identifier, "value type name");
                Expect(TokenKind.Colon);
                ExpectWord("value");
                var properties = ParseValueTypeProperties();
                _valueTypeNames.Add(vtName);
                changes.Add(new AddContractValueTypeChange(name, new ValueType(vtName, properties, [])));
                continue;
            }

            var endpointName = ExpectIdentifier(TokenKind.Identifier, "endpoint name");
            Expect(TokenKind.Colon);

            ContractEndpointDirection direction;
            if (Current.Kind == TokenKind.Identifier && Current.Text == "inbound") {
                Advance();
                direction = ContractEndpointDirection.Inbound;
            }
            else if (Current.Kind == TokenKind.Identifier && Current.Text == "outbound") {
                Advance();
                direction = ContractEndpointDirection.Outbound;
            }
            else {
                throw Error($"Expected 'inbound' or 'outbound' on endpoint '{endpointName}'");
            }

            ContractEndpointKind kind;
            if (Current.Kind == TokenKind.Identifier && Current.Text == "operation") {
                Advance();
                kind = ContractEndpointKind.Operation;
            }
            else if (Current.Kind == TokenKind.Identifier && Current.Text == "event") {
                Advance();
                kind = ContractEndpointKind.Event;
            }
            else {
                throw Error($"Expected 'operation' or 'event' on endpoint '{endpointName}'");
            }

            string payloadType;
            if (IsPrimitiveType(Current.Kind)) {
                payloadType = Current.Text;
                Advance();
            }
            else {
                payloadType = ExpectIdentifier(TokenKind.Identifier, "payload type");
            }

            changes.Add(new AddContractEndpointChange(name,
                new ContractEndpoint(endpointName, kind, direction, new DomainTypeReference(payloadType))));
        }

        Expect(TokenKind.RBrace);
    }

    /// <summary>
    /// <c>Name: bind Contract Endpoint to Action param</c>
    /// </summary>
    private void ParseBind(List<DomainChange> changes) {
        var name = ExpectIdentifier(TokenKind.Identifier, "binding name");
        Expect(TokenKind.Colon);
        ExpectWord("bind");
        var contractName = ExpectIdentifier(TokenKind.Identifier, "contract name");
        var endpointName = ExpectIdentifier(TokenKind.Identifier, "endpoint name");
        Expect(TokenKind.To);
        var actionName = ExpectIdentifier(TokenKind.Identifier, "action name");
        var paramName = ExpectIdentifier(TokenKind.Identifier, "action parameter");
        changes.Add(new AddContractBindingChange(name, contractName, endpointName, actionName, paramName));
    }

    private bool IsNavLine() {
        // TokenKind.Many and TokenKind.One are unambiguous nav starts
        if (Current.Kind == TokenKind.Many || Current.Kind == TokenKind.One)
            return true;

        // "owned" as the first token after : → nav (must be followed by TypeName)
        if (Current.Kind == TokenKind.Owned)
            return true;

        // Bare identifier that isn't a primitive type, keyword, or reserved construct
        if (Current.Kind == TokenKind.Identifier && !IsPrimitiveType(Current.Kind)) {
            var text = Current.Text;
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
        if (Current.Kind == TokenKind.Many) {
            cardinality = RelationshipCardinality.OneToMany;
            Advance();
        }
        else if (Current.Kind == TokenKind.One) {
            Advance(); // consume 'one'
        }

        // Check for optional 'owned'
        if (Current.Kind == TokenKind.Owned) {
            owned = true;
            Advance();
        }

        // Remaining identifier is the target type name
        // Must be an identifier (not a primitive type keyword)
        if (Current.Kind != TokenKind.Identifier) {
            var hint = IsPrimitiveType(Current.Kind)
                ? $" '{Current.Text}' is a primitive type, not an entity. Use a primitive property declaration instead."
                : $" unexpected token '{Current.Text}'";
            throw Error($"Navigation property '{name}' requires an entity type as target:{hint}");
        }
        var targetToken = Current;
        var targetType = ExpectIdentifier(TokenKind.Identifier, "target entity name");

        RejectRetiredRequiredWord();
        _pendingNavs.Add(new PendingNav(_currentEntityName, name, targetType, cardinality, owned, targetToken));
    }

    /// <summary>
    /// Resolves all pending navigation properties against known entity names.
    /// Called after all entities have been parsed. Errors if a target type is
    /// unknown or is a primitive type.
    /// </summary>
    private void ResolvePendingNavs(List<DomainChange> changes) {
        foreach (var nav in _pendingNavs) {
            if (IsKnownPrimitiveName(nav.TargetTypeName)) {
                throw GrammarError.Error($"Navigation property '{nav.PropertyName}': '{nav.TargetTypeName}' is a primitive type, not an entity. Use a primitive property declaration instead.", nav.At.Line, nav.At.Col);
            }
            if (!_entityNames.Contains(nav.TargetTypeName)) {
                // A bare type name here is a nav target, but the author may have meant a
                // value type or a value keyword (`Ref: Guid`). Name both possibilities.
                var known = string.Join(", ", _primitiveTypeNames.OrderBy(n => n, StringComparer.Ordinal));
                throw GrammarError.Error(
                    $"Property '{nav.PropertyName}' is declared with type '{nav.TargetTypeName}', which is "
                    + $"neither an entity in this domain nor a value type ({known}). "
                    + $"A value keyword such as 'Guid' or 'Now' is only valid inside an expression "
                    + $"(e.g. `default(Guid)`). To reference another domain, declare that entity.", nav.At.Line, nav.At.Col);
            }

            // Check property name collision
            if (_entityPropertyNames.TryGetValue(nav.SourceEntityName, out var props) && props.Contains(nav.PropertyName)) {
                throw GrammarError.Error($"Navigation property '{nav.PropertyName}' on '{nav.SourceEntityName}' conflicts with an existing property of the same name.", nav.At.Line, nav.At.Col);
            }

            // Check duplicate relationship name within the same source entity.
            // Relationship identity is (source entity, name) — the same nav name may
            // be declared on different source entities (e.g. back-references both
            // named 'order').
            if (!_relationshipNamesByEntity.TryGetValue(nav.SourceEntityName, out var names)) {
                _relationshipNamesByEntity[nav.SourceEntityName] = names = new HashSet<string>(StringComparer.Ordinal);
            }
            if (!names.Add(nav.PropertyName)) {
                throw GrammarError.Error($"Relationship '{nav.PropertyName}' is defined more than once on entity '{nav.SourceEntityName}'. Relationship names must be unique within their source entity.", nav.At.Line, nav.At.Col);
            }

            changes.Add(new AddRelationshipChange(
                nav.PropertyName,
                new DomainTypeReference(nav.SourceEntityName),
                new DomainTypeReference(nav.TargetTypeName),
                nav.Cardinality, [], nav.SourceOwnsTarget));
        }
        _pendingNavs.Clear();
    }

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

        while (Current.Kind != TokenKind.RParen) {
            if (Current.Kind == TokenKind.StringLiteral) {
                args[positionalIndex.ToString()] = new AnnotationString(Current.Text);
                Advance();
            }
            else if (Current.Kind == TokenKind.Number) {
                args[positionalIndex.ToString()] = new AnnotationNumber(
                    double.Parse(Current.Text, CultureInfo.InvariantCulture));
                Advance();
            }
            else if (Current.Kind == TokenKind.True) {
                args[positionalIndex.ToString()] = new AnnotationBool(true);
                Advance();
            }
            else if (Current.Kind == TokenKind.False) {
                args[positionalIndex.ToString()] = new AnnotationBool(false);
                Advance();
            }
            else if (Current.Kind == TokenKind.Null) {
                args[positionalIndex.ToString()] = new AnnotationNull();
                Advance();
            }
            else {
                throw Error($"Expected annotation argument (string, number, bool, null), got '{Current.Text}'");
            }

            positionalIndex++;

            if (Current.Kind == TokenKind.Comma) {
                Advance();
                if (Current.Kind == TokenKind.RParen) {
                    throw Error($"Trailing comma in annotation '{keyword}(...)' arguments");
                }
            }
            else if (Current.Kind != TokenKind.RParen) {
                throw Error(
                    $"Expected ',' or ')' in annotation '{keyword}(...)', got '{Current.Text}'");
            }
        }

        Expect(TokenKind.RParen);
        if (!TermFacets.IsWellFormed(keyword, args)) {
            throw Error(keyword == TermFacets.Iri
                ? "Annotation 'iri(...)' expects one absolute http(s) or urn IRI. " +
                  "A CURIE has no prefix declaration here; write the full IRI."
                : $"Annotation '{keyword}(...)' expects exactly one non-empty string argument.");
        }
        return new Annotation(keyword, args);
    }

    // ── Constraint parser ─────────────────────────────────────

    private Constraint? ParseConstraint(string? typeName = null) {
        switch (Current.Kind) {
            case TokenKind.Optional:
                Advance();
                return new OptionalConstraint();

            case TokenKind.Unique:
                Advance();
                return new UniqueConstraint();

            case TokenKind.Equals:
                Advance();
                Expect(TokenKind.LParen);
                var dvExpr = ParseExpression();
                Expect(TokenKind.RParen);
                return new DefaultValueConstraint(dvExpr);

            case TokenKind.Equality:
                Advance();
                Expect(TokenKind.LParen);
                var eqExpr = ParseExpression();
                Expect(TokenKind.RParen);
                if (eqExpr is not Literal { Value: { } expected })
                    throw Error("equals() requires a literal value.");
                return new EqualityConstraint(expected);

            case TokenKind.Enum:
                throw Error("Inline enum(...) constraints are no longer supported. " +
                    "Use a top-level enum type declaration: Name: enum { Member1, Member2 }");

            case TokenKind.Range:
                Advance();
                Expect(TokenKind.LParen);
                object? min = null, max = null;
                // Signed/fractional bounds: range(-500, ) / range(0.01, 1.0) — the
                // natural overdraft/pricing surface was previously unparseable.
                if (Current.Kind == TokenKind.Number || Current.Kind == TokenKind.Minus) {
                    min = ParseNumberBound(typeName);
                }
                if (Current.Kind == TokenKind.Comma) {
                    Advance();
                }
                if (Current.Kind == TokenKind.Number || Current.Kind == TokenKind.Minus) {
                    max = ParseNumberBound(typeName);
                }
                Expect(TokenKind.RParen);
                return new RangeConstraint(min, max);

            case TokenKind.Length:
                Advance();
                Expect(TokenKind.LParen);
                // Open bounds mirror range: `length(3, )` is open upper (int.MaxValue),
                // `length(, 5)` is open lower (0), `length(3)` is exact. A `length(3, )`
                // that collapsed to `length(3, 3)` silently rejected 4-char values.
                int lenMin = 0;
                int lenMax = int.MaxValue;
                if (Current.Kind == TokenKind.Number) {
                    lenMin = int.Parse(Current.Text, CultureInfo.InvariantCulture);
                    Advance();
                }
                if (Current.Kind == TokenKind.Comma) {
                    Advance();
                    if (Current.Kind == TokenKind.Number) {
                        lenMax = int.Parse(Current.Text, CultureInfo.InvariantCulture);
                        Advance();
                    }
                }
                else {
                    lenMax = lenMin; // single-arg form "length(3)" — exact length
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

    /// <summary>Parses a signed numeric bound for `range(min, max)` — e.g. `-500` or `0.01`.</summary>
    private object ParseNumberBound(string? typeName) {
        var negative = Current.Kind == TokenKind.Minus;
        if (negative) Advance();
        if (Current.Kind != TokenKind.Number)
            throw Error($"Expected a number in range bound, got '{Current.Text}'.");
        var text = Current.Text;
        if (IsIntegralRangeType(typeName) && !IsIntegralNumberText(text))
            throw Error($"Fractional bound '{text}' is not legal on {typeName}. Use Decimal.");
        if (typeName is "Decimal") {
            var dec = decimal.Parse(text, CultureInfo.InvariantCulture);
            Advance();
            return negative ? -dec : dec;
        }
        var value = double.Parse(text, CultureInfo.InvariantCulture);
        Advance();
        return negative ? -value : value;
    }

    private static bool IsIntegralRangeType(string? typeName) =>
        typeName is "Number" or "Int" or "Int64";

    private static bool IsIntegralNumberText(string text) {
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return true;
        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var dec)
            && dec == decimal.Truncate(dec);
    }

    private string ParseTypeName() {
        if (IsPrimitiveType(Current.Kind)) {
            var typeName = Current.Kind switch {
                TokenKind.Text => "Text",
                TokenKind.NumberType => "Number",
                TokenKind.BooleanType => "Boolean",
                _ => throw Error($"Unknown type '{Current.Kind}'"),
            };
            Advance();
            return typeName;
        }
        if (Current.Kind == TokenKind.Identifier) {
            return ExpectIdentifier(TokenKind.Identifier, "type name");
        }
        throw Error($"Expected a type name, got '{Current.Text}'");
    }

    private List<string> ParseIdentifierList() {
        var list = new List<string>();
        list.Add(ExpectIdentifier(TokenKind.Identifier, "identifier"));
        while (Current.Kind == TokenKind.Comma) {
            Advance();
            list.Add(ExpectIdentifier(TokenKind.Identifier, "identifier"));
        }
        return list;
    }

    private static bool IsPrimitiveType(TokenKind kind) => kind switch {
        TokenKind.Text or TokenKind.NumberType or TokenKind.BooleanType => true,
        _ => false,
    };

    private static bool IsConstraint(TokenKind kind) => kind switch {
        TokenKind.Optional or TokenKind.Unique or TokenKind.Range
            or TokenKind.Length or TokenKind.Pattern
            or TokenKind.Equals or TokenKind.Equality or TokenKind.Enum => true,
        _ => false,
    };

    /// <summary>
    /// <c>required</c> is not a constraint. A property is required unless it is marked
    /// <c>optional</c>. A member whose name is <c>required</c> stays legal
    /// (<c>required: Text</c>).
    /// </summary>
    private void RejectRetiredRequiredWord() {
        if (Current.Kind == TokenKind.Identifier
            && string.Equals(Current.Text, "required", StringComparison.Ordinal)
            && !PeekIs(TokenKind.Colon)) {
            throw Error(
                "'required' is not a constraint. A property is required unless it is marked 'optional'.");
        }
    }

    private static readonly HashSet<string> _unsupportedKeywords = new(StringComparer.OrdinalIgnoreCase) {
        "actor", "schedule", "parallel", "for", "function"
    };

    private void ExpectWord(string word) {
        if (Current.Kind != TokenKind.Identifier
            || !string.Equals(Current.Text, word, StringComparison.Ordinal))
            throw Error($"Expected '{word}', got '{Current.Text}'");
        Advance();
    }

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
}