using Poly.DomainModeling.Ontology;

using TokenKind = Poly.DomainModeling.Language.DslTokenKind;

namespace Poly.DomainModeling.Language;

// DomainResolver (Effects) — action effect bodies (transition, assign, create, invoke, for, if) and property initializers.

public sealed partial class DomainResolver {
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
}