using System.Linq.Expressions;
using System.Text.RegularExpressions;

using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// Whole-tree reads and invariants over an emitted VM body.
///
/// The emitter's conventions are tree-wide: a ring slot is declared once by index,
/// a declared local is used somewhere, and a <c>_slots</c> element access is
/// addressed through the frame pointer. A per-emitter test cannot see any of that.
/// The ring copy-out removed in #253 lived in a block nested under the invoke, and a
/// helper that read only the outer block missed it entirely — so this walks every
/// block.
/// </summary>
internal static class VmTree {
    private static readonly Regex RingLocal = new("^_[rdf](\\d+)$", RegexOptions.Compiled);

    /// <summary>
    /// One emitted loop: the break target its test jumps to and the top-level items of
    /// the body the scaffold assembled, in emission order. A loop's own meaning — its
    /// test, its per-iteration binding, its body and what <c>continue</c> lands on — is
    /// what sits between the markers; the markers themselves are the scaffold.
    /// </summary>
    internal sealed class LoopShape {
        internal LoopShape(LabelTarget? breakTarget, IReadOnlyList<Expression> steps, bool inTryFinally) {
            BreakTarget = breakTarget;
            Steps = steps;
            InTryFinally = inTryFinally;
        }

        public LabelTarget? BreakTarget { get; }

        /// <summary>The loop body's top-level items: the guard, the test's jump, the
        /// loop's own statements, and the continue label — in the scaffold's order.</summary>
        public IReadOnlyList<Expression> Steps { get; }

        /// <summary>The loop sits inside a <c>try</c>: the <c>foreach</c> shape, whose
        /// enumerator the enclosing finally disposes.</summary>
        public bool InTryFinally { get; }

        /// <summary>Where the iteration guard sits: the first step of every scaffold.</summary>
        public int GuardIndex => IndexOf(IsIterationGuard);

        /// <summary>Where the test's jump to the break label sits. Before the body for
        /// <c>while</c>/<c>for</c>/<c>foreach</c>, after it for <c>do</c>.</summary>
        public int BreakTestIndex => IndexOf(IsBreakTest);

        /// <summary>Where the continue label sits: what <c>continue</c> jumps to.</summary>
        public int ContinueLabelIndex => IndexOf(IsContinueLabel);

        public LabelTarget? ContinueTarget =>
            ContinueLabelIndex < 0 ? null : ((LabelExpression)Steps[ContinueLabelIndex]).Target;

        /// <summary>How many guards the scaffold emitted: one. A second would be a second
        /// scaffold inside one loop, which is what a hand-kept copy looks like.</summary>
        public int GuardCount => Count(IsIterationGuard);

        public int BreakTestCount => Count(IsBreakTest);

        public int ContinueLabelCount => Count(IsContinueLabel);

        private bool IsIterationGuard(Expression step) =>
            step is ConditionalExpression condition && ReferencesMaxLoopIterations(condition.Test);

        /// <summary>The scaffold's jump always has a negated test, which keeps a body's own
        /// <c>if (x) break;</c> from reading as one.</summary>
        private bool IsBreakTest(Expression step) =>
            step is ConditionalExpression { Test.NodeType: ExpressionType.Not, IfTrue: GotoExpression jump }
            && jump.Target == BreakTarget;

        private bool IsContinueLabel(Expression step) =>
            step is LabelExpression label && label.Target != BreakTarget;

        private int IndexOf(Func<Expression, bool> predicate) {
            for (int i = 0; i < Steps.Count; i++) {
                if (predicate(Steps[i]))
                    return i;
            }
            return -1;
        }

        private int Count(Func<Expression, bool> predicate) => Steps.Count(predicate);

        private static bool ReferencesMaxLoopIterations(Expression expression) {
            var finder = new MaxLoopIterationsFinder();
            finder.Visit(expression);
            return finder.Found;
        }

        private sealed class MaxLoopIterationsFinder : ExpressionVisitor {
            public bool Found { get; private set; }

            protected override Expression VisitMember(MemberExpression node) {
                if (node.Member.Name == nameof(VmState.MaxLoopIterations))
                    Found = true;
                return base.VisitMember(node);
            }
        }
    }

    internal sealed class Scan : ExpressionVisitor {
        private ParameterExpression? _slots;
        private readonly List<Expression> _slotIndexes = [];
        private readonly List<int> _assignedRings = [];
        private readonly List<BinaryExpression> _ieeeArithmetic = [];
        private readonly List<BinaryExpression> _comparisons = [];
        private readonly HashSet<Expression> _bitcastArguments = new(ReferenceEqualityComparer.Instance);
        private readonly List<ParameterExpression> _declared = [];
        private readonly List<LoopShape> _loops = [];
        private readonly Dictionary<ParameterExpression, int> _occurrences =
            new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<ParameterExpression, int> _declarations =
            new(ReferenceEqualityComparer.Instance);
        private int _tryDepth;

        /// <summary>Every local declared in any block, in tree order.</summary>
        public IReadOnlyList<ParameterExpression> Declared => _declared;

        /// <summary>Every emitted loop, in tree order.</summary>
        public IReadOnlyList<LoopShape> Loops => _loops;

        protected override Expression VisitLoop(LoopExpression node) {
            var steps = node.Body switch {
                BlockExpression block => (IReadOnlyList<Expression>)block.Expressions,
                var single => [single],
            };
            _loops.Add(new LoopShape(node.BreakLabel, steps, _tryDepth > 0));
            return base.VisitLoop(node);
        }

        protected override Expression VisitTry(TryExpression node) {
            _tryDepth++;
            try {
                return base.VisitTry(node);
            }
            finally {
                _tryDepth--;
            }
        }

        protected override Expression VisitBlock(BlockExpression node) {
            foreach (var variable in node.Variables) {
                _declared.Add(variable);
                _declarations[variable] = _declarations.GetValueOrDefault(variable) + 1;
                if (variable.Name == "_slots")
                    _slots ??= variable;
            }
            return base.VisitBlock(node);
        }

        protected override Expression VisitParameter(ParameterExpression node) {
            _occurrences[node] = _occurrences.GetValueOrDefault(node) + 1;
            return base.VisitParameter(node);
        }

        protected override Expression VisitBinary(BinaryExpression node) {
            if (node.NodeType == ExpressionType.Assign
                && node.Left is ParameterExpression assigned
                && RingLocal.Match(assigned.Name ?? string.Empty) is { Success: true } ring)
                _assignedRings.Add(int.Parse(ring.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
            if (node.NodeType == ExpressionType.ArrayIndex && node.Left == _slots)
                _slotIndexes.Add(node.Right);
            if (IsIeeeArithmetic(node))
                _ieeeArithmetic.Add(node);
            if (node.NodeType is ExpressionType.Equal or ExpressionType.NotEqual
                or ExpressionType.LessThan or ExpressionType.LessThanOrEqual
                or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual)
                _comparisons.Add(node);
            return base.VisitBinary(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node) {
            if (IsBitcastCall(node)) {
                foreach (var argument in node.Arguments)
                    _bitcastArguments.Add(argument);
            }
            return base.VisitMethodCall(node);
        }

        internal static bool IsBitcastCall(Expression expression) =>
            VmTree.IsBitcastCall(expression);

        private static bool IsIeeeArithmetic(BinaryExpression node) =>
            node.NodeType is ExpressionType.Add or ExpressionType.Subtract
                or ExpressionType.Multiply or ExpressionType.Divide or ExpressionType.Modulo
            && (node.Type == typeof(double) || node.Type == typeof(float));

        /// <summary>Locals that never appear anywhere in the body: allocation that
        /// nothing uses, which is always waste rather than a budget to tune. Uses are
        /// occurrences minus declarations, because a tree walk may surface a block's
        /// declared variables as parameter visits of their own.</summary>
        public IReadOnlyList<ParameterExpression> DeadLocals =>
            _declared.Where(d =>
                _occurrences.GetValueOrDefault(d) - _declarations.GetValueOrDefault(d) == 0)
                .ToList();

        /// <summary>Indexes of the ring locals (<c>_rN</c> word, <c>_dN</c> double,
        /// <c>_fN</c> float), distinct and in declaration order. A slot index can
        /// declare more than one local — one per representation — because the ring
        /// depth rewinds between statements.</summary>
        public IReadOnlyList<int> RingIndexes =>
            _declared.Select(d => RingLocal.Match(d.Name ?? string.Empty))
                .Where(m => m.Success)
                .Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
                .Distinct()
                .OrderBy(index => index)
                .ToList();

        /// <summary>Indexes of the ring locals the body assigns, in assignment order.</summary>
        public IReadOnlyList<int> AssignedRingIndexes => _assignedRings;

        /// <summary>Ring locals declared for one index, one per representation.</summary>
        public IReadOnlyList<ParameterExpression> RingLocalsAt(int index) =>
            _declared.Where(d => RingLocal.Match(d.Name ?? string.Empty) is { Success: true } m
                && int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) == index)
                .ToList();

        /// <summary>Comparison nodes, in tree order. The emitter's own frame-pointer
        /// and status tests are comparisons too, so a consumer filters by shape.</summary>
        public IReadOnlyList<BinaryExpression> Comparisons => _comparisons;

        /// <summary>Every IEEE-typed arithmetic node (<c>+ - * / %</c> on
        /// <c>float</c>/<c>double</c>) in the body. A typed ring slot reads its operands
        /// as doubles, so these have a shape to check: their operands must not need a
        /// word.</summary>
        public IReadOnlyList<BinaryExpression> IeeeArithmetic => _ieeeArithmetic;

        /// <summary>The IEEE arithmetic nodes that convert at the operation itself: an
        /// operand is a bitcast call, or the node's own result is the direct argument of
        /// one. A typed ring slot reads its operand as the value, so these are the
        /// per-operation round trips the representation rule removes.</summary>
        public IReadOnlyList<BinaryExpression> IeeeArithmeticWithBitcasts =>
            _ieeeArithmetic.Where(operation =>
                IsBitcastCall(operation.Left)
                || IsBitcastCall(operation.Right)
                || _bitcastArguments.Contains(operation)).ToList();

        /// <summary>A <c>_slots[k]</c> access with a bare constant index: absolute
        /// frame addressing. Frame slots are addressed as <c>_slots[_fp + k]</c>, and
        /// the SetArgs path seeds a callee frame through a stack-pointer local, so
        /// only the constant form is a violation.</summary>
        public IReadOnlyList<Expression> AbsoluteSlotAccesses =>
            _slotIndexes.Where(i => i is ConstantExpression).ToList();
    }

    public static Scan Read(Expression body) {
        var scan = new Scan();
        scan.Visit(body);
        return scan;
    }

    /// <summary>True for a <c>BitConverter</c> long↔double bitcast call: the word form of
    /// an IEEE value, and the only conversion the representation rule still allows —
    /// at a word boundary, never between two operations.</summary>
    public static bool IsBitcastCall(Expression expression) =>
        expression is MethodCallExpression call
        && call.Method.DeclaringType == typeof(BitConverter)
        && call.Method.Name is nameof(BitConverter.Int64BitsToDouble)
            or nameof(BitConverter.DoubleToInt64Bits);

    /// <summary>How many long↔double bitcasts a subtree carries.</summary>
    public static int BitcastCount(Expression expression) {
        var finder = new BitcastFinder();
        finder.Visit(expression);
        return finder.Count;
    }

    private sealed class BitcastFinder : ExpressionVisitor {
        public int Count { get; private set; }

        protected override Expression VisitMethodCall(MethodCallExpression node) {
            if (IsBitcastCall(node))
                Count++;
            return base.VisitMethodCall(node);
        }
    }

    /// <summary>Fail loudly on any tree-wide violation.</summary>
    public static void AssertInvariants(AbiEmitter.BuiltVmProgram built) {
        var scan = Read(built.Expression);
        var problems = new List<string>();

        var dead = scan.DeadLocals.Select(d => d.Name).ToList();
        if (dead.Count > 0)
            problems.Add($"declared but unused: {string.Join(", ", dead)}");

        var rings = scan.RingIndexes;
        if (rings.Count > 0 && rings[^1] + 1 != built.MaxRingDepth)
            problems.Add($"deepest ring local is {rings[^1]} but MaxRingDepth is {built.MaxRingDepth}");
        for (int i = 0; i < rings.Count; i++) {
            if (rings[i] != i) {
                problems.Add($"ring locals are not the prefix 0..n: [{string.Join(",", rings)}]");
                break;
            }
        }

        if (scan.AbsoluteSlotAccesses.Count > 0)
            problems.Add($"{scan.AbsoluteSlotAccesses.Count} _slots access(es) with a constant index");

        if (problems.Count > 0)
            throw new InvalidOperationException(
                "VM tree invariant violated: " + string.Join("; ", problems));
    }
}