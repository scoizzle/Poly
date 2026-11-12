// EffectAnalyzer partial: an exit-block transition must target what every leaving hop targets.
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;

namespace Poly.DomainModeling.Analysis;

internal sealed partial class EffectAnalyzer {
    /// <summary>
    /// One transition found in an entity. <c>Home</c> is the stage whose body holds it (null for
    /// entity-level actions and subscriptions). <c>Static</c> means the hop provably runs while the
    /// instance is still in <c>Home</c>; otherwise it may leave any stage.
    /// </summary>
    private sealed record TransitionHop(
        StageTransitionEffect Node,
        string Home,
        bool Static,
        bool IsExit,
        string Site,
        bool FollowsEarlier);

    /// <summary>
    /// DMEFF017: a hop assigns <c>CurrentStage</c> after the source stage's exit block is inlined,
    /// so an exit-block transition whose target can differ from the target of a hop that leaves
    /// the same stage is overridden. Guards are not analysed: every existing transition counts as
    /// a possible hop. A hop that may run after the instance has moved, or whose source stage is
    /// unknown, is assumed able to leave every stage.
    /// </summary>
    private static void ValidateExitBlockTransitionTargets(AnalysisContext context, Entity entity) {
        var hops = new List<TransitionHop>();
        foreach (var action in entity.Actions)
            CollectHops(action.Effects, null, $"entity-level action '{action.Name}'", $"action '{action.Name}'", false, hops);
        foreach (var sub in entity.Subscriptions)
            CollectHops(sub.Effects, null, "the entity-level 'when' subscription", "the entity-level 'when' subscription", false, hops);
        foreach (var stage in entity.Stages) {
            var name = stage.Name;
            CollectHops(stage.OnEntryEffects, name, $"the entry block of '{name}'", $"the entry block of '{name}'", false, hops);
            CollectHops(stage.OnExitEffects, name, $"the exit block of '{name}'", $"the exit block of '{name}'", true, hops);
            foreach (var action in stage.Actions)
                CollectHops(action.Effects, name, $"action '{action.Name}'", $"action '{action.Name}'", false, hops);
            foreach (var sub in stage.Subscriptions)
                CollectHops(sub.Effects, name, $"the 'when' subscription on '{name}'", $"the 'when' subscription on '{name}'", false, hops);
        }

        foreach (var exit in hops.Where(h => h.IsExit)) {
            var stage = exit.Home;
            var target = exit.Node.TargetStage.StageName;
            var conflicts = new List<string>();
            foreach (var other in hops) {
                if (ReferenceEquals(other, exit)
                    || other.Node.TargetStage.StageName == target
                    || (other.Static && other.Home != stage))
                    continue;
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
        IReadOnlyList<Effect> effects, string? home, string site, string loopSite, bool isExit, List<TransitionHop> hops) {
        var moved = false;
        Walk(effects, home, site, loopSite, isExit, inFor: false, hops, ref moved);
    }

    private static void Walk(
        IReadOnlyList<Effect> effects, string? home, string site, string loopSite, bool isExit, bool inFor,
        List<TransitionHop> hops, ref bool moved) {
        foreach (var effect in effects) {
            switch (effect) {
                case StageTransitionEffect st:
                    if (inFor)
                        hops.Add(new TransitionHop(st, home ?? "", false, isExit,
                            $"the transition inside the 'for' loop in {loopSite}", false));
                    else if (home is not null && !moved)
                        hops.Add(new TransitionHop(st, home, true, isExit, site, false));
                    else
                        hops.Add(new TransitionHop(st, home ?? "", false, isExit, site, home is not null));
                    moved = true;
                    break;
                case ConditionalEffect c: {
                    var thenMoved = moved;
                    var elseMoved = moved;
                    Walk(c.ThenEffects, home, site, loopSite, isExit, inFor, hops, ref thenMoved);
                    if (c.ElseEffects is not null)
                        Walk(c.ElseEffects, home, site, loopSite, isExit, inFor, hops, ref elseMoved);
                    moved = moved || thenMoved || elseMoved;
                    break;
                }
                case CompositeEffect c:
                    Walk(c.Effects, home, site, loopSite, isExit, inFor, hops, ref moved);
                    break;
                case ForEachEffect f: {
                    var bodyMoved = moved;
                    Walk(f.Body, home, site, loopSite, isExit, inFor: true, hops, ref bodyMoved);
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
