using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

using TokenKind = Poly.DomainModeling.Language.DslTokenKind;

namespace Poly.DomainModeling.Language;

// DomainResolver (Stages) — stage declarations, stage subscriptions and their quantifiers.

public sealed partial class DomainResolver {
    private void ParseStage(string name, List<DomainChange> changes) {
        Advance(); // consume 'stage'

        // P2′′′′.3: Clear error if someone tries the removed 'prev' keyword
        if (Current.Kind == TokenKind.Identifier &&
            Current.TextEquals("prev")) {
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
                            throw Error($"'{Shown(Current)}' must appear at the beginning of the stage block, before actions and subscriptions.");
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
                            throw Error($"'{Shown(Current)}' must appear at the beginning of the stage block, before actions and subscriptions.");
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
                throw Error($"'{Shown(Current)}' must appear at the beginning of the stage block, before actions and subscriptions.");
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
            Current.TextEquals("any", StringComparison.OrdinalIgnoreCase)) {
            Advance(); // consume 'any'
            return StageSubscriptionQuantifier.Any;
        }
        if (Current.Kind == TokenKind.Identifier &&
            Current.TextEquals("all", StringComparison.OrdinalIgnoreCase)) {
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
}