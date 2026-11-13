using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.Grammar;

using TokenKind = Poly.DomainModeling.Language.DslTokenKind;

namespace Poly.DomainModeling.Language;

// DomainResolver (Entities) — entity declarations, entity-body members (properties, annotations) and entity-body require resolution.

public sealed partial class DomainResolver {
    // S-15: the current entity's authored member order, recorded at the body arms below.
    private readonly List<EntityMemberRef> _entityMembers = new();

    private void ParseEntity(List<DomainChange> changes) {
        var entityName = ExpectIdentifier(TokenKind.Identifier, "entity name");
        Expect(TokenKind.Colon);

        // The "top" rule guarantees this is 'entity'; unsupported-keyword and
        // removed-inheritance shapes never reach this method (TopLevelRejection).
        Expect(TokenKind.Entity);
        ParseEntityAfterKind(changes, entityName, Visibility.Internal);
    }

    private const string KeyKeyword = "key";

    private const string InitialKeyword = "initial";

    /// <summary>
    /// True at the header clause <c>initial(Stage)</c>. The word is contextual: only in header
    /// position and only when followed by <c>(</c>, so a stage or property named <c>initial</c>
    /// is unaffected.
    /// </summary>
    private bool IsInitialStageClause() =>
        Current.Kind == TokenKind.Identifier && Current.TextEquals(InitialKeyword) && PeekIs(TokenKind.LParen);

    /// <summary>
    /// Parses <c>initial(Stage)</c> after the header annotations and before <c>key(...)</c>. The stage
    /// name is resolved by analysis, not here; an empty or non-identifier clause fails closed.
    /// </summary>
    private string ParseInitialStageClause() {
        Advance(); // 'initial'
        Expect(TokenKind.LParen);
        var name = ExpectIdentifier(TokenKind.Identifier, "initial stage name");
        if (Current.Kind == TokenKind.Comma)
            throw Error("The 'initial(...)' clause names exactly one stage; found a comma.");
        Expect(TokenKind.RParen);
        return name;
    }

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
                throw Error($"Expected ',' or ')' in 'key(...)', got '{Shown(Current)}'");
            break;
        }
        Expect(TokenKind.RParen);
        return members;
    }

    private void ParseEntityAfterKind(List<DomainChange> changes, string entityName, Visibility visibility) {
        _currentEntityName = entityName;
        _entityPolicies.Clear();
        _entityMembers.Clear();
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
        // #675 initial-stage clause: after the annotations, before key(...) and '{'.
        string? initialStage = null;
        if (IsInitialStageClause())
            initialStage = ParseInitialStageClause();
        if (initialStage is not null)
            changes.Add(new SetEntityInitialStageChange(entityName, initialStage));
        // #405 key clause: the last header element, immediately before '{'.
        if (Current.Kind == TokenKind.Identifier && Current.TextEquals(KeyKeyword) && PeekIs(TokenKind.LParen)) {
            changes.Add(new SetEntityKeyChange(entityName, ParseKeyClause()));
        }
        if (IsInitialStageClause()) {
            // Declared before LooksLikeAnnotationCall so the clause is not reported as an unknown
            // annotation: placement and repetition are the real failures.
            if (initialStage is not null)
                throw Error($"Entity '{entityName}' declares more than one 'initial(...)' clause.");
            throw Error("The 'initial(...)' clause must come before 'key(...)'.");
        }
        if (LooksLikeAnnotationCall()) {
            throw Error(
                $"Unknown or unregistered annotation '{Shown(Current)}'. " +
                "Enable a pack that registers this keyword, or remove the annotation.");
        }
        if (Current.Kind == TokenKind.Identifier && Current.TextEquals(KeyKeyword) && PeekIs(TokenKind.LParen)) {
            throw Error($"Entity '{entityName}' declares more than one 'key(...)' clause.");
        }

        RejectRetiredRequiredWord();
        Expect(TokenKind.LBrace);

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
                            NoteMember(EntityMemberKind.Stage, name);
                            ParseStage(name, changes);
                            continue;
                        }

                    case "marked-action": {
                            Expect(TokenKind.Public);
                            Expect(TokenKind.Action);
                            var name = ExpectIdentifier(TokenKind.Identifier, "action name");
                            NoteMember(EntityMemberKind.Action, name);
                            ParseActionBody(name, changes, stageName: null, preParsedParams: null, Visibility.Public);
                            continue;
                        }

                    case "action": {
                            var name = ExpectIdentifier(TokenKind.Identifier, "action name");
                            Expect(TokenKind.Colon);
                            NoteMember(EntityMemberKind.Action, name);
                            ParseStandaloneAction(name, changes);
                            continue;
                        }

                    case "policy": {
                            var name = ExpectIdentifier(TokenKind.Identifier, "policy name");
                            Expect(TokenKind.Colon);
                            NoteMember(EntityMemberKind.Policy, name);
                            ParsePolicy(name, changes);
                            continue;
                        }

                    case "legacy-action": {
                            // Legacy: Name(params): action { … }
                            var name = ExpectIdentifier(TokenKind.Identifier, "action name");
                            var actionParams = ParseActionParameterList();
                            Expect(TokenKind.Colon);
                            if (Current.Kind is not (TokenKind.Action or TokenKind.LBrace or TokenKind.When or TokenKind.Require)) {
                                throw Error($"Expected action after '{name}(...)', got '{Shown(Current)}'");
                            }
                            NoteMember(EntityMemberKind.Action, name);
                            ParseActionBody(name, changes, stageName: null, actionParams);
                            continue;
                        }

                    case "typed-line": {
                            // Enum-typed property or bare (N1) navigation — resolved by
                            // known enum names, mirroring the legacy IsNavLine dispatch.
                            var name = ExpectIdentifier(TokenKind.Identifier, "property name");
                            Expect(TokenKind.Colon);
                            // The typed-line row's third element is an Identifier value, so the type word is a name.
                            var typeWord = Current.Name();
                            if (_enumTypeNames.Contains(typeWord)
                                || _valueTypeNames.Contains(typeWord)
                                || IsKnownPrimitiveName(typeWord)) {
                                var typeName = ExpectIdentifier(TokenKind.Identifier, "type name");
                                NoteMember(EntityMemberKind.Property, name);
                                ParseNamedTypeProperty(name, typeName, changes);
                            }
                            else if (IsNavLine()) {
                                NoteMember(EntityMemberKind.Navigation, name);
                                ParseNavLine(name);
                            }
                            else {
                                CheckUnsupportedKeyword(name, typeWord);
                                throw Error($"Expected type, stage, action, policy, or navigation property after '{name}:'");
                            }
                            continue;
                        }

                    case "property": {
                            var name = ExpectIdentifier(TokenKind.Identifier, "property name");
                            Expect(TokenKind.Colon);
                            NoteMember(EntityMemberKind.Property, name);
                            ParseProperty(name, Current.Kind, changes);
                            continue;
                        }

                    case "nav-many":
                    case "nav-one":
                    case "nav-owned": {
                            var name = ExpectIdentifier(TokenKind.Identifier, "navigation property name");
                            Expect(TokenKind.Colon);
                            NoteMember(EntityMemberKind.Navigation, name);
                            ParseNavLine(name);
                            continue;
                        }

                    case "primitive-name": {
                            // Primitive keyword used as property name (e.g. "Number: Text"): the type
                            // must be a primitive too (DslTokenReader.IsPropertyName gives MCP the same rule).
                            var name = Current.Name();
                            Advance(); // consume type keyword (e.g. 'Number')
                            Expect(TokenKind.Colon);
                            // DslGrammar.IsPrimitiveType's rule: the type word's text is read only when it is an identifier.
                            if (!(DslGrammar.IsPrimitiveTypeKind(Current.Kind)
                                || (Current.Kind == TokenKind.Identifier && IsKnownPrimitiveName(Current.Name()))))
                                throw Error($"Expected type after '{name}:', got '{Shown(Current)}'");
                            if (IsPrimitiveType(Current.Kind)) {
                                NoteMember(EntityMemberKind.Property, name);
                                ParseProperty(name, Current.Kind, changes);
                            }
                            else {
                                var typeName = Current.Name();
                                Advance();
                                NoteMember(EntityMemberKind.Property, name);
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
                var name = Current.Name();
                Advance(); // consume identifier
                Expect(TokenKind.Colon);
                if (Current.Kind is TokenKind.LBrace or TokenKind.When or TokenKind.Require) {
                    NoteMember(EntityMemberKind.Action, name);
                    ParseStandaloneAction(name, changes);
                }
                else {
                    CheckUnsupportedKeyword(name, DslTokenText.Word(Current));
                    throw Error($"Expected type, stage, action, policy, or navigation property after '{name}:'");
                }
                continue;
            }

            throw Error($"Expected property, stage, action, or policy, got '{Shown(Current)}'");
        }

        Expect(TokenKind.RBrace);

        changes.Add(new SetEntityMemberOrderChange(entityName, _entityMembers.ToArray()));

        // ── Resolve pending requires (now all policies for this entity are known) ──
        ResolvePendingRequires(changes);
    }

    private void NoteMember(EntityMemberKind kind, string name) =>
        _entityMembers.Add(new EntityMemberRef(kind, name));

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
                    $"Unknown or unregistered annotation '{Shown(Current)}'. " +
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

        var keyword = Current.Name();
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
                args[positionalIndex.ToString()] = new AnnotationString(Current.Unescape());
                Advance();
            }
            else if (Current.Kind == TokenKind.Number) {
                args[positionalIndex.ToString()] = new AnnotationNumber(DslNumberText.ParseAnnotationNumber(Current));
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
                throw Error($"Expected annotation argument (string, number, bool, null), got '{Shown(Current)}'");
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
                    $"Expected ',' or ')' in annotation '{keyword}(...)', got '{Shown(Current)}'");
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