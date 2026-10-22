using System.Linq.Expressions;
using System.Reflection;

using Poly.Ast.Nodes;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Abi;

/// <summary>
/// Frame-slot promotion for small fixed-size value-type arrays: a
/// <c>v = new long[n]</c> buffer whose every use is <c>v[i]</c> lives in the
/// frame's value-stack slots instead of a heap <c>long[]</c>. Nqueens PolyVM
/// (four <c>new long[8]</c> buffers, ~36k element accesses per run) otherwise
/// pays a heap deref plus a cast for each access, which is where its 1.4x
/// regression came from.
/// </summary>
/// <remarks>
/// Promotion is an escape analysis, not a representation trick: the variable's
/// own slot retains no synthetic handle, so a promoted buffer can never leak a
/// slot index where a heap handle is expected. A candidate is promoted only
/// when all of the following hold, and falls back to the heap array otherwise:
/// <list type="bullet">
/// <item>the array's element is an inline slot type and the length is a
/// constant in <c>[1, SmallArrayThreshold]</c>;</item>
/// <item>exactly one assignment targets the variable, and it is that
/// <c>new T[n]</c>;</item>
/// <item>every other reference is the receiver of an <see cref="IndexAccess"/>
/// (an element read or write) — no aliasing, capture, <c>.Length</c>, argument,
/// or return;</item>
/// <item>every use sits in the defining block's subtree, after the definition,
/// inside the same lambdas;</item>
/// <item>the variable's name is unique in the program. The emitter also
/// resolves variables by name, so a second <see cref="Variable"/> node with
/// the same name could address the promoted binding through the heap
/// path.</item>
/// </list>
/// </remarks>
public static partial class AbiEmitter {

    /// <summary>Collect the <see cref="Variable"/> bindings eligible for
    /// promotion in <paramref name="root"/>. Called once per compiled body,
    /// before emit, because the checks are whole-body.</summary>
    internal static HashSet<Variable> FindPromotableArrays(Node root, Context ctx) {
        var scan = new ArrayEscapeScan(ctx);
        // Two passes: definitions first, because a use may precede its definition
        // in traversal order and must still be classified.
        Walk(root, scan.CollectDefinitions);
        Walk(root, scan.ClassifyUses);
        return scan.Promotable();
    }

    private static void Walk(Node root, Action<Node, Node?, List<Node>> visit) {
        void Visit(Node node, Node? parent, List<Node> ancestors) {
            visit(node, parent, ancestors);
            ancestors.Add(node);
            foreach (var child in node.Children) {
                if (child is not null)
                    Visit(child, node, ancestors);
            }
            ancestors.RemoveAt(ancestors.Count - 1);
        }
        Visit(root, null, []);
    }

    private sealed class ArrayEscapeScan {
        private readonly Context _ctx;
        private readonly Dictionary<Variable, Facts> _facts = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, Variable> _nameOwner = new(StringComparer.Ordinal);
        private readonly HashSet<string> _sharedNames = new(StringComparer.Ordinal);
        private readonly HashSet<Variable> _rejected = new(ReferenceEqualityComparer.Instance);

        private sealed class Facts {
            public int Assignments;
            public Assignment? Definition;
            public Node? DefinitionParent;
            public List<Lambda> Lambdas = [];
        }

        internal ArrayEscapeScan(Context ctx) => _ctx = ctx;

        /// <summary>Pass 1: every write to a variable, and the one small-array
        /// definition a candidate needs.</summary>
        public void CollectDefinitions(Node node, Node? parent, List<Node> ancestors) {
            if (node is Variable variable) {
                if (!_nameOwner.TryGetValue(variable.Name, out var owner))
                    _nameOwner[variable.Name] = variable;
                else if (!ReferenceEquals(owner, variable))
                    _sharedNames.Add(variable.Name);
                return;
            }

            if (node is not Assignment { Destination: Variable destination } assignment)
                return;

            var facts = FactsFor(destination);
            facts.Assignments++;
            if (assignment.Value is NewArray newArray
                && TrySmallInlineArrayShape(newArray, _ctx, out _, out _)
                && facts.Definition is null) {
                facts.Definition = assignment;
                facts.DefinitionParent = parent;
                facts.Lambdas = [.. ancestors.OfType<Lambda>()];
            }
        }

        /// <summary>Pass 2: every reference that is neither a write nor a block
        /// declaration must be an element receiver inside the definition's block.</summary>
        public void ClassifyUses(Node node, Node? parent, List<Node> ancestors) {
            if (node is not Variable variable)
                return;
            if (!_facts.TryGetValue(variable, out var facts))
                return;   // not a candidate: no small-array definition anywhere
            if (parent is Assignment assignment && ReferenceEquals(assignment.Destination, variable))
                return;   // a write; the count in pass 1 decides
            if (parent is Block block && ContainsReference(block.Variables, variable))
                return;   // a scope declaration, not a use

            if (parent is IndexAccess access && ReferenceEquals(access.Value, variable)
                && InsideDefinitionBlock(facts, ancestors))
                return;
            _rejected.Add(variable);
        }

        private static bool ContainsReference(IReadOnlyList<Node> nodes, Node node) {
            foreach (var candidate in nodes) {
                if (ReferenceEquals(candidate, node))
                    return true;
            }
            return false;
        }

        private static bool InsideDefinitionBlock(Facts facts, List<Node> ancestors) {
            if (facts.Definition is null)
                return false;
            if (facts.DefinitionParent is not Block block)
                return false;   // a definition outside a block has no dominator to check against
            if (!SameLambdas(facts.Lambdas, ancestors))
                return false;

            // Reference equality: Node is a record, and its structural equality
            // must not decide which block a use belongs to.
            int at = ReferenceIndexOf(ancestors, block);
            if (at < 0 || at + 1 >= ancestors.Count)
                return false;   // the use is outside the defining block
            Node direct = ancestors[at + 1];
            int definitionIndex = IndexOf(block.Nodes, facts.Definition);
            int useIndex = IndexOf(block.Nodes, direct);
            return definitionIndex >= 0 && useIndex >= definitionIndex;
        }

        private static int ReferenceIndexOf(List<Node> path, Node node) {
            for (int i = 0; i < path.Count; i++) {
                if (ReferenceEquals(path[i], node))
                    return i;
            }
            return -1;
        }

        private static bool SameLambdas(List<Lambda> definition, List<Node> ancestors) {
            int i = 0;
            foreach (var node in ancestors) {
                if (node is not Lambda lambda)
                    continue;
                if (i >= definition.Count || !ReferenceEquals(definition[i], lambda))
                    return false;
                i++;
            }
            return i == definition.Count;
        }

        private static int IndexOf(IReadOnlyList<Node> nodes, Node? node) {
            for (int i = 0; i < nodes.Count; i++) {
                if (ReferenceEquals(nodes[i], node))
                    return i;
            }
            return -1;
        }

        private Facts FactsFor(Variable variable) {
            if (!_facts.TryGetValue(variable, out var facts))
                _facts[variable] = facts = new Facts();
            return facts;
        }

        public HashSet<Variable> Promotable() {
            var result = new HashSet<Variable>(ReferenceEqualityComparer.Instance);
            foreach (var (variable, facts) in _facts) {
                if (facts.Definition is null || facts.Assignments != 1)
                    continue;
                if (_rejected.Contains(variable) || _sharedNames.Contains(variable.Name))
                    continue;
                if (_ctx.NeedsCell(variable) || _ctx.TryGetCapture(variable, out _))
                    continue;
                if (_ctx.BoundParameter(variable) is not null)
                    continue;
                result.Add(variable);
            }
            return result;
        }
    }

    /// <summary>The fixed shape a promoted array must have: an inline value-type
    /// element (a scalar slot, IEEE bits included) and a constant small length.</summary>
    private static bool TrySmallInlineArrayShape(
        NewArray newArray, Context ctx, out int length, out Type elementClr) {
        length = 0;
        elementClr = typeof(long);
        if (newArray.Length is not Constant constant
            || !RingCodec.TryInlineConstant(constant.Value, out long raw)
            || raw <= 0 || raw > Context.SmallArrayThreshold)
            return false;
        var clr = ArrayElementClrType(newArray.ElementType, ctx);
        if (clr is not { IsValueType: true } || !RingCodec.IsInline(clr))
            return false;
        length = (int)raw;
        elementClr = clr;
        return true;
    }

    /// <summary>Zero a promoted window. Frame memory is reused across calls, so a
    /// fresh array's zeros cannot be assumed.</summary>
    private static Expression InitFrameLocalArray(SlotLocation.Window window, Context ctx) {
        var inits = new Expression[window.Length];
        for (int i = 0; i < window.Length; i++)
            inits[i] = Assign(ctx.FrameSlotRead(window.Base + i), Constant(0L));
        return Block(inits);
    }

    /// <summary>An element read in value form (no ring). The slot holds the same
    /// <c>long</c> a <c>long[]</c> element would, IEEE bits included, so the
    /// representation the caller sees is identical to the heap path.</summary>
    private static Expression ReadFrameLocalArray(
        IndexAccess n, SlotLocation.Window window, Context ctx) {
        Expression index = n.Arguments.Length > 0 ? CompileValue(n.Arguments[0], ctx) : Constant(0L);
        return window.ReadAt(ctx, index);
    }

    /// <summary>An element write in ring form: the array assignment's destination
    /// path. The stored value is the same coercion the <c>long[]</c> route applies.</summary>
    private static Expression WriteFrameLocalArray(
        IndexAccess destination, SlotLocation.Window window, Node valueNode, Context ctx) {
        var ring = ctx.OpenRing();
        int indexSlot = ring.Add(CompileNode(
            destination.Arguments.Length > 0 ? destination.Arguments[0] : new Constant(0), ctx));
        int valueSlot = ring.Add(CompileNode(valueNode, ctx));
        var store = window.WriteAt(ctx, ctx.RingVar(indexSlot),
            RingCodec.CoerceIndexStore(
                ctx.RingVar(valueSlot), window.ElementClr, RingCodec.IsIeee(valueNode, ctx.Analysis)));
        ctx.RingDepth = indexSlot + 1;
        return ring.Then(Block(store, Assign(ctx.RingVar(indexSlot), ctx.RingVar(valueSlot))));
    }
}