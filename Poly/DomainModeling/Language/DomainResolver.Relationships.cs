using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.Grammar;

using TokenKind = Poly.DomainModeling.Language.DslTokenKind;

namespace Poly.DomainModeling.Language;

// DomainResolver (Relationships) — navigation-property (N1) declarations and deferred relationship resolution.

public sealed partial class DomainResolver {
    // Inverse names declared with `via`, keyed by the pending nav they belong to. `via`
    // is syntax and IR only: pairing validity is analysis (S-11), never this resolver.
    private readonly Dictionary<PendingNav, string> _inverseByNav = new();

    private Exception N2RelationshipNotSupported() =>
        Error(
            "The 'relationship Name from Source to Target one|many' form is not supported. " +
            "Use a navigation property on the source entity (e.g. 'orders: many Order').");

    /// <summary>
    /// Returns true if the current token starts a navigation property line (N1 form).
    /// Patterns: "many [owned] Type", "one [owned] Type", "owned Type", "Type" (bare entity name).
    /// </summary>
    private bool IsNavLine() {
        // TokenKind.Many and TokenKind.One are unambiguous nav starts
        if (Current.Kind == TokenKind.Many || Current.Kind == TokenKind.One)
            return true;

        // "owned" as the first token after : → nav (must be followed by TypeName)
        if (Current.Kind == TokenKind.Owned)
            return true;

        // Bare identifier that isn't a primitive type, keyword, or reserved construct
        if (Current.Kind == TokenKind.Identifier && !IsPrimitiveType(Current.Kind)) {
            var text = Current.Name();
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
                ? $" '{Shown(Current)}' is a primitive type, not an entity. Use a primitive property declaration instead."
                : $" unexpected token '{Shown(Current)}'";
            throw Error($"Navigation property '{name}' requires an entity type as target:{hint}");
        }
        var targetToken = Current;
        var targetType = ExpectIdentifier(TokenKind.Identifier, "target entity name");

        // `via <name>` names the inverse navigation. It is a contextual word, not a
        // grammar row: it starts a clause only when `via` is followed by a name, so a
        // member named `via` (`via: Text`, `via(x: Text): action`) still parses.
        string? inverseName = null;
        if (AtViaClause()) {
            var viaToken = Current;
            Advance();
            if (Current.Kind != TokenKind.Identifier || PeekIs(TokenKind.Colon))
                throw GrammarError.Error("'via' requires the inverse navigation name", viaToken.Line, viaToken.Col);
            inverseName = Current.Name();
            Advance();
            if (AtViaClause())
                throw Error($"'via' is declared more than once on navigation property '{name}'");
        }

        // `optional` closes the line: a to-one navigation is required at create unless it says so.
        // A collection is never required, and an owned single part is filled after its owner
        // exists, so it can never be required at create and must say `optional` (#677 ruling A).
        var isOptional = false;
        if (Current.Kind == TokenKind.Optional) {
            if (cardinality == RelationshipCardinality.OneToMany)
                throw GrammarError.Error(
                    $"Navigation '{name}' is a collection; 'optional' applies only to a to-one navigation. "
                    + "A collection is never required at create.",
                    Current.Line, Current.Col);
            isOptional = true;
            Advance();
        }
        else if (owned && cardinality == RelationshipCardinality.OneToOne) {
            throw GrammarError.Error(
                $"Navigation '{name}' is an owned single part: it is filled after its owner exists, so it can "
                + $"never be required at create. Declare it optional: '{name}: owned {targetType} optional'.",
                targetToken.Line, targetToken.Col);
        }

        RejectRetiredRequiredWord();
        var pending = new PendingNav(_currentEntityName, name, targetType, cardinality, owned, targetToken, isOptional);
        _pendingNavs.Add(pending);
        if (inverseName is not null)
            _inverseByNav[pending] = inverseName;
    }

    /// <summary>
    /// True when the cursor sits on a `via` clause: the contextual word `via` followed by
    /// the inverse navigation name. A following ':' or '(' means a member named `via`,
    /// not a clause.
    /// </summary>
    private bool AtViaClause() =>
        Current.Kind == TokenKind.Identifier
        && Current.TextEquals("via")
        && Peek(1).Kind != TokenKind.Colon
        && Peek(1).Kind != TokenKind.LParen;

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
            if (_valueTypeNames.Contains(nav.TargetTypeName)) {
                throw GrammarError.Error(
                    $"Property '{nav.PropertyName}' is declared with type '{nav.TargetTypeName}', a value type. "
                    + "A value type is a contract payload or action parameter and has no storage representation. "
                    + $"Declare a nested entity for stored state, or pass '{nav.TargetTypeName}' as an action parameter.",
                    nav.At.Line, nav.At.Col);
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

            _inverseByNav.TryGetValue(nav, out var inverse);
            changes.Add(new AddRelationshipChange(
                nav.PropertyName,
                new DomainTypeReference(nav.SourceEntityName),
                new DomainTypeReference(nav.TargetTypeName),
                nav.Cardinality, [], nav.SourceOwnsTarget, inverse, nav.IsOptional));
        }
        _pendingNavs.Clear();
        _inverseByNav.Clear();
    }
}