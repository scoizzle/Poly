// EffectAnalyzer partial: an exit-block transition must target what every leaving hop targets.
using Poly.DomainModeling.Ontology;

using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.DomainModeling.Analysis;

internal sealed partial class EffectAnalyzer {
    /// <summary>
    /// One transition found in an entity. <c>Home</c> is the stage whose body holds it, empty for an
    /// entity-level action or subscription. <c>Static</c> means the hop provably runs while the
    /// instance is still in <c>Home</c>; otherwise it may leave any stage. <c>Actor</c> is the action
    /// whose body holds the hop, if any: it decides whether the hop can run in a stage at all.
    /// </summary>
    private sealed record TransitionHop(
        StageTransitionEffect Node,
        string Home,
        bool Static,
        bool IsExit,
        string Site,
        bool FollowsEarlier,
        Action? Actor) {
        /// <summary>True when the hop sits in an entity-level body, which has no static source stage.</summary>
        public bool IsEntityLevel => Home.Length == 0;
    }

    /// <summary>
    /// DMEFF017: a hop assigns <c>CurrentStage</c> after the source stage's exit block is inlined,
    /// so an exit-block transition whose target can differ from the target of a hop that leaves
    /// the same stage is overridden. A hop whose actor cannot run in the stage is not a hop that
    /// leaves it (<see cref="GuardsExclude"/>). Every other existing transition counts as a possible
    /// hop: guards are otherwise not analysed, and a hop that may run after the instance has moved,
    /// or whose source stage is unknown, is assumed able to leave every stage.
    /// </summary>
    private static void ValidateExitBlockTransitionTargets(AnalysisContext context, Entity entity) {
        var hops = new List<TransitionHop>();
        foreach (var action in entity.Actions)
            CollectHops(action.Effects, action, null, $"entity-level action '{action.Name}'", $"action '{action.Name}'", false, hops);
        foreach (var sub in entity.Subscriptions)
            CollectHops(sub.Effects, null, null, "the entity-level 'when' subscription", "the entity-level 'when' subscription", false, hops);
        foreach (var stage in entity.Stages) {
            var name = stage.Name;
            CollectHops(stage.OnEntryEffects, null, name, $"the entry block of '{name}'", $"the entry block of '{name}'", false, hops);
            CollectHops(stage.OnExitEffects, null, name, $"the exit block of '{name}'", $"the exit block of '{name}'", true, hops);
            foreach (var action in stage.Actions)
                CollectHops(action.Effects, action, name, $"action '{action.Name}'", $"action '{action.Name}'", false, hops);
            foreach (var sub in stage.Subscriptions)
                CollectHops(sub.Effects, null, name, $"the 'when' subscription on '{name}'", $"the 'when' subscription on '{name}'", false, hops);
        }

        foreach (var exit in hops.Where(h => h.IsExit)) {
            var stage = exit.Home;
            var exitStage = entity.Stages.FirstOrDefault(s => string.Equals(s.Name, stage, StringComparison.Ordinal));
            var target = exit.Node.TargetStage.StageName;
            var conflicts = new List<string>();
            var namedByExit = new HashSet<string>();
            foreach (var other in hops.OrderByDescending(h => h.IsExit && h.Home == stage)) {
                if (ReferenceEquals(other, exit))
                    continue;
                // #802: an entity-level hop whose own gates contradict the stage's is refused before
                // any effect runs, so it cannot leave the stage.
                if (exitStage is not null && other.IsEntityLevel && other.Actor is { } actor
                    && GuardsExclude(actor, exitStage))
                    continue;
                if (other.Node.TargetStage.StageName == target
                    || (other.Static && other.Home != stage)
                    || (!(other.IsExit && other.Home == stage) && namedByExit.Contains(other.Node.TargetStage.StageName)))
                    continue;
                if (other.IsExit && other.Home == stage)
                    namedByExit.Add(other.Node.TargetStage.StageName);
                var phrase = DescribeConflict(other, stage);
                if (!conflicts.Contains(phrase))
                    conflicts.Add(phrase);
            }
            if (conflicts.Count == 0)
                continue;
            context.ReportError(
                exit.Node,
                $"Stage '{stage}' transitions to '{target}' in its exit block, but {string.Join("; ", conflicts)}. " +
                "The hop's stage assignment runs after the exit block, so the exit-block target is overridden: " +
                $"the instance can land on a different stage after '{target}' entry effects ran. " +
                $"Remove the transition from the exit block, or make it target the same stage as every hop that leaves '{stage}'.",
                DomainModelDiagnosticCodes.ExitBlockTransitionTargetOverridden);
        }
    }

    /// <summary>
    /// DMEFF018: the guide's effect table makes <c>invoke</c>, <c>create</c>, <c>create in</c> and
    /// <c>for … invoke</c> action-only, so each one anywhere in an exit block is rejected.
    /// </summary>
    private static void ValidateExitBlockEffects(AnalysisContext context, Entity entity) {
        foreach (var stage in entity.Stages)
            ReportActionOnlyEffects(context, stage.OnExitEffects, stage.Name);
    }

    private static void ReportActionOnlyEffects(AnalysisContext context, IReadOnlyList<Effect> effects, string stage) {
        foreach (var effect in effects) {
            switch (effect) {
                case ConditionalEffect c:
                    ReportActionOnlyEffects(context, c.ThenEffects, stage);
                    if (c.ElseEffects is not null)
                        ReportActionOnlyEffects(context, c.ElseEffects, stage);
                    break;
                case CompositeEffect c:
                    ReportActionOnlyEffects(context, c.Effects, stage);
                    break;
                case ForEachEffect f:
                    ReportActionOnlyEffects(context, f.Body, stage);
                    break;
                case InvokeActionEffect:
                    ReportActionOnly(context, effect, "invoke", stage);
                    break;
                case CreateEntityInstance:
                    ReportActionOnly(context, effect, "create", stage);
                    break;
                case CreateEntityInRelationshipEffect:
                    ReportActionOnly(context, effect, "create in", stage);
                    break;
                case ForEachInvokeEffect:
                    ReportActionOnly(context, effect, "for … invoke", stage);
                    break;
            }
        }
    }

    private static void ReportActionOnly(AnalysisContext context, Effect effect, string name, string stage) =>
        context.ReportError(
            effect,
            $"The exit block of stage '{stage}' uses '{name}', which is action-only. An exit block inlines into every " +
            $"hop that leaves '{stage}', so a callee or a create could transition the instance behind the exit-block " +
            $"target check. Move '{name}' into an action.",
            DomainModelDiagnosticCodes.ExitBlockEffectNotSupported);

    /// <summary>
    /// True when <paramref name="actor"/>'s gates and <paramref name="stage"/>'s cannot both hold, so the
    /// lowered body refuses the action before any effect runs and it cannot leave the stage. The one
    /// provable contradiction is a policy and its <c>require not</c> marker both required: the body checks
    /// the action's own policies and, for each stage policy the action does not lift, that policy's gate
    /// (<see cref="PolicyGuardFacts.Guards"/> is exactly that list). Every other guard relation is not
    /// analysed, so the hop counts as a possible one.
    /// </summary>
    private static bool GuardsExclude(Action actor, Stage stage) {
        var gates = PolicyGuardFacts.Guards(actor, stage);
        if (gates.Count < 2)
            return false;
        var required = gates.Select(g => g.Policy.Name).ToHashSet(StringComparer.Ordinal);
        return gates.Any(g => g.Negated && required.Contains(g.Policy.Name[PolicyGuardFacts.NegatedPrefix.Length..]));
    }

    private static string DescribeConflict(TransitionHop other, string stage) {
        var otherTarget = other.Node.TargetStage.StageName;
        if (other.IsExit && other.Home == stage)
            return $"this exit block also transitions to '{otherTarget}'";
        if (other.Static)
            return $"{other.Site} leaves '{stage}' for '{otherTarget}'";
        var phrase = $"{other.Site} transitions to '{otherTarget}' and can leave '{stage}' at run time";
        return other.FollowsEarlier
            ? phrase + " (it follows an earlier transition, branch, invoke or create)"
            : phrase;
    }

    private static void CollectHops(
        IReadOnlyList<Effect> effects, Action? actor, string? home, string site, string loopSite, bool isExit,
        List<TransitionHop> hops) {
        var moved = false;
        Walk(effects, actor, home, site, loopSite, isExit, inFor: false, hops, ref moved);
    }

    private static void Walk(
        IReadOnlyList<Effect> effects, Action? actor, string? home, string site, string loopSite, bool isExit, bool inFor,
        List<TransitionHop> hops, ref bool moved) {
        foreach (var effect in effects) {
            switch (effect) {
                case StageTransitionEffect st:
                    if (inFor)
                        hops.Add(new TransitionHop(st, home ?? "", false, isExit,
                            $"the transition inside the 'for' loop in {loopSite}", false, actor));
                    else if (home is not null && !moved)
                        hops.Add(new TransitionHop(st, home, true, isExit, site, false, actor));
                    else
                        hops.Add(new TransitionHop(st, home ?? "", false, isExit, site, moved, actor));
                    moved = true;
                    break;
                case ConditionalEffect c: {
                        var thenMoved = moved;
                        var elseMoved = moved;
                        Walk(c.ThenEffects, actor, home, site, loopSite, isExit, inFor, hops, ref thenMoved);
                        if (c.ElseEffects is not null)
                            Walk(c.ElseEffects, actor, home, site, loopSite, isExit, inFor, hops, ref elseMoved);
                        moved = moved || thenMoved || elseMoved;
                        break;
                    }
                case CompositeEffect c:
                    Walk(c.Effects, actor, home, site, loopSite, isExit, inFor, hops, ref moved);
                    break;
                case ForEachEffect f: {
                        var bodyMoved = moved;
                        Walk(f.Body, actor, home, site, loopSite, isExit, inFor: true, hops, ref bodyMoved);
                        moved = moved || bodyMoved;
                        break;
                    }
                case InvokeActionEffect or ForEachInvokeEffect or CreateEntityInstance or CreateEntityInRelationshipEffect:
                    moved = true;
                    break;
            }
        }
    }
}