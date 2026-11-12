// EffectAnalyzer partial: unconditional automatic stage-transition cycle detection.
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;

namespace Poly.DomainModeling.Analysis;

internal sealed partial class EffectAnalyzer {
    /// <summary>
    /// DMEFF013: reject only unconditional cycles in the automatic transition graph
    /// (entry + exit, including nested in <c>if</c>). A cycle that passes through a real
    /// guard is allowed; Analyze cannot prove every guard eventually fails, so the runtime
    /// loop guard is the backstop.
    /// </summary>
    private static void ValidateUnconditionalAutomaticTransitionCycles(
        AnalysisContext context, Entity entity) {
        if (entity.Stages.Count == 0)
            return;

        // Unconditional edges only: stage → target when a transition is reached without a
        // real guard (or only through guards Analyze can prove always true).
        var adjacency = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var stage in entity.Stages) {
            CollectUnconditionalTargets(stage.OnEntryEffects, guarded: false, adjacency, stage.Name);
            CollectUnconditionalTargets(stage.OnExitEffects, guarded: false, adjacency, stage.Name);
        }
        if (adjacency.Count == 0)
            return;

        var cycle = FindCycle(adjacency);
        if (cycle is null)
            return;

        context.ReportError(
            entity,
            $"Entity '{entity.Name}' has an unconditional automatic stage transition cycle: " +
            $"{string.Join(" → ", cycle)}. Add a guard (if) on at least one edge, or remove a " +
            "transition from entry/exit.",
            DomainModelDiagnosticCodes.UnconditionalAutomaticTransitionCycle);
    }

    private static void CollectUnconditionalTargets(
        IReadOnlyList<Effect> effects,
        bool guarded,
        Dictionary<string, HashSet<string>> adjacency,
        string fromStage) {
        foreach (var effect in effects) {
            switch (effect) {
                case StageTransitionEffect st when !guarded:
                    if (!adjacency.TryGetValue(fromStage, out var set)) {
                        set = new HashSet<string>(StringComparer.Ordinal);
                        adjacency[fromStage] = set;
                    }
                    set.Add(st.TargetStage.StageName);
                    break;
                case StageTransitionEffect:
                    break; // guarded — not part of the unconditional graph
                case ConditionalEffect c:
                    var alwaysTrue = IsProvablyAlwaysTrue(c.Condition);
                    // Then-branch: still unguarded only when the condition is always true.
                    CollectUnconditionalTargets(
                        c.ThenEffects, guarded: guarded || !alwaysTrue, adjacency, fromStage);
                    if (c.ElseEffects is { Count: > 0 })
                        CollectUnconditionalTargets(
                            c.ElseEffects, guarded: true, adjacency, fromStage);
                    break;
                case CompositeEffect c:
                    CollectUnconditionalTargets(c.Effects, guarded, adjacency, fromStage);
                    break;
            }
        }
    }

    /// <summary>
    /// Numeric order of two literal values through the shared compare: exact first, master's double
    /// comparison when the exact order is missing. Null when either side is not a number, so a
    /// non-numeric pair is never proven (as on master).
    /// </summary>
    private static int? CompareNumericLiterals(object? left, object? right) =>
        ConstraintMerge.IsNumeric(left) && ConstraintMerge.IsNumeric(right)
            ? ConstraintMerge.TryCompare(left!, right!)
            : null;

    /// <summary>
    /// Literal equality for the Equal/NotEqual arms. Numbers compare by value, so a mixed
    /// <c>long</c>/<c>decimal</c> pair above 2^53 is decided by order, not by CLR type. When the two
    /// cannot be ordered the pre-V-05a value equality stands, so this is never worse than master.
    /// </summary>
    private static bool? LiteralsEqual(object? left, object? right) {
        if (left is null || right is null) return Equals(left, right);
        // The order path is for numbers; anything else keeps master's value equality, which is
        // ordinal for strings (CompareTo is culture-sensitive and ignores a soft hyphen).
        if (!ConstraintMerge.IsNumeric(left) || !ConstraintMerge.IsNumeric(right)) return Equals(left, right);
        return ConstraintMerge.TryCompare(left, right) is { } order ? order == 0 : Equals(left, right);
    }

    /// <summary>
    /// Only literals and comparisons of literals — anything that reads state is not proven.
    /// </summary>
    internal static bool IsProvablyAlwaysTrue(DomainExpression expression) => expression switch {
        Literal { Value: bool b } => b,
        global::Poly.DomainModeling.Ontology.Not n => IsProvablyAlwaysFalse(n.Operand),
        global::Poly.DomainModeling.Ontology.And a => IsProvablyAlwaysTrue(a.Left) && IsProvablyAlwaysTrue(a.Right),
        global::Poly.DomainModeling.Ontology.Or o => IsProvablyAlwaysTrue(o.Left) || IsProvablyAlwaysTrue(o.Right),
        Comparison { Kind: ComparisonKind.Equal, Left: Literal l, Right: Literal r } =>
            LiteralsEqual(l.Value, r.Value) is true,
        Comparison { Kind: ComparisonKind.NotEqual, Left: Literal l, Right: Literal r } =>
            LiteralsEqual(l.Value, r.Value) is false,
        Comparison { Kind: ComparisonKind.GreaterThanOrEqual, Left: Literal l, Right: Literal r }
            when CompareNumericLiterals(l.Value, r.Value) is { } order => order >= 0,
        Comparison { Kind: ComparisonKind.GreaterThan, Left: Literal l, Right: Literal r }
            when CompareNumericLiterals(l.Value, r.Value) is { } order => order > 0,
        Comparison { Kind: ComparisonKind.LessThanOrEqual, Left: Literal l, Right: Literal r }
            when CompareNumericLiterals(l.Value, r.Value) is { } order => order <= 0,
        Comparison { Kind: ComparisonKind.LessThan, Left: Literal l, Right: Literal r }
            when CompareNumericLiterals(l.Value, r.Value) is { } order => order < 0,
        _ => false
    };

    internal static bool IsProvablyAlwaysFalse(DomainExpression expression) => expression switch {
        Literal { Value: bool b } => !b,
        global::Poly.DomainModeling.Ontology.Not n => IsProvablyAlwaysTrue(n.Operand),
        global::Poly.DomainModeling.Ontology.And a => IsProvablyAlwaysFalse(a.Left) || IsProvablyAlwaysFalse(a.Right),
        global::Poly.DomainModeling.Ontology.Or o => IsProvablyAlwaysFalse(o.Left) && IsProvablyAlwaysFalse(o.Right),
        Comparison { Kind: ComparisonKind.Equal, Left: Literal l, Right: Literal r } =>
            LiteralsEqual(l.Value, r.Value) is false,
        Comparison { Kind: ComparisonKind.NotEqual, Left: Literal l, Right: Literal r } =>
            LiteralsEqual(l.Value, r.Value) is true,
        Comparison { Kind: ComparisonKind.GreaterThanOrEqual, Left: Literal l, Right: Literal r }
            when CompareNumericLiterals(l.Value, r.Value) is { } order => order < 0,
        Comparison { Kind: ComparisonKind.GreaterThan, Left: Literal l, Right: Literal r }
            when CompareNumericLiterals(l.Value, r.Value) is { } order => order <= 0,
        Comparison { Kind: ComparisonKind.LessThanOrEqual, Left: Literal l, Right: Literal r }
            when CompareNumericLiterals(l.Value, r.Value) is { } order => order > 0,
        Comparison { Kind: ComparisonKind.LessThan, Left: Literal l, Right: Literal r }
            when CompareNumericLiterals(l.Value, r.Value) is { } order => order >= 0,
        _ => false
    };

    /// <summary>DFS back-edge cycle; returns stage names A → … → A when found.</summary>
    private static List<string>? FindCycle(Dictionary<string, HashSet<string>> adjacency) {
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new List<string>();

        List<string>? Dfs(string node) {
            if (visiting.Contains(node)) {
                var i = stack.IndexOf(node);
                var cycle = stack.Skip(i).ToList();
                cycle.Add(node);
                return cycle;
            }
            if (!visited.Add(node))
                return null;
            visiting.Add(node);
            stack.Add(node);
            if (adjacency.TryGetValue(node, out var targets)) {
                foreach (var t in targets) {
                    var cycle = Dfs(t);
                    if (cycle is not null)
                        return cycle;
                }
            }
            stack.RemoveAt(stack.Count - 1);
            visiting.Remove(node);
            return null;
        }

        foreach (var start in adjacency.Keys) {
            var cycle = Dfs(start);
            if (cycle is not null)
                return cycle;
        }
        return null;
    }
}