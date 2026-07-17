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

    private readonly record struct PendingRequire(
        string ActionName,
        string? StageName,
        string PolicyName,
        bool Negated);

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

        // ── Relationships (top-level N2 form) ──────────────────
        while (_current.Kind == TokenKind.Relationship) {
            ParseRelationship(changes);
        }

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

        changes.Add(new AddEntityChange(entityName, []));
        EnsurePrimitivesOnce(changes);

        while (_current.Kind != TokenKind.RBrace) {
            if (_current.Kind == TokenKind.Relationship) {
                ParseRelationship(changes);
            }
            else if (_current.Kind == TokenKind.Identifier && PeekIs(TokenKind.Colon)) {
                // Could be property, stage, action, or policy
                var name = _current.Text;
                var saved = _current;
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
                else if (IsPrimitiveType(_current.Kind)) {
                    ParseProperty(name, _current.Kind, changes);
                }
                else {
                    CheckUnsupportedKeyword(name, _current.Text);
                    throw Error($"Expected type, stage, action, or policy after '{name}:'");
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

    private void ParseRelationship(List<DomainChange> changes) {
        Advance(); // consume 'relationship'
        var relName = ExpectIdentifier(TokenKind.Identifier, "relationship name");
        Expect(TokenKind.From);
        var source = ExpectIdentifier(TokenKind.Identifier, "source entity name");
        Expect(TokenKind.To);
        var target = ExpectIdentifier(TokenKind.Identifier, "target entity name");

        var cardinality = RelationshipCardinality.OneToOne;
        if (_current.Kind == TokenKind.One) {
            Advance();
            cardinality = RelationshipCardinality.OneToOne;
        }
        else if (_current.Kind == TokenKind.Many) {
            Advance();
            cardinality = RelationshipCardinality.OneToMany;
        }
        else {
            throw Error($"Expected cardinality ('one' or 'many') for relationship '{relName}'");
        }

        changes.Add(new AddRelationshipChange(relName,
            new DomainTypeReference(source), new DomainTypeReference(target),
            cardinality, [], false));
    }

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