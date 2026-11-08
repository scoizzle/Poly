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
/// entity property, <c>A:name</c> for an action parameter.
/// </summary>
internal static class ProvableGuards {
    private const string Property = "P:";
    private const string Parameter = "A:";

    private interface IFlowVisitor {
        void OnAssign(AssignEffect assign, HashSet<string> facts);
        void OnTransition(StageTransitionEffect transition, HashSet<string> facts);
    }

    private sealed class Visitor(
        System.Action<AssignEffect, HashSet<string>>? assign,
        System.Action<StageTransitionEffect, HashSet<string>>? transition) : IFlowVisitor {
        public void OnAssign(AssignEffect a, HashSet<string> facts) => assign?.Invoke(a, facts);
        public void OnTransition(StageTransitionEffect t, HashSet<string> facts) => transition?.Invoke(t, facts);
    }

    /// <summary>Runs every in-tree nullable-copy check for one entity.</summary>
    public static void Validate(AnalysisContext context, Domain domain, Entity entity) {
        var stages = entity.Stages;
        var stageRequired = stages.ToDictionary(
            s => s.Name, s => Presence(s.Policies), StringComparer.Ordinal);

        var allActions = new List<(Action Action, Stage? Stage)>();
        foreach (var action in entity.Actions)
            allActions.Add((action, null));
        foreach (var stage in stages)
            foreach (var action in stage.Actions)
                allActions.Add((action, stage));

        // Assigns that may null their target, judged on what the action itself establishes.
        var renulls = new Dictionary<Action, List<(AssignEffect Node, string Property)>>(
            ReferenceEqualityComparer.Instance);
        foreach (var (action, _) in allActions) {
            var list = new List<(AssignEffect, string)>();
            renulls[action] = list;
            Walk(action.Effects, Facts(Presence(action.Policies)), action, entity,
                new Visitor((assign, facts) => {
                    if (assign.Target is PropertyAccess target && MayBeNull(assign.Value, facts, action, entity))
                        list.Add((assign, target.Name));
                }, null));
        }

        // DMEFF015: an action that can run in a stage must not null what the stage proves present.
        foreach (var stage in stages) {
            if (stageRequired[stage.Name].Count == 0)
                continue;
            foreach (var (action, owner) in allActions) {
                if (owner is not null && !ReferenceEquals(owner, stage))
                    continue;
                foreach (var (node, property) in renulls[action]) {
                    if (!stageRequired[stage.Name].Contains(property))
                        continue;
                    context.ReportError(
                        node,
                        $"Action '{action.Name}' may assign null to '{property}', but stage '{stage.Name}' " +
                        $"requires '{property}' to be present. No action that can run in the stage may null it.",
                        DomainModelDiagnosticCodes.StagePresenceInvalidated);
                }
            }
        }

        var entryProven = EntryProven(entity, stages);

        bool EntryCovers(Stage stage, string property) =>
            entryProven.TryGetValue(stage.Name, out var proven)
            && proven.Contains(property)
            && !allActions.Any(a => (a.Stage is null || ReferenceEquals(a.Stage, stage))
                && renulls[a.Action].Any(r => r.Property == property));

        void Check(IReadOnlyList<Effect> effects, HashSet<string> initial, Action? action) =>
            Walk(effects, initial, action, entity,
                new Visitor((assign, facts) => CheckCopy(context, domain, entity, action, assign, facts), null));

        foreach (var action in entity.Actions) {
            var facts = Facts(Presence(action.Policies));
            if (stages.Count > 0) {
                foreach (var property in stageRequired[stages[0].Name]) {
                    if (stages.All(s => stageRequired[s.Name].Contains(property)))
                        facts.Add(Property + property);
                }
            }
            Check(action.Effects, facts, action);
        }
        for (var i = 0; i < stages.Count; i++) {
            var stage = stages[i];
            foreach (var action in stage.Actions) {
                var facts = Facts(Presence(action.Policies));
                facts.UnionWith(Facts(stageRequired[stage.Name]));
                if (i > 0) {
                    foreach (var property in entryProven.GetValueOrDefault(stage.Name) ?? [])
                        if (EntryCovers(stage, property))
                            facts.Add(Property + property);
                }
                Check(action.Effects, facts, action);
            }
            Check(stage.OnEntryEffects, [], null);
            Check(stage.OnExitEffects, [], null);
            foreach (var sub in stage.Subscriptions)
                Check(sub.Effects, [], null);
        }
        foreach (var sub in entity.Subscriptions)
            Check(sub.Effects, [], null);
    }

    /// <summary>
    /// Per stage (never the first): the properties proven present on every transition into it.
    /// A transition from entry, exit or a subscription has no action policies, so it proves nothing.
    /// </summary>
    private static Dictionary<string, HashSet<string>> EntryProven(Entity entity, IReadOnlyList<Stage> stages) {
        var snapshots = new Dictionary<string, List<HashSet<string>>>(StringComparer.Ordinal);

        void Collect(IReadOnlyList<Effect> effects, HashSet<string> initial, Action? action) =>
            Walk(effects, initial, action, entity, new Visitor(null, (transition, facts) => {
                var target = transition.TargetStage.StageName;
                if (!snapshots.TryGetValue(target, out var list))
                    snapshots[target] = list = [];
                list.Add([.. facts.Where(f => f.StartsWith(Property, StringComparison.Ordinal))
                    .Select(f => f.Substring(Property.Length))]);
            }));

        foreach (var action in entity.Actions)
            Collect(action.Effects, Facts(Presence(action.Policies)), action);
        foreach (var sub in entity.Subscriptions)
            Collect(sub.Effects, [], null);
        foreach (var stage in stages) {
            foreach (var action in stage.Actions)
                Collect(action.Effects, Facts(Presence(action.Policies)), action);
            Collect(stage.OnEntryEffects, [], null);
            Collect(stage.OnExitEffects, [], null);
            foreach (var sub in stage.Subscriptions)
                Collect(sub.Effects, [], null);
        }

        var proven = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        for (var i = 1; i < stages.Count; i++) {
            if (!snapshots.TryGetValue(stages[i].Name, out var list) || list.Count == 0)
                continue;
            var common = new HashSet<string>(list[0], StringComparer.Ordinal);
            foreach (var other in list.Skip(1))
                common.IntersectWith(other);
            proven[stages[i].Name] = common;
        }
        return proven;
    }

    private static void CheckCopy(
        AnalysisContext context, Domain domain, Entity entity, Action? action, AssignEffect assign, HashSet<string> facts) {
        if (assign.Target is not PropertyAccess target)
            return;
        var targetProperty = entity.Properties.FirstOrDefault(p => p.Name == target.Name);
        if (targetProperty is null || !EntityStructureAnalyzer.IsRequired(targetProperty))
            return;
        if (SourceKey(assign.Value, action) is not { } key || !IsNullable(key, action, entity) || facts.Contains(key))
            return;

        context.ReportError(
            assign,
            $"Assign copies '{key.Substring(2)}', which may be null, into required '{target.Name}'. " +
            $"Guard the copy with an if ({key.Substring(2)} is not null), require a policy that proves it present, " +
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
        var name = key.Substring(2);
        var property = key.StartsWith(Parameter, StringComparison.Ordinal)
            ? action?.Parameters.FirstOrDefault(p => p.Name == name)
            : entity.Properties.FirstOrDefault(p => p.Name == name);
        return property is not null && !EntityStructureAnalyzer.IsRequired(property);
    }

    private static bool MayBeNull(DomainExpression value, HashSet<string> facts, Action? action, Entity entity) {
        switch (value) {
            case Literal literal:
                return literal.Value is null;
            case PropertyAccess or ParameterAccess:
                var key = SourceKey(value, action)!;
                var name = key.Substring(2);
                var known = key.StartsWith(Parameter, StringComparison.Ordinal)
                    ? action?.Parameters.Any(p => p.Name == name) == true
                    : entity.Properties.Any(p => p.Name == name);
                return !known || (IsNullable(key, action, entity) && !facts.Contains(key));
            case Add or Subtract or Multiply or Divide or Comparison or And or Or or Not
                or Exists or NotExists or CountExpr or SumExpr or AnyExpr or AllExpr or NoneExpr:
                return false;
            default:
                return true;
        }
    }

    /// <summary>
    /// Walks effects in order, tracking which keys are proven present. An <c>if</c> adds what its
    /// condition proves to each branch and keeps what both branches keep; an assign that may carry
    /// null drops its target.
    /// </summary>
    private static void Walk(
        IReadOnlyList<Effect> effects, HashSet<string> facts, Action? action, Entity entity, IFlowVisitor visitor) {
        foreach (var effect in effects) {
            switch (effect) {
                case AssignEffect assign:
                    visitor.OnAssign(assign, facts);
                    if (assign.Target is PropertyAccess target) {
                        var key = Property + target.Name;
                        if (MayBeNull(assign.Value, facts, action, entity))
                            facts.Remove(key);
                        else
                            facts.Add(key);
                    }
                    break;
                case StageTransitionEffect transition:
                    visitor.OnTransition(transition, facts);
                    break;
                case ConditionalEffect conditional: {
                        var whenTrue = new HashSet<string>(facts);
                        var whenFalse = new HashSet<string>(facts);
                        Prove(conditional.Condition, true, whenTrue, action);
                        Prove(conditional.Condition, false, whenFalse, action);
                        Walk(conditional.ThenEffects, whenTrue, action, entity, visitor);
                        if (conditional.ElseEffects is not null)
                            Walk(conditional.ElseEffects, whenFalse, action, entity, visitor);
                        facts.Clear();
                        facts.UnionWith(whenTrue.Where(whenFalse.Contains));
                        break;
                    }
                case CompositeEffect composite:
                    Walk(composite.Effects, facts, action, entity, visitor);
                    break;
            }
        }
    }

    /// <summary>
    /// Adds the keys <paramref name="expression"/> proves present when it evaluates to
    /// <paramref name="truth"/>. A conjunct proves if one side does; an <c>or</c> proves nothing true.
    /// </summary>
    private static void Prove(DomainExpression expression, bool truth, HashSet<string> facts, Action? action) {
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
                    facts.Add(left);
                else if (comparison.Left is Literal { Value: null } && SourceKey(comparison.Right, action) is { } right)
                    facts.Add(right);
                break;
            case Exists exists when truth && SourceKey(exists.Target, action) is { } existing:
                facts.Add(existing);
                break;
            case NotExists notExists when !truth && SourceKey(notExists.Target, action) is { } missing:
                facts.Add(missing);
                break;
        }
    }

    /// <summary>The entity properties the given policies prove present (all must hold).</summary>
    private static HashSet<string> Presence(IReadOnlyList<Policy> policies) {
        var facts = new HashSet<string>();
        foreach (var policy in policies)
            Prove(policy.Expression, true, facts, action: null);
        return [.. facts.Where(f => f.StartsWith(Property, StringComparison.Ordinal))
            .Select(f => f.Substring(Property.Length))];
    }

    private static HashSet<string> Facts(IEnumerable<string> properties) =>
        [.. properties.Select(p => Property + p)];
}