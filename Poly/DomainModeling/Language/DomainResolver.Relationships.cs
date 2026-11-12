using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.Grammar;

using TokenKind = Poly.DomainModeling.Language.DslTokenKind;

namespace Poly.DomainModeling.Language;

// DomainResolver (Relationships) — navigation-property (N1) declarations and deferred relationship resolution.

public sealed partial class DomainResolver {
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

            changes.Add(new AddRelationshipChange(
                nav.PropertyName,
                new DomainTypeReference(nav.SourceEntityName),
                new DomainTypeReference(nav.TargetTypeName),
                nav.Cardinality, [], nav.SourceOwnsTarget));
        }
        _pendingNavs.Clear();
    }
}