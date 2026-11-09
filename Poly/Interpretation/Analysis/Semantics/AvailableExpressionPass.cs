using System.Runtime.CompilerServices;

using Poly.Introspection;

namespace Poly.Interpretation.Analysis.Semantics;

/// <summary>
/// Available-expression elimination inside one straight-line region.
/// A pure operator tree that runs twice with no kill between the uses becomes
/// one store and a read of that store. <c>and</c> does not hoist a value that
/// exists only on the right.
/// </summary>
public sealed class AvailableExpressionPass : INodeAnalyzer {
    public const string Id = "AvailableExpressions";
    public string PassName => Id;

    public void Analyze(AnalysisContext context, Node node) {
        var loans = new List<PlaceLoan>();
        Analyze(context, node, isRoot: true, loans);
        context.SetMetadata(node, new PlaceLoanMetadata(loans));
    }

    private void Analyze(AnalysisContext context, Node node, bool isRoot, List<PlaceLoan> loans) {
        foreach (var child in node.Children) {
            if (child is not null)
                Analyze(context, child, isRoot: false, loans);
        }

        if (node is Block block)
            Rewrite(context, block.Nodes, block, loans);
        else if (isRoot)
            Rewrite(context, [node], anchor: node, loans);
    }

    private static void Rewrite(AnalysisContext context, IReadOnlyList<Node> statements, Node anchor, List<PlaceLoan> loans) {
        var region = new Region(loans);
        var extraTemps = new List<Variable>();
        foreach (var statement in statements) {
            if (statement is WhileLoop loop) {
                extraTemps.AddRange(RewriteTrueEdge(context, loop.Condition, loop.Body, asLoop: true, loop.Label, elseBody: null, loans, out var replaced));
                if (replaced is not null)
                    context.SetNodeReplacement(loop, replaced);
                region.Clear();
            }
            else if (statement is IfStatement ifStatement) {
                extraTemps.AddRange(RewriteTrueEdge(
                    context, ifStatement.Condition, ifStatement.ThenBranch, asLoop: false, label: null, ifStatement.ElseBranch, loans, out var replaced));
                if (replaced is not null)
                    context.SetNodeReplacement(ifStatement, replaced);
                region.Clear();
            }
            else if (statement is Block or ForLoop or DoWhileLoop or ForEachLoop or SwitchStatement)
                region.Clear();
            else
                Walk(context, statement, region);
        }
        Apply(context, region, anchor, extraTemps);
    }

    /// <summary>
    /// A value computed on every path that makes the test true is available in the
    /// body until the body kills it. The store stays in that evaluation. The else
    /// arm and the right of <c>or</c> do not see it.
    /// </summary>
    private static List<Variable> RewriteTrueEdge(
        AnalysisContext context, Node condition, Node thenBody, bool asLoop, string? label, Node? elseBody, List<PlaceLoan> loans, out Node? replaced) {
        replaced = null;
        var region = new Region(loans);
        CollectTrueEdge(context, condition, region, storeable: false, entered: true);
        WalkBody(context, thenBody, region);
        var groups = region.Reusable()
            .Where(group => group.Any(occurrence => Contains(condition, occurrence)))
            .Where(group => !region.Reusable().Any(inner => !ReferenceEquals(inner, group) && Contains(group[0], inner[0])))
            .ToList();
        if (groups.Count == 0)
            return [];

        var temps = new List<Variable>();
        var subst = new Dictionary<Node, Node>(ReferenceEqualityComparer.Instance);
        var stores = new List<(Variable Temp, Node First)>();
        foreach (var group in groups) {
            var temp = new Variable("te" + temps.Count);
            temps.Add(temp);
            if (context.GetResolvedType(group[0]) is { } type)
                context.SetResolvedType(temp, type);
            stores.Add((temp, group[0]));
            foreach (var occurrence in group)
                subst[occurrence] = temp;
        }

        var newCondition = PlaceStores(context, condition, subst, stores);
        var newBody = subst.Keys.Any(key => Contains(thenBody, key))
            ? Rebuild(context, thenBody, subst)
            : thenBody;
        replaced = asLoop
            ? new WhileLoop(newCondition, newBody, label)
            : new IfStatement(newCondition, newBody, elseBody);
        return temps;
    }

    /// <summary>
    /// A temp is noted only where <see cref="PlaceStores"/> will assign it: an <c>and</c>
    /// that pass enters. An <c>and</c> under <c>or</c> or <c>not</c> is not entered, and
    /// an unstored temp reads as zero.
    /// </summary>
    private static void CollectTrueEdge(AnalysisContext context, Node node, Region region, bool storeable, bool entered) {
        if (context.GetNodeReplacement(node) is not null)
            return;
        var effective = context.GetEffectiveNode(node);
        switch (effective) {
            case And when entered:
                CollectTrueEdge(context, ((And)effective).LeftHandValue, region, storeable: true, entered: true);
                CollectTrueEdge(context, ((And)effective).RightHandValue, region, storeable: true, entered: true);
                return;
            case And and:
                CollectTrueEdge(context, and.LeftHandValue, region, storeable: false, entered: false);
                CollectTrueEdge(context, and.RightHandValue, region, storeable: false, entered: false);
                return;
            case Or or:
                CollectTrueEdge(context, or.LeftHandValue, region, storeable: false, entered: false);
                return;
            case Not not:
                CollectTrueEdge(context, not.Value, region, storeable: false, entered: false);
                return;
            case Assignment assignment:
                CollectTrueEdge(context, assignment.Value, region, storeable, entered: false);
                Kill(context, assignment.Destination, region);
                return;
        }

        foreach (var child in effective.Children) {
            if (child is not null)
                CollectTrueEdge(context, child, region, storeable, entered: false);
        }

        if (storeable && ValueNumber.TryCreate(context, effective, out var number))
            region.Note(number, node);
    }

    private static void WalkBody(AnalysisContext context, Node body, Region region) {
        if (body is Block block) {
            foreach (var statement in block.Nodes) {
                if (statement is Assignment assignment) {
                    Walk(context, assignment.Value, region);
                    Kill(context, assignment.Destination, region);
                }
                else if (statement is Block or WhileLoop or IfStatement or ForLoop or DoWhileLoop or ForEachLoop or SwitchStatement)
                    region.Clear();
                else
                    Walk(context, statement, region);
            }
            return;
        }
        Walk(context, body, region);
    }

    private static Node PlaceStores(
        AnalysisContext context, Node node, Dictionary<Node, Node> subst, List<(Variable Temp, Node First)> stores) {
        if (node is And and) {
            var rightStores = stores.Where(store => Contains(and.RightHandValue, store.First)).ToList();
            var leftStores = stores.Where(store => Contains(and.LeftHandValue, store.First)).ToList();
            var right = PlaceStores(context, and.RightHandValue, subst, rightStores);
            if (rightStores.Count > 0)
                right = new Block([.. rightStores.Select(store => new Assignment(store.Temp, store.First)), right]);
            var left = PlaceStores(context, and.LeftHandValue, subst, leftStores);
            if (leftStores.Count > 0)
                left = new Block([.. leftStores.Select(store => new Assignment(store.Temp, store.First)), left]);
            return Stamp(context, node, new And(left, right));
        }
        return Rebuild(context, node, subst);
    }

    private static Node Stamp(AnalysisContext context, Node original, Node rebuilt) {
        if (!ReferenceEquals(original, rebuilt) && context.GetResolvedType(original) is { } type)
            context.SetResolvedType(rebuilt, type);
        return rebuilt;
    }

    private static void Walk(AnalysisContext context, Node node, Region region) {
        if (context.GetNodeReplacement(node) is not null)
            return;
        var effective = context.GetEffectiveNode(node);
        switch (effective) {
            case And and:
                Walk(context, and.LeftHandValue, region);
                var right = region.Child();
                Walk(context, and.RightHandValue, right);
                Apply(context, right, and.RightHandValue);
                if (ReachesExternal(and))
                    region.Clear();
                return;
            case Or or:
                Walk(context, or.LeftHandValue, region);
                region.Clear();
                Walk(context, or.RightHandValue, region);
                region.Clear();
                return;
            case Assignment assignment:
                Walk(context, assignment.Value, region);
                Kill(context, assignment.Destination, region);
                return;
            case Block or WhileLoop or IfStatement or ForLoop or DoWhileLoop or ForEachLoop or SwitchStatement:
                region.Clear();
                return;
            case Invoke or Await or New:
                region.Clear();
                return;
        }

        foreach (var child in effective.Children) {
            if (child is not null)
                Walk(context, child, region);
        }

        if (ValueNumber.TryCreate(context, effective, out var number))
            region.Note(number, node);
    }

    private static void Kill(AnalysisContext context, Node destination, Region region) {
        if (destination is Variable variable)
            region.Kill(Declaration(context, variable));
        else if (destination is IndexAccess { Value: Variable array } write)
            region.KillIndexWrite(Declaration(context, array), write, ValueNumber.KeysOf(context, write));
        else
            region.Clear();
    }

    private static void Apply(AnalysisContext context, Region region, Node anchor, List<Variable>? extraTemps = null) {
        var groups = DropUnrebuildable(region.Reusable(), anchor is Block anchorBlock ? anchorBlock.Nodes : new Node[] { anchor });
        extraTemps ??= [];
        if (groups.Count == 0 && extraTemps.Count == 0)
            return;

        var temps = new List<Variable>();
        var subst = new Dictionary<Node, Node>(ReferenceEqualityComparer.Instance);
        var stores = new List<Node>();
        foreach (var group in groups) {
            var temp = new Variable("av" + temps.Count);
            temps.Add(temp);
            if (context.GetResolvedType(group[0]) is { } type)
                context.SetResolvedType(temp, type);
            // The original node keeps its analyzed type. A clone would not.
            stores.Add(new Assignment(temp, group[0]));
            foreach (var occurrence in group)
                subst[occurrence] = temp;
        }

        if (anchor is Block block) {
            foreach (var statement in block.Nodes) {
                if (!Mentions(statement, subst))
                    continue;
                var localStores = stores.Where(store => store is Assignment { Value: var value } && Contains(statement, value)).ToArray();
                context.SetNodeReplacement(statement, new Block([.. localStores, Rebuild(context, statement, subst)]));
            }
            context.SetNodeReplacement(block, new Block(block.Nodes, [.. block.Variables, .. temps, .. extraTemps]));
            return;
        }
        if (context.GetNodeReplacement(anchor) is { } existing) {
            if (extraTemps.Count > 0)
                context.SetNodeReplacement(anchor, new Block([existing], [.. extraTemps]));
            return;
        }
        if (groups.Count == 0)
            return;

        context.SetNodeReplacement(anchor, new Block([.. stores, Rebuild(context, anchor, subst)], temps));
    }

    /// <summary>
    /// <see cref="Rebuild"/> only descends the node kinds it lists; any other kind comes back as itself.
    /// A replacement whose body is its own original node re-enters the emitter forever, and a
    /// substitution beneath such a kind is silently lost. A group with an occurrence under one is
    /// not rewritten, so every rewritten statement is rebuilt from nodes <see cref="Rebuild"/> can reach.
    /// </summary>
    private static List<List<Node>> DropUnrebuildable(List<List<Node>> groups, IReadOnlyList<Node> roots) {
        var kept = groups;
        while (kept.Count > 0) {
            var targets = new HashSet<Node>(kept.SelectMany(group => group), ReferenceEqualityComparer.Instance);
            var blocked = new HashSet<Node>(ReferenceEqualityComparer.Instance);
            foreach (var root in roots)
                FindBlocked(root, targets, blocked);
            if (blocked.Count == 0)
                return kept;
            kept = kept.Where(group => !group.Any(blocked.Contains)).ToList();
        }
        return kept;
    }

    private static void FindBlocked(Node node, HashSet<Node> targets, HashSet<Node> blocked) {
        if (targets.Contains(node) || !targets.Any(target => Contains(node, target)))
            return;
        if (!IsRebuildable(node)) {
            foreach (var target in targets.Where(target => Contains(node, target)))
                blocked.Add(target);
            return;
        }
        foreach (var child in node.Children) {
            if (child is not null)
                FindBlocked(child, targets, blocked);
        }
    }

    /// <summary>The kinds <see cref="Rebuild"/> reconstructs. Keep in step with its switch.</summary>
    private static bool IsRebuildable(Node node) =>
        node is Multiply or Add or Subtract or Divide or IndexAccess or And or LessThan or LessThanOrEqual
            or ShiftRight or Assignment or Block;

    private static bool ReachesExternal(Node node) {
        if (node is Invoke or Await or New)
            return true;
        foreach (var child in node.Children) {
            if (child is not null && ReachesExternal(child))
                return true;
        }
        return false;
    }

    private static bool Mentions(Node node, Dictionary<Node, Node> subst) =>
        subst.ContainsKey(node) || node.Children.Any(child => child is not null && Mentions(child, subst));

    private static bool Contains(Node node, Node target) =>
        ReferenceEquals(node, target) || node.Children.Any(child => child is not null && Contains(child, target));

    private static Node Rebuild(AnalysisContext context, Node node, Dictionary<Node, Node> subst) {
        if (subst.TryGetValue(node, out var replaced))
            return replaced;
        Node rebuilt = node switch {
            Multiply m => new Multiply(Rebuild(context, m.LeftHandValue, subst), Rebuild(context, m.RightHandValue, subst)),
            Add a => new Add(Rebuild(context, a.LeftHandValue, subst), Rebuild(context, a.RightHandValue, subst)),
            Subtract s => new Subtract(Rebuild(context, s.LeftHandValue, subst), Rebuild(context, s.RightHandValue, subst)),
            Divide d => new Divide(Rebuild(context, d.LeftHandValue, subst), Rebuild(context, d.RightHandValue, subst)),
            IndexAccess ix => RebuildIndex(context, ix, subst),
            And a => new And(Rebuild(context, a.LeftHandValue, subst), Rebuild(context, a.RightHandValue, subst)),
            LessThan l => new LessThan(Rebuild(context, l.LeftHandValue, subst), Rebuild(context, l.RightHandValue, subst)),
            LessThanOrEqual l => new LessThanOrEqual(Rebuild(context, l.LeftHandValue, subst), Rebuild(context, l.RightHandValue, subst)),
            ShiftRight s => new ShiftRight(Rebuild(context, s.LeftHandValue, subst), Rebuild(context, s.RightHandValue, subst)),
            Assignment a => new Assignment(Rebuild(context, a.Destination, subst), Rebuild(context, a.Value, subst)),
            Block b => new Block(b.Nodes.Select(child => Rebuild(context, child, subst)).ToArray(), b.Variables),
            _ => node,
        };
        return Stamp(context, node, rebuilt);
    }

    private static Node RebuildIndex(AnalysisContext context, IndexAccess access, Dictionary<Node, Node> subst) {
        var value = Rebuild(context, access.Value, subst);
        var arguments = access.Arguments.Select(argument => Rebuild(context, argument, subst)).ToArray();
        var unchanged = ReferenceEquals(value, access.Value)
            && arguments.Zip(access.Arguments).All(pair => ReferenceEquals(pair.First, pair.Second));
        return unchanged ? access : new IndexAccess(value, arguments);
    }

    private static Variable Declaration(AnalysisContext context, Variable variable) {
        var meta = context.GetMetadata<VariableAnalysisMetadata>(variable);
        if (meta is not null && meta.VariableReferences.TryGetValue(variable, out var declaration) && declaration is not null)
            return declaration;
        return variable;
    }

    private sealed class Region {
        private readonly Dictionary<ValueNumber, List<Node>> _occurrences = [];
        private readonly List<List<Node>> _sealed = [];
        private readonly List<PlaceLoan> _loans;

        public Region(List<PlaceLoan> loans) {
            _loans = loans;
        }

        public Region Child() => new(_loans);

        public string Dump() =>
            "live=" + string.Join(" | ", _occurrences.Select(pair => pair.Value.Count + ":" + pair.Key.Key))
            + " sealed=" + _sealed.Count;

        public void Note(ValueNumber number, Node node) {
            if (!_occurrences.TryGetValue(number, out var list))
                _occurrences[number] = list = [];
            list.Add(node);
        }

        /// <summary>
        /// A later use is a new evaluation. Uses already recorded stay reusable:
        /// the body may read the condition's value and then assign the binding.
        /// </summary>
        public void Kill(Variable declaration) {
            foreach (var number in _occurrences.Keys.Where(n => n.Reads.Contains(declaration)).ToArray()) {
                Seal(number, _occurrences[number]);
                _occurrences.Remove(number);
            }
        }

        /// <summary>
        /// An element write drops every non-element number the way <see cref="Clear"/> does.
        /// An element read of the same array stays only when both keys are constants and
        /// differ; any other element read may alias the written one (another variable can
        /// hold the same array), so it is sealed and dropped.
        /// </summary>
        public void KillIndexWrite(Variable array, IndexAccess write, List<ValueNumber>? writeKeys) {
            var preserved = new List<Node>();
            foreach (var pair in _occurrences.ToArray()) {
                if (pair.Key.IsPlace && ReferenceEquals(pair.Key.Binding, array) && ValueNumber.ProvablyDistinct(pair.Key, writeKeys)) {
                    preserved.Add(pair.Value[0]);
                    continue;
                }
                Seal(pair.Key, pair.Value);
                _occurrences.Remove(pair.Key);
            }
            if (preserved.Count > 0)
                _loans.Add(new PlaceLoan(array, write, preserved));
        }

        public void Clear() {
            foreach (var pair in _occurrences)
                Seal(pair.Key, pair.Value);
            _occurrences.Clear();
        }

        public List<List<Node>> Reusable() {
            foreach (var pair in _occurrences)
                Seal(pair.Key, pair.Value);
            _occurrences.Clear();
            return _sealed;
        }

        private void Seal(ValueNumber number, List<Node> list) {
            if (number.HasOperator && list.Count >= 2)
                _sealed.Add(list);
        }
    }

    private sealed class ValueNumber : IEquatable<ValueNumber> {
        private ValueNumber(string key, HashSet<Variable> reads, bool hasOperator) {
            Key = key;
            Reads = reads;
            HasOperator = hasOperator;
        }

        public string Key { get; }
        public HashSet<Variable> Reads { get; }
        public bool HasOperator { get; }

        /// <summary>An element read: its array binding and per-dimension key numbers.</summary>
        public bool IsPlace { get; private init; }
        public Variable? Binding { get; private init; }
        public List<ValueNumber> Keys { get; private init; } = [];

        /// <summary>A constant key: the value is what two keys compare on.</summary>
        public bool IsConstant { get; private init; }
        public object? ConstantValue { get; private init; }

        /// <summary>Key numbers of a written element, or null when any key is not numberable.</summary>
        public static List<ValueNumber>? KeysOf(AnalysisContext context, IndexAccess access) {
            var keys = new List<ValueNumber>();
            foreach (var argument in access.Arguments) {
                if (!TryCreate(context, argument, out var key))
                    return null;
                keys.Add(key);
            }
            return keys;
        }

        /// <summary>Equal numbers are equal keys. Only two unequal integral constants prove distinct elements.</summary>
        public static bool ProvablyDistinct(ValueNumber read, List<ValueNumber>? writeKeys) {
            if (writeKeys is null || writeKeys.Count != read.Keys.Count)
                return false;
            for (var index = 0; index < writeKeys.Count; index++) {
                var readKey = read.Keys[index];
                var writeKey = writeKeys[index];
                if (readKey.IsConstant && writeKey.IsConstant
                    && IsIntegral(readKey.ConstantValue) && IsIntegral(writeKey.ConstantValue)
                    && Convert.ToDecimal(readKey.ConstantValue) != Convert.ToDecimal(writeKey.ConstantValue))
                    return true;
            }
            return false;
        }

        private static bool IsIntegral(object? value) =>
            value is sbyte or byte or short or ushort or int or uint or long or ulong;

        private static bool TryIndex(AnalysisContext context, IndexAccess access, out ValueNumber number) {
            number = null!;
            if (access.Value is not Variable array || access.Arguments.Length == 0)
                return false;
            if (context.GetResolvedMember(array)?.Mutability.HasFlag(Mutability.VolatileAccess) == true)
                return false;
            if (context.GetResolvedType(array) is not { } arrayType
                || !arrayType.TryGetRuntimeType(out var runtimeType)
                || !runtimeType.IsArray)
                return false;
            var keys = KeysOf(context, access);
            if (keys is null)
                return false;
            var declaration = Declaration(context, array);
            var reads = new HashSet<Variable> { declaration };
            foreach (var key in keys)
                reads.UnionWith(key.Reads);
            number = new ValueNumber(
                "idx(" + RuntimeHelpers.GetHashCode(declaration) + ";" + string.Join(",", keys.Select(key => key.Key)) + ")",
                reads,
                hasOperator: true) {
                IsPlace = true,
                Binding = declaration,
                Keys = keys,
            };
            return true;
        }

        public static bool TryCreate(AnalysisContext context, Node node, out ValueNumber number) {
            number = null!;
            switch (node) {
                case Constant constant:
                    number = new ValueNumber("c:" + constant.Value, [], false) {
                        IsConstant = true,
                        ConstantValue = constant.Value,
                    };
                    return true;
                case IndexAccess access:
                    return TryIndex(context, access, out number);
                case Variable variable:
                    var declaration = Declaration(context, variable);
                    number = new ValueNumber("v:" + RuntimeHelpers.GetHashCode(declaration), [declaration], false);
                    return true;
                case Multiply m when Parts(context, m.LeftHandValue, m.RightHandValue, out var mulLeft, out var mulRight):
                    number = Op("mul", mulLeft, mulRight);
                    return true;
                case Add a when Parts(context, a.LeftHandValue, a.RightHandValue, out var addLeft, out var addRight):
                    number = Op("add", addLeft, addRight);
                    return true;
                case Subtract s when Parts(context, s.LeftHandValue, s.RightHandValue, out var subLeft, out var subRight):
                    number = Op("sub", subLeft, subRight);
                    return true;
                case Divide d when Parts(context, d.LeftHandValue, d.RightHandValue, out var divLeft, out var divRight):
                    number = Op("div", divLeft, divRight);
                    return true;
                default:
                    return false;
            }
        }

        private static ValueNumber Op(string name, ValueNumber left, ValueNumber right) {
            var reads = new HashSet<Variable>(left.Reads);
            reads.UnionWith(right.Reads);
            return new ValueNumber(name + "(" + left.Key + "," + right.Key + ")", reads, true);
        }

        private static bool Parts(AnalysisContext context, Node left, Node right, out ValueNumber leftNumber, out ValueNumber rightNumber) {
            leftNumber = null!;
            rightNumber = null!;
            return TryCreate(context, left, out leftNumber)
                && TryCreate(context, right, out rightNumber);
        }

        public bool Equals(ValueNumber? other) => other is not null && Key == other.Key;
        public override bool Equals(object? obj) => obj is ValueNumber other && Equals(other);
        public override int GetHashCode() => Key.GetHashCode(StringComparison.Ordinal);
    }
}

public static class AvailableExpressionExtensions {
    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseAvailableExpressions() {
            builder.AddAnalyzer(new AvailableExpressionPass());
            return builder;
        }
    }
}