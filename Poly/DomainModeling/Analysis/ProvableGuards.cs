using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;
using Poly.DomainModeling.Ontology.Effects;

using Action = Poly.DomainModeling.Ontology.Action;
using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using Subtract = Poly.DomainModeling.Ontology.Subtract;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// #444: a copy whose source may be null into a required target is DMEFF014 unless a guard clears
/// it, and a stage <c>require</c> that proves a property present is an invariant (DMEFF015). The
/// guards are a dominating <c>if</c>, an action <c>require</c>, an entry policy on every
/// transition into the stage, and a stage <c>require</c>. Facts are keys: <c>P:Name</c> for an
/// entity property, <c>A:name</c> for an action parameter. Stage and entry facts are borrowed:
/// a <c>transition to</c> drops them. Action <c>require</c> and <c>if</c> facts are local and stay.
/// </summary>
internal static class ProvableGuards {
    private const string Property = "P:";
    private const string Parameter = "A:";

    /// <summary>Local facts survive a stage transition. Borrowed facts are the stage and entry guards.</summary>
    private sealed class FactSet {
        public HashSet<string> Local { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Borrowed { get; } = new(StringComparer.Ordinal);

        public bool Has(string key) => Local.Contains(key) || Borrowed.Contains(key);

        public void Drop(string key) {
            Local.Remove(key);
            Borrowed.Remove(key);
        }

        public void ProveLocal(string key) {
            Borrowed.Remove(key);
            Local.Add(key);
        }

        public void Borrow(string key) {
            if (!Local.Contains(key))
                Borrowed.Add(key);
        }

        public void LeaveStage() => Borrowed.Clear();

        public FactSet Clone() {
            var copy = new FactSet();
            copy.Local.UnionWith(Local);
            copy.Borrowed.UnionWith(Borrowed);
            return copy;
        }

        public HashSet<string> PropertyNames() =>
            [.. Local.Concat(Borrowed)
                .Where(k => k.StartsWith(Property, StringComparison.Ordinal))
                .Select(k => k[Property.Length..])];
    }

    private sealed class EntryHit {
        public required string Target { get; init; }
        public required HashSet<string> Proven { get; init; }
        public HashSet<string> RenulledAfter { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>Runs every in-tree nullable-copy check for one entity.</summary>
    public static void Validate(AnalysisContext context, Domain domain, Entity entity) {
        var stages = entity.Stages;
        var hits = CollectEntries(entity);

        var entryProven = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        for (var i = 1; i < stages.Count; i++) {
            var name = stages[i].Name;
            var into = hits.Where(h => string.Equals(h.Target, name, StringComparison.Ordinal)).ToList();
            if (into.Count == 0)
                continue;
            HashSet<string>? common = null;
            foreach (var hit in into) {
                var held = new HashSet<string>(hit.Proven, StringComparer.Ordinal);
                held.ExceptWith(hit.RenulledAfter);
                if (common is null)
                    common = held;
                else
                    common.IntersectWith(held);
            }
            entryProven[name] = common ?? [];
        }

        HashSet<string> EntrySeed(Stage stage) =>
            entryProven.TryGetValue(stage.Name, out var proven) ? proven : [];

        // Greatest fixed point: drop any entry proof some action in the stage may re-null when
        // seeded with the current set, until stable. One invalid proof cannot vouch for another.
        var validSeeds = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        HashSet<string> ValidSeed(Stage stage) {
            if (validSeeds.TryGetValue(stage.Name, out var valid))
                return valid;
            valid = [.. EntrySeed(stage)];
            bool changed;
            do {
                var renulled = stage.Actions.Concat(entity.Actions)
                    .SelectMany(action => Renulls(action, stage, entity, valid))
                    .Select(r => r.Property)
                    .ToHashSet(StringComparer.Ordinal);
                changed = valid.RemoveWhere(renulled.Contains) > 0;
            } while (changed);
            validSeeds[stage.Name] = valid;
            return valid;
        }

        void ReportViolations(Action action, Stage stage) {
            var required = StagePresence(action, stage);
            if (required.Count == 0)
                return;
            foreach (var (node, property) in Renulls(action, stage, entity, ValidSeed(stage))) {
                if (!required.Contains(property))
                    continue;
                context.ReportError(
                    node,
                    $"Action '{action.Name}' may assign null to '{property}', but stage '{stage.Name}' " +
                    $"requires '{property}' to be present. No action that can run in the stage may null it.",
                    DomainModelDiagnosticCodes.StagePresenceInvalidated);
            }
        }

        foreach (var stage in stages) {
            foreach (var action in stage.Actions)
                ReportViolations(action, stage);
            foreach (var action in entity.Actions)
                ReportViolations(action, stage);
        }

        void Check(IReadOnlyList<Effect> effects, FactSet facts, Action? action) =>
            Walk(effects, facts, action, entity,
                (assign, current) => CheckCopy(context, domain, entity, action, assign, current));

        foreach (var action in entity.Actions) {
            var facts = FactsOf(Presence(action.Policies));
            if (stages.Count > 0) {
                foreach (var property in StagePresence(action, stages[0])) {
                    if (stages.All(s => StagePresence(action, s).Contains(property)))
                        facts.Borrow(Property + property);
                }
            }
            Check(action.Effects, facts, action);
        }
        for (var i = 0; i < stages.Count; i++) {
            var stage = stages[i];
            foreach (var action in stage.Actions) {
                var facts = FactsOf(Presence(action.Policies));
                foreach (var property in StagePresence(action, stage))
                    facts.Borrow(Property + property);
                if (i > 0) {
                    foreach (var property in ValidSeed(stage))
                        facts.Borrow(Property + property);
                }
                Check(action.Effects, facts, action);
            }
            Check(stage.OnEntryEffects, new FactSet(), null);
            Check(stage.OnExitEffects, new FactSet(), null);
            foreach (var sub in stage.Subscriptions)
                Check(sub.Effects, new FactSet(), null);
        }
        foreach (var sub in entity.Subscriptions)
            Check(sub.Effects, new FactSet(), null);
    }

    /// <summary>
    /// Properties a stage <c>require</c> proves present for this action. An action
    /// <c>require not Policy</c> suppresses that policy, matching <c>EmitStagePolicyGuards</c>.
    /// </summary>
    private static HashSet<string> StagePresence(Action action, Stage stage) {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var policy in stage.Policies) {
            if (action.Policies.Any(p => string.Equals(p.Name, "not_" + policy.Name, StringComparison.Ordinal)))
                continue;
            names.UnionWith(Presence([policy]));
        }
        return names;
    }

    private static List<(AssignEffect Node, string Property)> Renulls(
        Action action, Stage stage, Entity entity, IReadOnlySet<string> entrySeed) {
        var facts = FactsOf(Presence(action.Policies));
        foreach (var property in StagePresence(action, stage))
            facts.Borrow(Property + property);
        foreach (var property in entrySeed)
            facts.Borrow(Property + property);
        var list = new List<(AssignEffect, string)>();
        Walk(action.Effects, facts, action, entity, (assign, current) => {
            if (assign.Target is PropertyAccess target && MayBeNull(assign.Value, current, action, entity))
                list.Add((assign, target.Name));
        });
        return list;
    }

    /// <summary>
    /// One hit per transition. Proven is the property facts at the transition. RenulledAfter is
    /// what that path may null after it, including effects that follow an <c>if</c> the transition
    /// sat in. A transition from entry, exit or a subscription starts with no action policies.
    /// </summary>
    private static List<EntryHit> CollectEntries(Entity entity) {
        var hits = new List<EntryHit>();

        void Collect(IReadOnlyList<Effect> effects, FactSet facts, Action? action, List<EntryHit> open) {
            foreach (var effect in effects) {
                switch (effect) {
                    case AssignEffect assign:
                        if (assign.Target is PropertyAccess target && MayBeNull(assign.Value, facts, action, entity)) {
                            foreach (var hit in open)
                                hit.RenulledAfter.Add(target.Name);
                        }
                        NoteAssign(facts, assign, action, entity);
                        break;
                    case StageTransitionEffect transition:
                        var entered = new EntryHit {
                            Target = transition.TargetStage.StageName,
                            Proven = facts.PropertyNames(),
                        };
                        hits.Add(entered);
                        open.Add(entered);
                        facts.LeaveStage();
                        break;
                    case ConditionalEffect conditional: {
                            var thenFacts = facts.Clone();
                            var elseFacts = facts.Clone();
                            Prove(conditional.Condition, true, thenFacts, action);
                            Prove(conditional.Condition, false, elseFacts, action);
                            var thenOpen = CloneHits(open);
                            var elseOpen = CloneHits(open);
                            Collect(conditional.ThenEffects, thenFacts, action, thenOpen);
                            if (conditional.ElseEffects is not null)
                                Collect(conditional.ElseEffects, elseFacts, action, elseOpen);
                            for (var i = 0; i < open.Count; i++) {
                                open[i].RenulledAfter.UnionWith(thenOpen[i].RenulledAfter);
                                if (conditional.ElseEffects is not null)
                                    open[i].RenulledAfter.UnionWith(elseOpen[i].RenulledAfter);
                            }
                            foreach (var created in thenOpen.Skip(open.Count))
                                open.Add(created);
                            if (conditional.ElseEffects is not null) {
                                foreach (var created in elseOpen.Skip(open.Count))
                                    open.Add(created);
                            }
                            Intersect(facts, thenFacts, elseFacts, conditional.ElseEffects is not null);
                            break;
                        }
                    case CompositeEffect composite:
                        Collect(composite.Effects, facts, action, open);
                        break;
                    case ForEachEffect forEach: {
                            // Zero or more iterations: an `if` with no else over the body.
                            var bodyFacts = facts.Clone();
                            var bodyOpen = CloneHits(open);
                            Collect(forEach.Body, bodyFacts, action, bodyOpen);
                            for (var i = 0; i < open.Count; i++)
                                open[i].RenulledAfter.UnionWith(bodyOpen[i].RenulledAfter);
                            foreach (var created in bodyOpen.Skip(open.Count))
                                open.Add(created);
                            Intersect(facts, bodyFacts, bodyFacts, hasElse: false);
                            break;
                        }
                }
            }
        }

        foreach (var action in entity.Actions)
            Collect(action.Effects, FactsOf(Presence(action.Policies)), action, []);
        foreach (var sub in entity.Subscriptions)
            Collect(sub.Effects, new FactSet(), null, []);
        foreach (var stage in entity.Stages) {
            foreach (var action in stage.Actions)
                Collect(action.Effects, FactsOf(Presence(action.Policies)), action, []);
            Collect(stage.OnEntryEffects, new FactSet(), null, []);
            Collect(stage.OnExitEffects, new FactSet(), null, []);
            foreach (var sub in stage.Subscriptions)
                Collect(sub.Effects, new FactSet(), null, []);
        }
        return hits;
    }

    private static List<EntryHit> CloneHits(List<EntryHit> open) {
        var copy = new List<EntryHit>(open.Count);
        foreach (var hit in open) {
            var clone = new EntryHit {
                Target = hit.Target,
                Proven = new HashSet<string>(hit.Proven, StringComparer.Ordinal),
            };
            clone.RenulledAfter.UnionWith(hit.RenulledAfter);
            copy.Add(clone);
        }
        return copy;
    }

    /// <summary>
    /// A fact stays local only when both branches proved it locally. A fact both branches still
    /// hold, but not both locally, stays borrowed so a later transition can drop it. A missing
    /// else keeps the facts the then-branch did not drop.
    /// </summary>
    private static void Intersect(FactSet into, FactSet thenSide, FactSet elseSide, bool hasElse) {
        var elseLocal = hasElse ? elseSide.Local : into.Local;
        var elseBorrowed = hasElse ? elseSide.Borrowed : into.Borrowed;
        var elseHeld = elseLocal.Concat(elseBorrowed).ToHashSet(StringComparer.Ordinal);
        var thenHeld = thenSide.Local.Concat(thenSide.Borrowed).ToHashSet(StringComparer.Ordinal);
        var local = thenSide.Local.Where(elseLocal.Contains).ToHashSet(StringComparer.Ordinal);
        var borrowed = thenHeld.Where(k => elseHeld.Contains(k) && !local.Contains(k)).ToHashSet(StringComparer.Ordinal);
        into.Local.Clear();
        into.Borrowed.Clear();
        into.Local.UnionWith(local);
        into.Borrowed.UnionWith(borrowed);
    }

    private static void CheckCopy(
        AnalysisContext context, Domain domain, Entity entity, Action? action, AssignEffect assign, FactSet facts) {
        if (assign.Target is not PropertyAccess target)
            return;
        var targetProperty = entity.Properties.FirstOrDefault(p => p.Name == target.Name);
        if (targetProperty is null || !EntityStructureAnalyzer.IsRequired(targetProperty))
            return;
        if (SourceKey(assign.Value, action) is not { } key || !IsNullable(key, action, entity) || facts.Has(key))
            return;

        context.ReportError(
            assign,
            $"Assign copies '{key[2..]}', which may be null, into required '{target.Name}'. " +
            $"Guard the copy with an if ({key[2..]} is not null), require a policy that proves it present, " +
            $"or declare '{target.Name}' optional.",
            DomainModelDiagnosticCodes.ObservedFlowNullability);
        ObservedFlowChecks.NoteNullabilityFinding(context, domain, assign);
    }

    private static string? SourceKey(DomainExpression value, Action? action) => value switch {
        ParameterAccess pa => Parameter + pa.Name,
        PropertyAccess pa when action?.Parameters.Any(p => p.Name == pa.Name) == true => Parameter + pa.Name,
        PropertyAccess pa => Property + pa.Name,
        _ => null,
    };

    private static bool IsNullable(string key, Action? action, Entity entity) {
        var name = key[2..];
        var property = key.StartsWith(Parameter, StringComparison.Ordinal)
            ? action?.Parameters.FirstOrDefault(p => p.Name == name)
            : entity.Properties.FirstOrDefault(p => p.Name == name);
        return property is not null && !EntityStructureAnalyzer.IsRequired(property);
    }

    private static bool MayBeNull(DomainExpression value, FactSet facts, Action? action, Entity entity) {
        switch (value) {
            case Literal literal:
                return literal.Value is null;
            case PropertyAccess or ParameterAccess:
                var key = SourceKey(value, action)!;
                var name = key[2..];
                var known = key.StartsWith(Parameter, StringComparison.Ordinal)
                    ? action?.Parameters.Any(p => p.Name == name) == true
                    : entity.Properties.Any(p => p.Name == name);
                return !known || (IsNullable(key, action, entity) && !facts.Has(key));
            case Add or Subtract or Multiply or Divide or Comparison or And or Or or Not
                or Exists or NotExists or CountExpr or AnyExpr or AllExpr or NoneExpr or SumExpr:
                return false;
            default:
                return true;
        }
    }

    /// <summary>
    /// Walks effects in order. An <c>if</c> adds what its condition proves to each branch and keeps
    /// what both branches keep. A <c>transition to</c> drops borrowed stage and entry facts and
    /// keeps action-<c>require</c> and <c>if</c> facts. An assign that may carry null drops its target.
    /// </summary>
    private static void Walk(
        IReadOnlyList<Effect> effects,
        FactSet facts,
        Action? action,
        Entity entity,
        System.Action<AssignEffect, FactSet> onAssign) {
        foreach (var effect in effects) {
            switch (effect) {
                case AssignEffect assign:
                    onAssign(assign, facts);
                    NoteAssign(facts, assign, action, entity);
                    break;
                case StageTransitionEffect:
                    facts.LeaveStage();
                    break;
                case ConditionalEffect conditional: {
                        var whenTrue = facts.Clone();
                        var whenFalse = facts.Clone();
                        Prove(conditional.Condition, true, whenTrue, action);
                        Prove(conditional.Condition, false, whenFalse, action);
                        Walk(conditional.ThenEffects, whenTrue, action, entity, onAssign);
                        if (conditional.ElseEffects is not null)
                            Walk(conditional.ElseEffects, whenFalse, action, entity, onAssign);
                        Intersect(facts, whenTrue, whenFalse, conditional.ElseEffects is not null);
                        break;
                    }
                case CompositeEffect composite:
                    Walk(composite.Effects, facts, action, entity, onAssign);
                    break;
                case ForEachEffect forEach: {
                        var bodyFacts = facts.Clone();
                        Walk(forEach.Body, bodyFacts, action, entity, onAssign);
                        Intersect(facts, bodyFacts, bodyFacts, hasElse: false);
                        break;
                    }
            }
        }
    }

    private static void NoteAssign(FactSet facts, AssignEffect assign, Action? action, Entity entity) {
        if (assign.Target is not PropertyAccess target)
            return;
        var key = Property + target.Name;
        if (MayBeNull(assign.Value, facts, action, entity))
            facts.Drop(key);
        else
            facts.ProveLocal(key);
    }

    /// <summary>
    /// Adds the keys <paramref name="expression"/> proves present when it evaluates to
    /// <paramref name="truth"/>. A conjunct proves if one side does; an <c>or</c> proves nothing true.
    /// </summary>
    private static void Prove(DomainExpression expression, bool truth, FactSet facts, Action? action) {
        switch (expression) {
            case Not negation:
                Prove(negation.Operand, !truth, facts, action);
                break;
            case And conjunction when truth:
                Prove(conjunction.Left, true, facts, action);
                Prove(conjunction.Right, true, facts, action);
                break;
            case Or disjunction when !truth:
                Prove(disjunction.Left, false, facts, action);
                Prove(disjunction.Right, false, facts, action);
                break;
            case Comparison { Kind: ComparisonKind.Equal or ComparisonKind.NotEqual } comparison:
                var present = (comparison.Kind == ComparisonKind.NotEqual) == truth;
                if (!present)
                    break;
                if (comparison.Right is Literal { Value: null } && SourceKey(comparison.Left, action) is { } left)
                    facts.ProveLocal(left);
                else if (comparison.Left is Literal { Value: null } && SourceKey(comparison.Right, action) is { } right)
                    facts.ProveLocal(right);
                break;
            case Exists exists when truth && SourceKey(exists.Target, action) is { } existing:
                facts.ProveLocal(existing);
                break;
            case NotExists notExists when !truth && SourceKey(notExists.Target, action) is { } missing:
                facts.ProveLocal(missing);
                break;
        }
    }

    /// <summary>The entity properties the given policies prove present (all must hold).</summary>
    private static HashSet<string> Presence(IReadOnlyList<Policy> policies) {
        var facts = new FactSet();
        foreach (var policy in policies)
            Prove(policy.Expression, true, facts, action: null);
        return facts.PropertyNames();
    }

    private static FactSet FactsOf(IEnumerable<string> properties) {
        var facts = new FactSet();
        foreach (var property in properties)
            facts.ProveLocal(Property + property);
        return facts;
    }
}