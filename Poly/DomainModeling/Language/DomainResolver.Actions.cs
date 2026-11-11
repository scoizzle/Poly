using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

using TokenKind = Poly.DomainModeling.Language.DslTokenKind;

namespace Poly.DomainModeling.Language;

// DomainResolver (Actions) — action declarations, parameter lists, action bodies and entity policies.

public sealed partial class DomainResolver {
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

    private void ParsePolicy(string name, List<DomainChange> changes) {
        Advance(); // consume 'policy'
        Expect(TokenKind.LBrace);
        var expr = ParseExpression();
        changes.Add(new AddPolicyToEntityChange(_currentEntityName, new Policy(name, expr)));
        _entityPolicies[name] = expr;
        Expect(TokenKind.RBrace);
    }
}