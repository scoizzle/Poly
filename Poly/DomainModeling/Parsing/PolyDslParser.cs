using System.Globalization;

using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Effects;
using Poly.DomainModeling.Evolution;

namespace Poly.DomainModeling.Parsing;

/// <summary>
/// Recursive-descent parser for the Phase 1a Poly DSL.
/// Produces <see cref="DomainChange"/> records for evolution application.
/// Zero external dependencies — uses <see cref="PolyDslTokenizer"/>.
///
/// Expressions parse to existing <see cref="DomainExpression"/> nodes.
/// </summary>
public sealed class PolyDslParser {
    private readonly PolyDslTokenizer _tokenizer;
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

    // Property names per entity, for collision detection with navs
    private readonly Dictionary<string, HashSet<string>> _entityPropertyNames = new(StringComparer.Ordinal);

    // Relationship names from N1 nav lines, for duplicate detection
    private readonly HashSet<string> _relationshipNames = new(StringComparer.Ordinal);

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

    public PolyDslParser(string text) {
        _tokenizer = new PolyDslTokenizer(text);
        _current = _tokenizer.Next();
    }

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

        // ── Entity definitions ─────────────────────────────────
        while (_current.Kind == TokenKind.Identifier) {
            ParseEntity(changes);
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

        // Check for unsupported keyword before expecting 'entity'
        if (_current.Kind == TokenKind.Identifier && _unsupportedKeywords.Contains(_current.Text)) {
            throw new FormatException(
                $"'{_current.Text}' is not supported in Phase 1a (use 'entity' instead)");
        }
        Expect(TokenKind.Entity);
        Expect(TokenKind.LBrace);

        _entityNames.Add(entityName);
        changes.Add(new AddEntityChange(entityName, []));
        EnsurePrimitivesOnce(changes);

        while (_current.Kind != TokenKind.RBrace) {
            if (_current.Kind == TokenKind.Relationship) {
                throw N2RelationshipNotSupported();
            }
            else if (_current.Kind == TokenKind.Identifier && PeekIs(TokenKind.Colon)) {
                // Could be property, stage, action, policy, or nav line
                var name = _current.Text;
                Advance(); // consume identifier
                Expect(TokenKind.Colon);

                if (_current.Kind == TokenKind.Stage) {
                    ParseStage(name, changes);
                }
                else if (_current.Kind == TokenKind.Action || _current.Kind == TokenKind.LBrace) {
                    ParseStandaloneAction(name, changes);
                }
                else if (_current.Kind == TokenKind.Policy) {
                    ParsePolicy(name, changes);
                }
                else if (IsNavLine()) {
                    ParseNavLine(name);
                }
                else if (IsPrimitiveType(_current.Kind)) {
                    ParseProperty(name, _current.Kind, changes);
                }
                else {
                    CheckUnsupportedKeyword(name, _current.Text);
                    throw Error($"Expected type, stage, action, policy, or navigation property after '{name}:'");
                }
            }
            else {
                throw Error($"Expected property, stage, action, or policy, got '{_current.Text}'");
            }
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

        // Track property name for collision detection with navs
        if (!_entityPropertyNames.TryGetValue(_currentEntityName, out var props)) {
            props = new HashSet<string>(StringComparer.Ordinal);
            _entityPropertyNames[_currentEntityName] = props;
        }
        props.Add(name);

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

        // Parse constraints
        var property = new Property(name, new DomainTypeReference(typeName), []);
        while (IsConstraint(_current.Kind)) {
            var constraint = ParseConstraint();
            if (constraint is not null) {
                changes.Add(new AddConstraintToPropertyChange(_currentEntityName, name, constraint));
            }
        }
    }

    private void ParseStage(string name, List<DomainChange> changes) {
        Advance(); // consume 'stage'
        StageReference? parent = null;

        if (_current.Kind == TokenKind.Prev) {
            Advance(); // consume 'prev'
            var parentName = ExpectIdentifier(TokenKind.Identifier, "parent stage name");
            parent = new StageReference(parentName);
        }

        changes.Add(new AddStageChange(_currentEntityName, name, parent));
        Expect(TokenKind.LBrace);

        while (_current.Kind != TokenKind.RBrace) {
            if (_current.Kind == TokenKind.When && PeekIs(TokenKind.Identifier)) {
                // Subscription: when RelName TargetStage { ... }
                ParseSubscription(name, changes);
            }
            else {
                // Stage-local action
                var actionName = ExpectIdentifier(TokenKind.Identifier, "action name");
                Expect(TokenKind.Colon);
                ParseActionBody(actionName, changes, name);
            }
        }

        Expect(TokenKind.RBrace);
    }

    private void ParseStandaloneAction(string name, List<DomainChange> changes) {
        // standalone action declared at entity level
        if (_current.Kind == TokenKind.Action)
            Advance(); // consume optional 'action'

        ParseActionBody(name, changes, stageName: null);
    }

    private void ParseActionBody(string actionName, List<DomainChange> changes, string? stageName) {
        // Optional 'action' keyword
        if (_current.Kind == TokenKind.Action)
            Advance();

        // Stage gates and require policies (collected, not emitted — resolved after entity body)
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

        Expect(TokenKind.LBrace);

        // Create the action
        if (stageName is not null) {
            changes.Add(new AddActionToStageChange(_currentEntityName, stageName, actionName));
        }
        else {
            changes.Add(new AddActionChange(_currentEntityName, actionName));
        }

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

        if (_current.Kind == TokenKind.When) {
            // Subscription effect — this is handled differently
            // (embedded in StageSubscription, not in a standalone action)
            // If we reach here it's a parsing error
            throw Error("Unexpected 'when' inside action body (subscriptions are stage-level)");
        }

        // Check for unsupported effect keywords
        if (_current.Kind == TokenKind.Identifier && _unsupportedKeywords.Contains(_current.Text)) {
            throw new FormatException(
                $"'{_current.Text}' is not supported in Phase 1a");
        }

        throw Error($"Expected effect (transition, assign), got '{_current.Text}'");
    }

    private void ParseSubscription(string stageName, List<DomainChange> changes) {
        Advance(); // consume 'when'
        var relName = ExpectIdentifier(TokenKind.Identifier, "relationship name");
        var targetStage = ExpectIdentifier(TokenKind.Identifier, "target stage name");
        Expect(TokenKind.LBrace);

        var effects = new List<Effect>();
        while (_current.Kind != TokenKind.RBrace) {
            effects.Add(ParseEffect());
        }
        Expect(TokenKind.RBrace);

        var subscription = new StageSubscription(relName, targetStage, StageSubscriptionQuantifier.Each, effects);
        changes.Add(new AddStageSubscriptionChange(_currentEntityName, stageName, subscription));
    }

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
        if (_current.Kind == TokenKind.Many || _current.Kind == TokenKind.One)
            return true;

        // "owned" as the first token after : → nav (must be followed by TypeName)
        if (_current.Kind == TokenKind.Identifier && _current.Text == "owned")
            return true;

        // Bare identifier that isn't a primitive type, keyword, or reserved construct
        if (_current.Kind == TokenKind.Identifier && !IsPrimitiveType(_current.Kind)) {
            var text = _current.Text;
            // Exclude known keywords that aren't primitives but shouldn't be nav targets
            return !_unsupportedKeywords.Contains(text)
                && text != "entity" && text != "stage" && text != "action"
                && text != "policy" && text != "relationship" && text != "when"
                && text != "require" && text != "transition" && text != "assign"
                && text != "prev" && text != "from" && text != "to"
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
        if (_current.Kind == TokenKind.Identifier && _current.Text == "owned") {
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

    // ── Expression parser ─────────────────────────────────────

    private DomainExpression ParseExpression() {
        return ParseOr();
    }

    private DomainExpression ParseOr() {
        var left = ParseAnd();
        while (_current.Kind == TokenKind.Or || (_current.Kind == TokenKind.Identifier && _current.Text == "or")) {
            Advance();
            var right = ParseAnd();
            left = DomainExpression.Or(left, right);
        }
        return left;
    }

    private DomainExpression ParseAnd() {
        var left = ParseNot();
        while (_current.Kind == TokenKind.And || (_current.Kind == TokenKind.Identifier && _current.Text == "and")) {
            Advance();
            var right = ParseNot();
            left = DomainExpression.And(left, right);
        }
        return left;
    }

    private DomainExpression ParseNot() {
        if (_current.Kind == TokenKind.Not) {
            Advance();
            var operand = ParsePrimary();
            return DomainExpression.Not(operand);
        }
        return ParseComparison();
    }

    private DomainExpression ParseComparison() {
        var left = ParsePrimary();

        if (IsComparisonOp(_current.Kind)) {
            var op = _current.Kind;

            // Special case: "is not" → NotEqual (consume both tokens)
            if (op == TokenKind.Is && PeekIs(TokenKind.Not)) {
                Advance(); // consume 'is'
                Advance(); // consume 'not'
                var rhs = ParsePrimary();
                return DomainExpression.NotEqual(left, rhs);
            }

            Advance(); // consume the operator

            // Handle standalone "is" without following "not" → Equal
            if (op == TokenKind.Is) {
                var rhs = ParsePrimary();
                return DomainExpression.Equal(left, rhs);
            }

            // Standard operators: == != > >= < <=
            var right = ParsePrimary();

            return op switch {
                TokenKind.Eq => DomainExpression.Equal(left, right),
                TokenKind.Neq => DomainExpression.NotEqual(left, right),
                TokenKind.Gt => DomainExpression.GreaterThan(left, right),
                TokenKind.Gte => DomainExpression.GreaterThanOrEqual(left, right),
                TokenKind.Lt => DomainExpression.LessThan(left, right),
                TokenKind.Lte => DomainExpression.LessThanOrEqual(left, right),
                _ => throw Error($"Unknown comparison operator '{op}'"),
            };
        }

        return left;
    }

    private DomainExpression ParsePrimary() {
        switch (_current.Kind) {
            case TokenKind.Number:
                var numText = _current.Text;
                Advance();
                if (long.TryParse(numText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longVal))
                    return DomainExpression.Literal(longVal);
                return DomainExpression.Literal(numText);

            case TokenKind.StringLiteral:
                var str = _current.Text;
                Advance();
                return DomainExpression.Literal(str);

            case TokenKind.True:
                Advance();
                return DomainExpression.Literal(true);

            case TokenKind.False:
                Advance();
                return DomainExpression.Literal(false);

            case TokenKind.Null:
                Advance();
                return DomainExpression.Literal(null);

            case TokenKind.LParen:
                Advance();
                var expr = ParseExpression();
                Expect(TokenKind.RParen);
                return expr;

            case TokenKind.Identifier:
                var name = _current.Text;
                Advance();
                return DomainExpression.Property(name);

            case TokenKind.Not:
                return ParseNot();

            default:
                throw Error($"Expected expression, got '{_current.Text}'");
        }
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

    private void Advance() {
        _current = _tokenizer.Next();
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
        return _tokenizer.Peek().Kind == kind;
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
            or TokenKind.Length or TokenKind.Pattern => true,
        _ => false,
    };

    private static bool IsComparisonOp(TokenKind kind) => kind switch {
        TokenKind.Is or TokenKind.Eq or TokenKind.Neq
            or TokenKind.Gt or TokenKind.Gte or TokenKind.Lt or TokenKind.Lte => true,
        _ => false,
    };

    private static readonly HashSet<string> _unsupportedKeywords = new(StringComparer.OrdinalIgnoreCase) {
        "actor", "value", "create", "schedule", "parallel", "invoke", "for", "function"
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

    private Exception Error(string message) {
        var tok = _current;
        return new FormatException($"Poly DSL parse error at line {tok.Line}, col {tok.Col}: {message}");
    }
}