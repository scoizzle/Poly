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
    // LocalType's slot families, the names LocalPool.RingLocalName emits.
    // Nullable is _n{index}_{Underlying}. A variable lease uses the same names;
    // there is no _reg family.
    private static readonly Regex RingLocal = new("^_(?:[rdfi](\\d+)|n(\\d+)_\\w+)$", RegexOptions.Compiled);
    private static readonly Regex ArrayCacheLocal = new("^_f?arr(\\d+)$", RegexOptions.Compiled);

    private static bool TryRingIndex(string? name, out int index) {
        index = 0;
        if (name is null)
            return false;
        var match = RingLocal.Match(name);
        if (!match.Success)
            return false;
        var group = match.Groups[1].Success ? match.Groups[1] : match.Groups[2];
        index = int.Parse(group.Value, System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }

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
        private readonly HashSet<Expression> _boundaryBitcasts = new(ReferenceEqualityComparer.Instance);
        private readonly List<(MethodCallExpression Call, ParameterExpression Local)> _leaseBitcasts = [];
        private readonly HashSet<ParameterExpression> _zeroInits = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<ParameterExpression> _rejectedZero = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<ParameterExpression> _cellInits = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<ParameterExpression> _rejectedCell = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<ParameterExpression> _assignedRing = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<ParameterExpression, int> _ringAssigns = new(ReferenceEqualityComparer.Instance);
        private ParameterExpression? _pendingZero;
        private HashSet<ParameterExpression>? _leases;
        private readonly List<MethodCallExpression> _heapDerefs = [];
        private readonly List<MethodCallExpression> _callFrames = [];
        private readonly List<MethodCallExpression> _callFramesWithArgumentArray = [];
        private readonly List<MethodCallExpression> _heapAllocations = [];
        private readonly List<Expression> _arrayCacheAccesses = [];
        private readonly List<(ParameterExpression Local, string Value)> _arrayCacheStores = [];
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

        /// <summary>An element access is an <see cref="IndexExpression"/> whenever the
        /// index is a conversion rather than a bare <c>int</c>, which is the normal shape
        /// of a VM index.</summary>
        protected override Expression VisitIndex(IndexExpression node) {
            if (node.Object is ParameterExpression receiver && IsArrayCacheLocal(receiver))
                _arrayCacheAccesses.Add(node);
            return base.VisitIndex(node);
        }

        protected override Expression VisitParameter(ParameterExpression node) {
            _occurrences[node] = _occurrences.GetValueOrDefault(node) + 1;
            return base.VisitParameter(node);
        }

        protected override Expression VisitBinary(BinaryExpression node) {
            if (node.NodeType == ExpressionType.Assign)
                NoteRingAssign(node.Left, node.Right);
            if (node.NodeType == ExpressionType.Assign
                && node.Left is ParameterExpression assigned
                && TryRingIndex(assigned.Name, out int ring))
                _assignedRings.Add(ring);
            if (node.NodeType == ExpressionType.ArrayIndex && node.Left == _slots)
                _slotIndexes.Add(node.Right);
            if (node.NodeType == ExpressionType.Assign
                && IsFrameSlot(node.Left)) {
                // The observation copy is the sanctioned word boundary.
                if (IsBitcastCall(node.Right))
                    _boundaryBitcasts.Add(node.Right);
            }
            // A lease stored through a bitcast is the write half of a per-access
            // conversion. A typed lease stores the value itself.
            if (node.NodeType == ExpressionType.Assign
                && node.Left is ParameterExpression stored
                && TryRingIndex(stored.Name, out _)
                && node.Right is MethodCallExpression conversion
                && IsLeaseBitcast(conversion))
                NoteLeaseBitcast(conversion, stored);
            if (node.NodeType == ExpressionType.Assign
                && node.Left is ParameterExpression storedCache
                && IsArrayCacheLocal(storedCache))
                _arrayCacheStores.Add((storedCache,
                    node.Right is ConstantExpression { Value: null } ? "null" : "array"));
            if (node.NodeType == ExpressionType.ArrayIndex
                && node.Left is ParameterExpression cacheReceiver
                && IsArrayCacheLocal(cacheReceiver))
                _arrayCacheAccesses.Add(node);
            if (IsIeeeArithmetic(node))
                _ieeeArithmetic.Add(node);
            if (node.NodeType is ExpressionType.Equal or ExpressionType.NotEqual
                or ExpressionType.LessThan or ExpressionType.LessThanOrEqual
                or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual)
                _comparisons.Add(node);
            return base.VisitBinary(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node) {
            if (IsCallFrame(node)) {
                _callFrames.Add(node);
                if (node.Arguments.Any(argument => argument.NodeType == ExpressionType.NewArrayInit))
                    _callFramesWithArgumentArray.Add(node);
            }
            if (node.Method.DeclaringType == typeof(VmManagedObjectHeap)) {
                if (node.Method.Name == nameof(VmManagedObjectHeap.UnsafeGet))
                    _heapDerefs.Add(node);
                else if (node.Method.Name == nameof(VmManagedObjectHeap.Allocate))
                    _heapAllocations.Add(node);
            }
            if (IsBitcastCall(node)) {
                foreach (var argument in node.Arguments)
                    _bitcastArguments.Add(argument);
            }
            else {
                // A call argument is a word boundary too: the callee wants the CLR value.
                foreach (var argument in node.Arguments) {
                    if (IsBitcastCall(argument))
                        _boundaryBitcasts.Add(argument);
                }
            }
            if (IsBitcastCall(node)
                && !_boundaryBitcasts.Contains(node)
                && IsLeaseBitcast(node)
                && UnwrapLocal(node.Arguments[0]) is { } read)
                NoteLeaseBitcast(node, read);
            return base.VisitMethodCall(node);
        }

        private void NoteLeaseBitcast(MethodCallExpression call, ParameterExpression local) {
            if (_leaseBitcasts.Any(pair => ReferenceEquals(pair.Call, call) && ReferenceEquals(pair.Local, local)))
                return;
            _leaseBitcasts.Add((call, local));
        }

        /// <summary>A <c>_slots[_fp + n]</c> store. The emitter writes the frame
        /// through <see cref="Expression.ArrayAccess"/>, which is an
        /// <see cref="IndexExpression"/>, and through <c>ArrayIndex</c>.</summary>
        private bool IsFrameSlot(Expression expression) =>
            expression is IndexExpression { Object: ParameterExpression indexed } && indexed == _slots
            || expression is BinaryExpression { NodeType: ExpressionType.ArrayIndex, Left: ParameterExpression array }
                && array == _slots;

        private static ParameterExpression? UnwrapLocal(Expression? expression) {
            while (expression is UnaryExpression {
                NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked
            } unary)
                expression = unary.Operand;
            if (expression is MethodCallExpression call && IsBitcastCall(call) && call.Arguments.Count > 0)
                return UnwrapLocal(call.Arguments[0]);
            return expression as ParameterExpression;
        }

        /// <summary>A bitcast whose argument is a pool local, classified by
        /// <see cref="LocalType"/>. An IEEE local encodes with
        /// <c>DoubleToInt64Bits</c>. A word local decoded with
        /// <c>Int64BitsToDouble</c> is the per-access read a typed lease does
        /// not do. The name is not consulted: <c>_reg</c> is gone, and temps
        /// share the pool's names.</summary>
        private static bool IsLeaseBitcast(Expression expression) {
            if (expression is not MethodCallExpression call || !IsBitcastCall(call) || call.Arguments.Count == 0)
                return false;
            var local = UnwrapLocal(call.Arguments[0]);
            if (local is null || !TryRingIndex(local.Name, out _))
                return false;
            var form = LocalType.Of(local.Type);
            return call.Method.Name == nameof(BitConverter.DoubleToInt64Bits)
                ? form is LocalType.IEEELocal
                : form is not LocalType.IEEELocal;
        }

        /// <summary>Variable leases. A cell lease is the capture allocate. A value
        /// lease is the declaration zero that the program assigns again.
        /// <c>NoDebug</c> emits no observation copy, so a frame store cannot be the
        /// signal. A zero copied into another pool local, or a zero assigned only
        /// once, is a temp. A pool local reused for a later value after an allocate
        /// is a temp too.</summary>
        private HashSet<ParameterExpression> Leases {
            get {
                if (_leases is not null)
                    return _leases;
                _leases = new HashSet<ParameterExpression>(ReferenceEqualityComparer.Instance);
                foreach (var local in _zeroInits) {
                    if (!_rejectedZero.Contains(local)
                        && _ringAssigns.GetValueOrDefault(local) >= 2)
                        _leases.Add(local);
                }
                foreach (var local in _cellInits)
                    if (!_rejectedCell.Contains(local))
                        _leases.Add(local);
                return _leases;
            }
        }

        /// <summary>A declaration init is <c>local = zero</c> (or the cell
        /// handle's heap allocate). The value temp of an assignment is also
        /// <c>temp = zero</c> when the value is zero, and the next assign copies
        /// that temp into the lease. That copy is what keeps the temp out.</summary>
        private void NoteRingAssign(Expression left, Expression right) {
            if (_pendingZero is { } pending) {
                if (left is ParameterExpression target
                    && target != pending
                    && TryRingIndex(target.Name, out _)
                    && UnwrapLocal(right) == pending)
                    _rejectedZero.Add(pending);
                _pendingZero = null;
            }
            if (left is ParameterExpression counted && TryRingIndex(counted.Name, out _))
                _ringAssigns[counted] = _ringAssigns.GetValueOrDefault(counted) + 1;
            if (left is ParameterExpression overwritten
                && _cellInits.Contains(overwritten)
                && !IsHeapAllocate(right))
                _rejectedCell.Add(overwritten);
            if (left is not ParameterExpression local || !TryRingIndex(local.Name, out _) || !_assignedRing.Add(local))
                return;
            if (IsZeroConstant(right)) {
                _zeroInits.Add(local);
                _pendingZero = local;
            }
            else if (IsHeapAllocate(right))
                _cellInits.Add(local);
        }

        private static bool IsZeroConstant(Expression expression) => expression switch {
            ConstantExpression { Value: 0 or 0L or 0.0 or 0f or false } => true,
            DefaultExpression => true,
            _ => false,
        };

        private static bool IsHeapAllocate(Expression expression) {
            while (expression is UnaryExpression unary)
                expression = unary.Operand;
            return expression is MethodCallExpression { Method.Name: "Allocate", Method.DeclaringType: { } type }
                && type == typeof(VmManagedObjectHeap);
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
        /// <c>_fN</c> float, <c>_iN</c> int, <c>_nN_T</c> nullable), distinct and in
        /// declaration order. A slot index can
        /// declare more than one local — one per representation — because the ring
        /// depth rewinds between statements.</summary>
        public IReadOnlyList<int> RingIndexes =>
            _declared.Select(d => TryRingIndex(d.Name, out int index) ? index : -1)
                .Where(index => index >= 0)
                .Distinct()
                .OrderBy(index => index)
                .ToList();

        /// <summary>Indexes of the ring locals the body assigns, in assignment order.</summary>
        public IReadOnlyList<int> AssignedRingIndexes => _assignedRings;

        /// <summary>Ring locals declared for one index, one per representation.</summary>
        public IReadOnlyList<ParameterExpression> RingLocalsAt(int index) =>
            _declared.Where(d => TryRingIndex(d.Name, out int parsed) && parsed == index)
                .ToList();

        /// <summary>The variable leases the body declares, in tree order. A lease is a
        /// pool local the observation copy stores. Statement temps share the pool's
        /// names and are not leases. An index reused by a later scope declares one
        /// local per <see cref="LocalType"/>.</summary>
        public IReadOnlyList<ParameterExpression> Registers =>
            _declared.Where(Leases.Contains).Distinct().ToList();

        /// <summary>Bitcasts at a variable lease: a bitcast that reads a lease, or is
        /// the value assigned to one, classified by <see cref="LocalType"/> rather than
        /// by a <c>_reg</c> name. The two sanctioned word
        /// boundaries are excluded — the block-exit <c>_slots</c> snapshot store and a CLR
        /// call argument — because those convert once per crossing rather than per access.
        /// A typed variable has none of these.</summary>
        public IReadOnlyList<MethodCallExpression> RegisterBitcasts =>
            _leaseBitcasts.Where(pair => Leases.Contains(pair.Local))
                .Select(pair => pair.Call)
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

        /// <summary>The locals that cache a promoted array's object — the emitter's
        /// <c>_arrN</c> (register slot) / <c>_farrN</c> (frame slot) convention, in
        /// declaration order.</summary>
        public IReadOnlyList<ParameterExpression> ArrayCacheLocals =>
            _declared.Where(IsArrayCacheLocal).ToList();

        /// <summary>Every store to an array-cache local, in tree order, with the kind of
        /// value stored: the array at its definition (<c>array</c>) or the scope-exit
        /// clear (<c>null</c>). The order is what says a cleared local cannot reach the
        /// next block that reuses the slot.</summary>
        public IReadOnlyList<(ParameterExpression Local, string Value)> ArrayCacheStores =>
            _arrayCacheStores;

        /// <summary>Element accesses (<c>_arrN[i]</c>) through a cache local, in tree
        /// order. A promoted read or write is one of these and carries no heap deref.</summary>
        public IReadOnlyList<Expression> ArrayCacheAccesses => _arrayCacheAccesses;

        /// <summary>Every <c>heap.UnsafeGet</c> call in the body: the deref a promoted
        /// array access must not have.</summary>
        public IReadOnlyList<MethodCallExpression> HeapDerefs => _heapDerefs;

        /// <summary>Every <c>heap.Allocate</c> call in the body. One array definition is
        /// one allocation, so a promoted definition does not add a second.</summary>
        public IReadOnlyList<MethodCallExpression> HeapAllocations => _heapAllocations;

        /// <summary>A call frame: the emitter's <c>InvokeAstFrame</c> / <c>InvokeAstFunction</c>
        /// hop, whatever its arity. Its argument words are the frame's parameters.</summary>
        internal static bool IsCallFrame(MethodCallExpression call) =>
            call.Method.DeclaringType == typeof(AbiEmitter)
            && call.Method.Name is "InvokeAstFrame" or "InvokeAstFunction";

        /// <summary>Every emitted call frame, in tree order.</summary>
        public IReadOnlyList<MethodCallExpression> CallFrames => _callFrames;

        /// <summary>The call frames that materialise a <c>long[]</c> to carry their
        /// arguments. A frame should name its words one by one instead.</summary>
        public IReadOnlyList<MethodCallExpression> CallFramesWithArgumentArray =>
            _callFramesWithArgumentArray;

        internal static bool IsArrayCacheLocal(Expression expression) =>
            expression is ParameterExpression parameter
            && ArrayCacheLocal.IsMatch(parameter.Name ?? string.Empty);
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