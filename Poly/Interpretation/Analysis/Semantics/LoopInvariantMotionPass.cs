using System.Runtime.CompilerServices;

namespace Poly.Interpretation.Analysis.Semantics;

/// <summary>
/// Loop-invariant motion for <c>while</c>. A pure arithmetic tree in the body whose
/// reads no statement in the loop assigns runs once, at the loop's position, and the
/// body reads the stored value. One level: a tree inside an inner <c>while</c> is the
/// inner loop's to lift. Runs after <see cref="AvailableExpressionPass"/> and lifts
/// from the loop that pass left.
/// </summary>
public sealed class LoopInvariantMotionPass : INodeAnalyzer {
    public const string Id = "LoopInvariantMotion";
    public string PassName => Id;

    private const int LocalLimit = 32;

    public void Analyze(AnalysisContext context, Node node) =>
        Visit(context, node, enclosing: null, new State());

    private sealed class State {
        public int Next;
        public Dictionary<Node, int> Added { get; } = new(ReferenceEqualityComparer.Instance);
    }

    private static void Visit(AnalysisContext context, Node node, Block? enclosing, State state) {
        var scope = node as Block ?? enclosing;
        foreach (var child in node.Children) {
            if (child is not null)
                Visit(context, child, scope, state);
        }

        if (node is WhileLoop loop)
            Lift(context, loop, enclosing, state);
    }

    private static void Lift(AnalysisContext context, WhileLoop original, Block? enclosing, State state) {
        if (context.GetEffectiveNode(original) is not WhileLoop loop)
            return;
        if (Blocked(context, loop.Condition) || Blocked(context, loop.Body))
            return;

        var assigned = new HashSet<Variable>();
        CollectAssigned(context, loop.Condition, assigned);
        CollectAssigned(context, loop.Body, assigned);

        var groups = new Dictionary<string, List<Node>>();
        Collect(context, loop.Body, assigned, groups);
        if (groups.Count == 0)
            return;

        var declared = enclosing is null ? 0 : context.GetEffectiveNode(enclosing) is Block effectiveBlock
            ? effectiveBlock.Variables.Count
            : enclosing.Variables.Count;
        var added = enclosing is not null && state.Added.TryGetValue(enclosing, out var count) ? count : 0;

        var temps = new List<Variable>();
        var stores = new List<Node>();
        var subst = new Dictionary<Node, Node>(ReferenceEqualityComparer.Instance);
        foreach (var group in groups.Values) {
            if (declared + added + temps.Count + 1 > LocalLimit)
                break;
            var temp = new Variable("li" + state.Next++);
            temps.Add(temp);
            if (context.GetResolvedType(group[0]) is { } type)
                context.SetResolvedType(temp, type);
            stores.Add(new Assignment(temp, group[0]));
            foreach (var occurrence in group)
                subst[occurrence] = temp;
        }
        if (temps.Count == 0)
            return;
        if (enclosing is not null)
            state.Added[enclosing] = added + temps.Count;

        var body = Substitute(context, loop.Body, subst);
        var rewritten = new WhileLoop(loop.Condition, body, loop.Label);
        // The chain ends at a fresh loop: the effective node cannot sit inside its own replacement.
        context.SetNodeReplacement(loop, new Block([.. stores, rewritten], [.. temps]));
    }

    /// <summary>
    /// A call, jump, or implicit binding in the loop can assign or leave in ways the
    /// assigned-set walk does not see. Fail closed.
    /// </summary>
    private static bool Blocked(AnalysisContext context, Node node) {
        var effective = context.GetEffectiveNode(node);
        if (effective is Invoke or Await or Lambda or ForEachLoop or TryCatchFinally or UsingStatement
            or BreakStatement or ContinueStatement or GotoStatement or LabelDeclaration or SuspendNode)
            return true;
        foreach (var child in effective.Children) {
            if (child is not null && Blocked(context, child))
                return true;
        }
        return false;
    }

    private static void CollectAssigned(AnalysisContext context, Node node, HashSet<Variable> assigned) {
        var effective = context.GetEffectiveNode(node);
        if (effective is Assignment { Destination: Variable destination })
            assigned.Add(Declaration(context, destination));
        foreach (var child in effective.Children) {
            if (child is not null)
                CollectAssigned(context, child, assigned);
        }
    }

    /// <summary>
    /// Maximal invariant pure trees. An inner <c>while</c> is opaque. Descent stops at any
    /// node <see cref="Substitute"/> cannot rebuild, so a candidate is always substitutable.
    /// </summary>
    private static void Collect(AnalysisContext context, Node node, HashSet<Variable> assigned, Dictionary<string, List<Node>> groups) {
        var effective = context.GetEffectiveNode(node);
        switch (effective) {
            case WhileLoop:
                return;
            case Block block:
                foreach (var statement in block.Nodes)
                    Collect(context, statement, assigned, groups);
                return;
            case Assignment assignment:
                Collect(context, assignment.Value, assigned, groups);
                return;
            case IfStatement ifStatement:
                Collect(context, ifStatement.Condition, assigned, groups);
                Collect(context, ifStatement.ThenBranch, assigned, groups);
                if (ifStatement.ElseBranch is not null)
                    Collect(context, ifStatement.ElseBranch, assigned, groups);
                return;
            case IndexAccess access:
                foreach (var argument in access.Arguments)
                    Collect(context, argument, assigned, groups);
                return;
            case Add or Subtract or Multiply or Divide:
                var reads = new HashSet<Variable>();
                if (Key(context, effective, reads) is { } key && !reads.Overlaps(assigned)) {
                    if (!groups.TryGetValue(key, out var list))
                        groups[key] = list = [];
                    list.Add(effective);
                    return;
                }
                break;
            case And or Or or Not or LessThan or LessThanOrEqual or GreaterThan or GreaterThanOrEqual or Equal or NotEqual:
                break;
            default:
                return;
        }

        foreach (var child in effective.Children) {
            if (child is not null)
                Collect(context, child, assigned, groups);
        }
    }

    /// <summary>
    /// Structural key of a pure trees of variables, constants and add, subtract, multiply
    /// and divide. A divide needs a nonzero constant divisor: moving it ahead of a loop that
    /// may not run must not add a fault. Null when anything else appears.
    /// </summary>
    private static string? Key(AnalysisContext context, Node node, HashSet<Variable> reads) {
        var effective = context.GetEffectiveNode(node);
        switch (effective) {
            case Constant constant:
                return "c:" + constant.Value?.GetType().Name + ":" + constant.Value;
            case Variable variable:
                var declaration = Declaration(context, variable);
                reads.Add(declaration);
                return "v:" + RuntimeHelpers.GetHashCode(declaration);
            case Add a:
                return Binary("add", context, a.LeftHandValue, a.RightHandValue, reads);
            case Subtract s:
                return Binary("sub", context, s.LeftHandValue, s.RightHandValue, reads);
            case Multiply m:
                return Binary("mul", context, m.LeftHandValue, m.RightHandValue, reads);
            case Divide d when context.GetEffectiveNode(d.RightHandValue) is Constant { Value: var divisor } && IsSafeDivisor(divisor):
                return Binary("div", context, d.LeftHandValue, d.RightHandValue, reads);
            default:
                return null;
        }
    }

    /// <summary>
    /// A non-positive divisor is not lifted. Zero faults, and <c>long.MinValue / -1</c>
    /// overflows; a loop that does not run must not gain that fault.
    /// </summary>
    private static bool IsSafeDivisor(object? value) => value switch {
        sbyte n => n > 0,
        short n => n > 0,
        int n => n > 0,
        long n => n > 0,
        byte or ushort or uint or ulong => Convert.ToDecimal(value) != 0m,
        float f => f > 0f && !float.IsNaN(f),
        double d => d > 0d && !double.IsNaN(d),
        _ => false,
    };

    private static string? Binary(string name, AnalysisContext context, Node left, Node right, HashSet<Variable> reads) {
        var leftKey = Key(context, left, reads);
        var rightKey = leftKey is null ? null : Key(context, right, reads);
        return leftKey is null || rightKey is null ? null : name + "(" + leftKey + "," + rightKey + ")";
    }

    /// <summary>Rebuilds only the spine to a substituted occurrence; an untouched node is returned as is.</summary>
    private static Node Substitute(AnalysisContext context, Node node, Dictionary<Node, Node> subst) {
        var effective = context.GetEffectiveNode(node);
        if (subst.TryGetValue(effective, out var temp))
            return temp;

        Node Sub(Node child) => Substitute(context, child, subst);
        Node Done(Node rebuilt) => Stamp(context, effective, rebuilt);
        Node Same(bool unchanged, Func<Node> make) => unchanged ? node : Done(make());

        switch (effective) {
            case Block block: {
                    var nodes = block.Nodes.Select(Sub).ToArray();
                    return Same(nodes.Zip(block.Nodes).All(pair => ReferenceEquals(pair.First, pair.Second)),
                        () => new Block(nodes, block.Variables));
                }
            case Assignment assignment: {
                    var value = Sub(assignment.Value);
                    return Same(ReferenceEquals(value, assignment.Value), () => new Assignment(assignment.Destination, value));
                }
            case IfStatement ifStatement: {
                    var condition = Sub(ifStatement.Condition);
                    var then = Sub(ifStatement.ThenBranch);
                    var otherwise = ifStatement.ElseBranch is null ? null : Sub(ifStatement.ElseBranch);
                    return Same(
                        ReferenceEquals(condition, ifStatement.Condition) && ReferenceEquals(then, ifStatement.ThenBranch)
                            && ReferenceEquals(otherwise, ifStatement.ElseBranch),
                        () => new IfStatement(condition, then, otherwise));
                }
            case IndexAccess access: {
                    var arguments = access.Arguments.Select(Sub).ToArray();
                    return Same(arguments.Zip(access.Arguments).All(pair => ReferenceEquals(pair.First, pair.Second)),
                        () => new IndexAccess(access.Value, arguments));
                }
            case Not not: {
                    var value = Sub(not.Value);
                    return Same(ReferenceEquals(value, not.Value), () => new Not(value));
                }
            case Add a: return Bin(a.LeftHandValue, a.RightHandValue, (x, y) => new Add(x, y));
            case Subtract s: return Bin(s.LeftHandValue, s.RightHandValue, (x, y) => new Subtract(x, y));
            case Multiply m: return Bin(m.LeftHandValue, m.RightHandValue, (x, y) => new Multiply(x, y));
            case Divide d: return Bin(d.LeftHandValue, d.RightHandValue, (x, y) => new Divide(x, y));
            case And a: return Bin(a.LeftHandValue, a.RightHandValue, (x, y) => new And(x, y));
            case Or o: return Bin(o.LeftHandValue, o.RightHandValue, (x, y) => new Or(x, y));
            case LessThan l: return Bin(l.LeftHandValue, l.RightHandValue, (x, y) => new LessThan(x, y));
            case LessThanOrEqual l: return Bin(l.LeftHandValue, l.RightHandValue, (x, y) => new LessThanOrEqual(x, y));
            case GreaterThan g: return Bin(g.LeftHandValue, g.RightHandValue, (x, y) => new GreaterThan(x, y));
            case GreaterThanOrEqual g: return Bin(g.LeftHandValue, g.RightHandValue, (x, y) => new GreaterThanOrEqual(x, y));
            case Equal e: return Bin(e.LeftHandValue, e.RightHandValue, (x, y) => new Equal(x, y));
            case NotEqual n: return Bin(n.LeftHandValue, n.RightHandValue, (x, y) => new NotEqual(x, y));
            default: return node;
        }

        Node Bin(Node left, Node right, Func<Node, Node, Node> make) {
            var l = Sub(left);
            var r = Sub(right);
            return Same(ReferenceEquals(l, left) && ReferenceEquals(r, right), () => make(l, r));
        }
    }

    private static Node Stamp(AnalysisContext context, Node original, Node rebuilt) {
        if (!ReferenceEquals(original, rebuilt) && context.GetResolvedType(original) is { } type)
            context.SetResolvedType(rebuilt, type);
        return rebuilt;
    }

    private static Variable Declaration(AnalysisContext context, Variable variable) {
        var meta = context.GetMetadata<VariableAnalysisMetadata>(variable);
        if (meta is not null && meta.VariableReferences.TryGetValue(variable, out var declaration) && declaration is not null)
            return declaration;
        return variable;
    }
}

public static class LoopInvariantMotionExtensions {
    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseLoopInvariantMotion() {
            builder.AddAnalyzer(new LoopInvariantMotionPass());
            return builder;
        }
    }
}