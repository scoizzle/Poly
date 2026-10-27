using System.Linq.Expressions;
using System.Reflection;

using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Abi;

public static partial class AbiEmitter {

    public sealed class Context {
        private readonly ParameterExpression _stateParam;
        private readonly List<ParameterExpression> _locals = new();
        private readonly LocalPool _pool;

        /// <summary>The seeded-handle local for <paramref name="runtimeType"/>, declared on
        /// first use. Two <c>typeof</c> sites for one type share it. The pool holds the
        /// local for the method.</summary>
        public ParameterExpression TypeHandleLocal(Type runtimeType) => _pool.TypeHandle(runtimeType);

        /// <summary>The type handles registered while compiling, in declaration order.</summary>
        public IEnumerable<KeyValuePair<Type, ParameterExpression>> TypeHandles => _pool.TypeHandles;

        public Context() {
            _stateParam = Parameter(typeof(VmState), "state");
            CurrentAstNodeExpr = Property(_stateParam, nameof(VmState.CurrentAstNode));
            SlotsLocal = Variable(typeof(long[]), "_slots");
            _locals.Add(SlotsLocal);
            HeapLocal = Variable(typeof(VmManagedObjectHeap), "_heap");
            _locals.Add(HeapLocal);
            FramePosLocal = Variable(typeof(int), "_fp");
            _locals.Add(FramePosLocal);
            InstanceHandle = Variable(typeof(long), "_this");
            _locals.Add(InstanceHandle);
            EntryLabel = Label("_entry");
            ExitLabel = Label("_exit");
            _pool = new LocalPool(_locals);
        }

        public ParameterExpression State => _stateParam;
        public ParameterExpression SlotsLocal { get; }
        public ParameterExpression HeapLocal { get; }
        public ParameterExpression FramePosLocal { get; }
        public ParameterExpression InstanceHandle { get; }
        public LabelTarget EntryLabel { get; }
        public LabelTarget ExitLabel { get; }
        public Expression? DebugHookProp { get; set; }
        public Expression CurrentAstNodeExpr { get; }
        /// <summary>Monotonic frame slot high-water. Debug-hook spans must cover this
        /// rather than a live count, because sequential inner blocks reuse neither
        /// slot 0 nor a dense 0..live-1 prefix.</summary>
        public int FrameSlotHighWater => _nextFrameSlot;
        public int StepCounter { get; set; }

        public LabelTarget RegisterOrGetResumeLabel(int step) {
            if (!_resumeLabels.TryGetValue(step, out var label)) {
                label = Label($"resume_{step}");
                _resumeLabels[step] = label;
            }
            return label;
        }

        internal bool IsResumeTarget(LabelTarget target) {
            foreach (var label in _resumeLabels.Values)
                if (ReferenceEquals(label, target))
                    return true;
            return false;
        }

        internal void EnterUnresumable() => _unresumable++;

        internal void LeaveUnresumable() => _unresumable--;

        /// <summary>Suspend inside try, catch, finally, using, or foreach cannot
        /// resume: the CLR will not jump into a try, and leaving runs finally.</summary>
        internal bool InUnresumable => _unresumable > 0;

        private readonly Dictionary<int, LabelTarget> _resumeLabels = new();
        private int _unresumable;

        public Expression EmitPcDispatch(Expression defaultBody) {
            if (_resumeLabels.Count == 0) return Empty();
            var cases = new System.Linq.Expressions.SwitchCase[_resumeLabels.Count];
            int i = 0;
            foreach (var (step, label) in _resumeLabels) {
                cases[i++] = System.Linq.Expressions.Expression.SwitchCase(Goto(label), Constant(step));
            }
            return IfThen(
                Equal(Property(_stateParam, nameof(VmState.Status)),
                    Constant(InterpreterStatus.Resuming)),
                System.Linq.Expressions.Expression.Switch(
                    Property(_stateParam, nameof(VmState.ProgramCounter)),
                    defaultBody, cases));
        }

        public Expression StatePcFlush => Property(_stateParam, "ProgramCounter");
        public CompilationMode Mode { get; set; }
        public Expression? FunctionTableExpr { get; set; }
        public bool IsCompiledFunctionBody { get; set; }
        /// <summary>True only while emitting the compilation-unit body, the one body that
        /// emits <see cref="EmitPcDispatch"/> and so the only one a resume can re-enter.
        /// <see cref="CompileFunctionBody"/> emits into its own <see cref="Context"/>, where
        /// this stays false.</summary>
        public bool IsRootBody { get; set; }
        public AnalysisResult? Analysis { get; set; }
        /// <summary>The declared <c>Task</c> / <c>Task&lt;T&gt;</c> return of an <c>IsAsync</c>
        /// function body being compiled; <see cref="EmitReturn"/> wraps every return in a
        /// completed task while it is set. Null for lambdas and non-async bodies.</summary>
        public Type? AsyncReturn { get; set; }
        public IReadOnlyList<ParameterExpression> Locals => _locals;

        public Expression SlotsInitExpression =>
            Property(Property(_stateParam, StateStackProperty), ValueStackRawSlotsProperty);
        public Expression HeapInitExpression =>
            Property(_stateParam, "Heap");
        public Expression ClosureHandle =>
            Property(_stateParam, StateClosureHandleProperty);
        public Expression SlotsStackPointer =>
            Property(Property(_stateParam, StateStackProperty), ValueStackStackPointerProperty);

        public int RingDepth {
            get => _pool.Depth;
            set => _pool.Depth = value;
        }
        public int MaxRingDepth => _pool.MaxDepth;

        /// <summary>Open a scoped ring region. Each <see cref="RingRegion.Add"/>
        /// folds its result into the next contiguous slot, so callers never
        /// hand-manage <see cref="RingDepth"/> or the fold.</summary>
        internal RingRegion OpenRing() => new(this);

        /// <summary>Open a ring region that closes when it is disposed: the depth returns
        /// to the caller's, so a sub-expression's slots cannot leak into the enclosing
        /// region and no emitter writes the cursor back by hand.</summary>
        internal RingScope OpenRingScope() => new(this, RingDepth);

        /// <summary>A ring region that restores the depth it opened at. It is a type
        /// rather than a callback so an emitter can hold it for a whole body
        /// (<c>using var scope = ctx.OpenRingScope();</c>) or for one compile.</summary>
        internal readonly struct RingScope : IDisposable {
            private readonly Context _context;
            private readonly int _callerDepth;

            internal RingScope(Context context, int baseDepth) {
                _context = context;
                _callerDepth = context.RingDepth;
                context.RingDepth = baseDepth;
            }

            public void Dispose() => _context.RingDepth = _callerDepth;
        }

        private readonly Dictionary<Node, (ValueRepresentationKind Kind, Type? ClrType)> _representation = new();

        /// <summary>The slot kind and CLR type of a node's effective runtime action.
        /// Analysis replacements carry the real action's type, so this is a pure
        /// read of the resolved type, memoized per emit.</summary>
        public (ValueRepresentationKind Kind, Type? ClrType) RepresentationOf(Node node) {
            if (_representation.TryGetValue(node, out var cached))
                return cached;
            var classified = ValueRepresentationClassifier.Classify(Analysis, node);
            _representation[node] = classified;
            return classified;
        }

        public ValueRepresentationKind KindOf(Node node) => RepresentationOf(node).Kind;

        public Type? ClrTypeOf(Node node) => RepresentationOf(node).ClrType;

        /// <summary>Contiguous ring slots for a sequence of compiled operands.
        /// <see cref="Add"/> compacts each result to <c>Base + count</c>; any
        /// slots the caller allocates afterwards extend the depth normally.</summary>
        internal sealed class RingRegion {
            private readonly Context _ctx;
            private readonly int _base;
            private readonly List<Expression> _steps = new();
            private int _count;

            internal RingRegion(Context ctx) {
                _ctx = ctx;
                _base = ctx.RingDepth;
            }

            /// <summary>The first slot of the region.</summary>
            public int Base => _base;

            /// <summary>Number of live values accumulated.</summary>
            public int Count => _count;

            /// <summary>The expressions that produce and fold the accumulated values.</summary>
            public IReadOnlyList<Expression> Steps => _steps;

            /// <summary>Compile-time slot holding the value added at <paramref name="index"/>.</summary>
            public int Slot(int index) => _base + index;

            /// <summary>Add a compiled value: fold its result into the next contiguous
            /// slot and leave <see cref="RingDepth"/> just past it. Returns its slot.
            /// <para>A region slot is a word. An IEEE value is converted once here, at
            /// this boundary, so every consumer that reads <c>RingVar(slot)</c> from a
            /// region sees the same shape whether or not the value was a double.</para></summary>
            public int Add(Expression compiled) {
                int result = _ctx.RingDepth - 1;
                int target = _base + _count;
                _steps.Add(compiled);
                if (result != target || RingCodec.IsTypedSlot(compiled.Type))
                    _steps.Add(Assign(_ctx.RingVar(target), _ctx.RingWord(result, compiled.Type)));
                _count++;
                _ctx.RingDepth = target + 1;
                return target;
            }

            /// <summary>Append a step that produces no ring value (a prelude or spill
            /// assignment). Does not touch <see cref="Count"/> or <see cref="RingDepth"/>.</summary>
            public void AddUncounted(Expression step) => _steps.Add(step);

            /// <summary>Assign <paramref name="value"/> into the next slot and count it —
            /// for a value that is not already on the ring (e.g. a boxed CLR expression),
            /// in its word form.</summary>
            public int AddValue(Expression value) {
                int target = _base + _count;
                _steps.Add(Assign(_ctx.RingVar(target), RingCodec.ToWord(value)));
                _count++;
                _ctx.RingDepth = target + 1;
                return target;
            }

            /// <summary>Run the accumulated steps, then <paramref name="body"/>.</summary>
            public Expression Then(Expression body) {
                var steps = new List<Expression>(_steps.Count + 1);
                steps.AddRange(_steps);
                steps.Add(body);
                return Block(steps);
            }
        }

        /// <summary>Declares a typed local in the outermost block for a lambda-bridge
        /// spill. The local is closed over by a nested delegate/expression tree, so it
        /// must live outside the invoke's expression sequence. The pool holds it for
        /// the method.</summary>
        public ParameterExpression DeclareBridgeLocal(Type type, string name) =>
            _pool.Bridge(type, name);

        public ParameterExpression RingVar(int absoluteIndex) =>
            RingVar(absoluteIndex, typeof(long));

        /// <summary>The ring local for <paramref name="absoluteIndex"/> at
        /// <paramref name="representation"/>, declared on first use.
        /// <para>A slot carries a representation, not just an index. Ring depth rewinds
        /// after every statement, so index 3 legitimately holds a <c>double</c> in one
        /// statement and a <c>long</c> in the next; one local per index would have to be
        /// assigned a value of the wrong type. An IEEE slot keeps the value itself, so
        /// no operation inside an expression has to bitcast it.</para></summary>
        public ParameterExpression RingVar(int absoluteIndex, Type representation) =>
            _pool.Ring(absoluteIndex, representation);

        /// <summary>The next ring slot, held for the caller. A block rewind releases it.</summary>
        public ParameterExpression HoldRing(Type representation) => _pool.HoldRing(representation);

        /// <summary>A helper local (<c>_coal</c>, <c>_shift</c>, <c>_callSp</c>,
        /// <c>_callerFp</c>). Dispose returns it to the pool.</summary>
        internal LocalPool.HelperLease LeaseHelper(Type type, string stem) =>
            _pool.LeaseHelper(type, stem);

        /// <summary>A scope whose <see cref="EmitScope.Yield"/> spills a value
        /// and whose dispose rewinds anything that was not yielded.</summary>
        internal EmitScope Scope() => new(this);

        /// <summary>Rewinds the ring to <paramref name="entryDepth"/> when disposed.
        /// A block disposes the previous statement's scope at the next statement
        /// and leaves the last one undisposed, so the block value stays put.</summary>
        internal StatementScope OpenStatement(int entryDepth) => new(this, entryDepth);

        internal sealed class StatementScope : IDisposable {
            private readonly Context _context;
            private readonly int _entry;
            private bool _hold;

            internal StatementScope(Context context, int entry) {
                _context = context;
                _entry = entry;
            }

            /// <summary>The block's value stays at the depth this statement left.</summary>
            internal void Hold() => _hold = true;

            public void Dispose() {
                if (!_hold)
                    _context.RingDepth = _entry;
            }
        }

        /// <summary>The word form of a ring slot. A <c>Nullable&lt;T&gt;</c> local
        /// boxes here; every other local uses <see cref="RingCodec.ToWord"/>.</summary>
        public Expression RingWord(int absoluteIndex, Type representation) =>
            RingCodec.ToFrame(RingVar(absoluteIndex, representation), representation, HeapLocal);

        /// <summary>The ring representation of a node's value: its own type for an IEEE or
        /// <c>int</c> scalar, the word for every other shape — including one analysis could
        /// not resolve, which is the storage fallback rather than a failure. (An integer
        /// <em>operation</em> with an unresolved width is a different question, and it fails
        /// closed: see <see cref="RingCodec.IntegerArithmeticWidth"/>.)</summary>
        public Type SlotTypeOf(Node node) {
            var (kind, clr) = RepresentationOf(node);
            if (clr is not null && Nullable.GetUnderlyingType(clr) is not null)
                return clr;
            return kind is ValueRepresentationKind.StackScalar ? RingCodec.SlotTypeOf(clr) : typeof(long);
        }

        public int AllocSlot() => _pool.Alloc();

        private readonly Stack<Dictionary<Variable, int>> _scopeStack = new();
        private readonly Dictionary<Variable, VariableLease> _variableLeases = new(ReferenceEqualityComparer.Instance);
        private readonly Stack<List<Variable>> _scopeVars = new();
        private int _nextFrameSlot;
        /// <summary>Simultaneous variable leases. The 33rd variable in a scope stays a
        /// frame word: it has no local to spill, and its word survives resume.</summary>
        private const int MaxVariableLeases = 32;
        private int _liveLeases;
        private readonly Dictionary<ParameterExpression, int> _spillSlots = new(ReferenceEqualityComparer.Instance);
        /// <summary>A ring index and the representation the variable's lease holds.
        /// The index is what a scope releases; the representation is what the local holds.</summary>
        private readonly record struct VariableLease(int Index, Type Representation);

        public void PushScope() {
            _scopeStack.Push(new Dictionary<Variable, int>(ReferenceEqualityComparer.Instance));
            _scopeVars.Push(new List<Variable>());
        }

        public void PopScope() {
            if (_scopeVars.Count > 0) {
                var frame = _scopeStack.Peek();
                foreach (var v in _scopeVars.Peek()) {
                    if (_variableLeases.TryGetValue(v, out var lease)) {
                        _variableLeases.Remove(v);
                        _liveLeases--;
                        _pool.ForgetPromoted(register: true, lease.Index);
                    }
                    if (frame.TryGetValue(v, out int slot))
                        _pool.ForgetPromoted(register: false, slot);
                }
            }
            _scopeStack.Pop();
            _scopeVars.Pop();
        }

        public IReadOnlyList<Expression> EmitScopeStores() {
            if (_scopeVars.Count == 0 || _scopeStack.Count == 0) return Array.Empty<Expression>();
            var vars = _scopeVars.Peek();
            var scope = _scopeStack.Peek();
            var stores = new List<Expression>(vars.Count);
            foreach (var v in vars) {
                if (_variableLeases.TryGetValue(v, out var lease) && scope.TryGetValue(v, out int slot)) {
                    stores.Add(new SlotLocation.Frame(slot).Write(this, LeaseLocal(lease)));
                }
            }
            return stores;
        }

        /// <summary>Store every live local that dies on resume, and build the matching
        /// reload. A frame-only binding is absent: its word already survives. A cell
        /// spills the handle local, never <c>cell[0]</c>. Each local is spilled once,
        /// to the variable's own frame slot or to a spill slot stable for that local.
        /// The store uses <see cref="SlotLocation.Write"/>, which is <see cref="LocalType.Encode"/>:
        /// <see cref="LocalType.ToWord"/> does not box a <c>Nullable&lt;T&gt;</c>.</summary>
        internal void SpillBarrier(int ringDepth, List<Expression> stores, List<Expression> reloads) {
            var seen = new HashSet<ParameterExpression>(ReferenceEqualityComparer.Instance);
            foreach (var scope in _scopeStack) {
                foreach (var (variable, frameSlot) in scope) {
                    if (!_variableLeases.TryGetValue(variable, out var lease))
                        continue;
                    var local = LeaseLocal(lease);
                    if (!seen.Add(local))
                        continue;
                    stores.Add(new SlotLocation.Frame(frameSlot).Write(this, local));
                    reloads.Add(Reload(local, frameSlot));
                }
            }
            foreach (var helper in _pool.LiveHelpers) {
                if (!seen.Add(helper))
                    continue;
                int frameSlot = SpillFrame(helper);
                stores.Add(new SlotLocation.Frame(frameSlot).Write(this, helper));
                reloads.Add(Reload(helper, frameSlot));
            }
            var ring = new List<ParameterExpression>();
            _pool.CollectBelow(ringDepth, ring);
            foreach (var local in ring) {
                if (!seen.Add(local))
                    continue;
                int frameSlot = SpillFrame(local);
                stores.Add(new SlotLocation.Frame(frameSlot).Write(this, local));
                reloads.Add(Reload(local, frameSlot));
            }
            // A promoted array cache is a CLR reference, not a frame word. Resume
            // zeros it. The heap object survives, so the reload reads it back from
            // the handle after that handle has been restored.
            foreach (var scope in _scopeStack) {
                foreach (var (variable, frameSlot) in scope) {
                    if (!TryArrayCache(variable, out var cache))
                        continue;
                    Expression handle = _variableLeases.TryGetValue(variable, out var lease)
                        ? LeaseLocal(lease)
                        : new SlotLocation.Frame(frameSlot).Read(this);
                    reloads.Add(Assign(cache, Convert(
                        Call(HeapLocal, HeapUnsafeGet, Convert(handle, typeof(int))),
                        cache.Type)));
                }
            }
        }

        private Expression Reload(ParameterExpression local, int frameSlot) {
            var word = new SlotLocation.Frame(frameSlot).Read(this);
            var form = LocalType.Of(local.Type);
            return Assign(local, form.FromWord(word, local.Type, HeapLocal));
        }

        private int SpillFrame(ParameterExpression local) {
            if (_spillSlots.TryGetValue(local, out int slot))
                return slot;
            slot = _nextFrameSlot++;
            _spillSlots[local] = slot;
            return slot;
        }

        /// <summary>
        /// Keep locals from colliding with root SetArgs slots (slot 0 = this,
        /// slots 1..N = declared root parameters). EmitScopeStores writes
        /// leased variables into <c>_slots[FP+slot]</c>; without this
        /// reserve, create0/create1 wipe parameter values before they are read.
        /// </summary>
        public void ReserveFrameSlots(int count) {
            if (count > _nextFrameSlot)
                _nextFrameSlot = count;
        }

        public void DeclareVariable(Variable v) {
            if (_scopeStack.Count == 0)
                throw new InvalidOperationException("No active scope");
            int slot = _nextFrameSlot++;
            _scopeStack.Peek()[v] = slot;
            _scopeVars.Peek().Add(v);
            _variableLayouts.Add(new VariableLayout(v.Name, slot, NeedsCell(v)));
            if (_liveLeases >= MaxVariableLeases)
                return;
            int index = _pool.Alloc();
            var representation = LeaseRepresentation(v);
            _pool.Ring(index, representation);
            _variableLeases[v] = new VariableLease(index, representation);
            _liveLeases++;
        }

        /// <summary>The representation a variable's lease holds: the variable's own
        /// value shape for an IEEE local, the <c>long</c> word for every other value — and
        /// the word for a cell-backed local, whose lease holds the capture cell's heap
        /// handle rather than the value.</summary>
        private Type LeaseRepresentation(Variable v) =>
            NeedsCell(v) ? typeof(long) : SlotTypeOf(v);

        /// <summary>The zero a declared local starts at, at its own representation.</summary>
        public Expression VariableZero(Variable v) {
            var representation = LeaseRepresentation(v);
            if (Nullable.GetUnderlyingType(representation) is not null)
                return Default(representation);
            return representation switch {
                var t when t == typeof(double) => Constant(0.0),
                var t when t == typeof(float) => Constant(0.0f),
                var t when t == typeof(int) => Constant(0),
                _ => Constant(0L),
            };
        }

        /// <summary>The leased local. The pool declares one local per (index, representation),
        /// so a scope that reuses an index at another type does not alias the parameter.</summary>
        private ParameterExpression LeaseLocal(VariableLease lease) =>
            _pool.Ring(lease.Index, lease.Representation);

        /// <summary>Bind a promoted variable's array to the typed CLR local that caches it,
        /// and return that local so the declaring block can clear it at scope exit.
        /// <para>The local is keyed by <c>(slot, element type)</c>, not by the variable:
        /// the rest of the emitter resolves bindings by slot, and a slot is released when
        /// its block ends and handed to the next sibling scope's variable. Two element
        /// types never share one local, because a <c>long[]</c> and an <c>object[]</c>
        /// cannot be assigned to the same <see cref="ParameterExpression"/>.</para></summary>
        public ParameterExpression PromoteArray(Variable v, Type elementType) {
            if (!TryOwnSlot(v, out var slot))
                throw Unresolved(v);
            return _pool.ArrayLocal(slot, elementType, ArrayLocalName(slot));
        }

        /// <summary>The cache local for the storage <paramref name="v"/> resolves to, or
        /// false when that storage was not promoted. The lookup goes through the same slot
        /// resolution a read or write uses, so a use that names the binding rather than
        /// being the declaring node still finds its cache.</summary>
        public bool TryArrayCache(Variable v, out ParameterExpression local) {
            if (TryOwnSlot(v, out var slot)
                && _pool.TryPromoted((slot.Register, slot.Index), out var found)) {
                local = found;
                return true;
            }
            local = null!;
            return false;
        }

        /// <summary>The storage slot a variable's own value lives in — the register index
        /// or the frame slot — which is what an array cache is keyed by. A use bound to a
        /// parameter resolves to that parameter's slot first (as
        /// <see cref="ResolveRaw"/> does), so it has no local storage to cache.</summary>
        private bool TryOwnSlot(Variable v, out (bool Register, int Index) slot) {
            slot = default;
            if (BoundParameter(v) is not null)
                return false;
            if (TryGetLease(v, out var lease)) {
                slot = (true, lease.Index);
                return true;
            }
            if (TryGetVariable(v, out int index)) {
                slot = (false, index);
                return true;
            }
            return false;
        }

        private static string ArrayLocalName((bool Register, int Index) slot) =>
            slot.Register ? $"_arr{slot.Index}" : $"_farr{slot.Index}";

        private readonly List<VariableLayout> _variableLayouts = new();
        public IReadOnlyList<VariableLayout> VariableLayouts => _variableLayouts;

        public bool IsDeclared(Variable v) =>
            TryGetVariable(v, out _);

        public bool TryGetVariable(Variable v, out int slot) {
            // Identity first: a Variable node is its binding, so a node that is a
            // declaration resolves to that declaration even when an inner scope
            // shadows the name.
            if (TryGetVariableIdentity(v, out slot))
                return true;
            // A reader emits a fresh Variable per identifier. The bind pass records
            // the declaration node; that node, not the name, is the binding.
            if (DeclaredVariable(v) is { } declaration && !ReferenceEquals(declaration, v)
                && TryGetVariableIdentity(declaration, out slot))
                return true;
            slot = -1;
            return false;
        }

        private bool TryGetVariableIdentity(Variable v, out int slot) {
            foreach (var scope in _scopeStack) {
                if (scope.TryGetValue(v, out slot))
                    return true;
            }
            slot = -1;
            return false;
        }

        private bool TryGetLease(Variable v, out VariableLease lease) {
            // Identity first: a Variable node is its binding, so a node that is a
            // declaration resolves to that declaration even when an inner scope
            // shadows the name.
            if (_variableLeases.TryGetValue(v, out lease))
                return true;
            // v is itself a declaration that did not take a lease (the frame-only
            // fallback past MaxVariableLeases). Do not follow a reference that points
            // back at v, and do not pick a same-named inner lease.
            foreach (var scope in _scopeStack) {
                if (scope.ContainsKey(v)) {
                    lease = default;
                    return false;
                }
            }
            if (DeclaredVariable(v) is { } declaration && !ReferenceEquals(declaration, v))
                return TryGetLease(declaration, out lease);
            lease = default;
            return false;
        }

        public int ParamSlotOffset { get; set; }

        /// <summary>One frame slot: <c>_slots[_fp + slot]</c>. The one address form
        /// for locals, parameters, results, and a promoted array's window.</summary>
        public Expression FrameSlotRead(int slot) =>
            ArrayAccess(SlotsLocal, Add(FramePosLocal, Constant(slot)));

        public Expression VariableRead(int varIndex) => FrameSlotRead(varIndex);

        public Expression VariableWrite(int varIndex, Expression value) =>
            Assign(FrameSlotRead(varIndex), value);

        /// <summary>Resolve a binding the way a reader or a writer sees it: a
        /// closure capture cell first, then its own storage. Capture precedence
        /// matters — a captured parameter is read from the closure, not from a
        /// parameter slot the closure body never declared.</summary>
        internal SlotLocation? Resolve(Variable v) =>
            TryGetCapture(v, out int captureIndex)
                ? new SlotLocation.Cell(CaptureCellArray(captureIndex))
                : ResolveStorage(v);

        /// <summary>The binding's own storage: bound-parameter slot, register, or
        /// frame slot, wrapped in its cell when the local is cell-backed. This is
        /// <see cref="VariableReadRaw"/>'s rule — no closure capture indirection.</summary>
        internal SlotLocation? ResolveStorage(Variable v) {
            var raw = ResolveRaw(v);
            if (raw is null)
                return null;
            return IsCellBacked(v)
                ? new SlotLocation.Cell(RingCodec.CellArray(HeapLocal, raw.Read(this)))
                : raw;
        }

        /// <summary>Resolve a parameter's storage, declaring its slot on first use.
        /// A pre-C1a root aliases parameter 0 to the instance handle.</summary>
        internal SlotLocation Resolve(Parameter p) {
            if (TryGetInlineParameter(p, out int inlineSlot))
                return new SlotLocation.Ring(inlineSlot);
            if (!TryGetParameterSlot(p, out int paramIndex))
                paramIndex = DeclareParameter(p);
            // Pre-C1a root programs: param 0 aliases SetArgs(this) via InstanceHandle.
            if (paramIndex == 0 && ParamSlotOffset == 0 && !HasInlineParameters && !IsCompiledFunctionBody)
                return new SlotLocation.Local(InstanceHandle, typeof(long));
            return new SlotLocation.Frame(paramIndex - ParamSlotOffset);
        }

        /// <summary>The binding's own storage, before any cell indirection.</summary>
        internal SlotLocation? ResolveRaw(Variable v) {
            if (BoundParameter(v) is { } parameter) {
                if (TryGetInlineParameter(parameter, out int inlineSlot))
                    return new SlotLocation.Ring(inlineSlot);
                return TryGetParameterSlot(parameter, out int paramIndex)
                    ? new SlotLocation.Frame(paramIndex - ParamSlotOffset)
                    : null;
            }
            if (TryGetLease(v, out var lease))
                return new SlotLocation.Local(LeaseLocal(lease), lease.Representation);
            return TryGetVariable(v, out int slotIndex)
                ? new SlotLocation.Frame(slotIndex)
                : null;
        }

        /// <summary>The <c>long[1]</c> capture cell at <paramref name="captureIndex"/>
        /// in the active closure array.</summary>
        internal Expression CaptureCellArray(int captureIndex) =>
            RingCodec.ClosureCell(HeapLocal, ClosureHandle, captureIndex);

        /// <summary>The binding's value as the <c>long</c> word, which is what a handle
        /// consumer (a closure cell, a bridge spill) needs. A typed register converts once
        /// here, at that boundary.</summary>
        public Expression VariableRead(Variable v) =>
            ResolveStorage(v) is { } storage
                ? RingCodec.ToWord(storage.Read(this))
                : throw Unresolved(v);

        /// <summary>The binding's value at <paramref name="representation"/>: the value
        /// itself when the storage already holds it, so a typed variable read is a copy.
        /// Capture-aware, like <see cref="Resolve(Variable)"/>: a captured binding reads
        /// through its cell.</summary>
        public Expression VariableReadAs(Variable v, Type representation) =>
            Resolve(v)?.ReadAs(this, representation) ?? throw Unresolved(v);

        public Expression VariableWrite(Variable v, Expression value) =>
            ResolveStorage(v)?.Write(this, value) ?? throw Unresolved(v);

        /// <summary>The binding's name in the one fail-closed error, distinguishing a
        /// bound parameter (which resolves through its slot) from a local.</summary>
        private InvalidOperationException Unresolved(Variable v) =>
            BoundParameter(v) is { } parameter
                ? new InvalidOperationException($"Parameter '{parameter.Name}' not declared in any scope")
                : new InvalidOperationException($"Variable '{v.Name}' not declared in any scope");

        public Expression ParameterRead(int paramIndex) =>
            FrameSlotRead(paramIndex - ParamSlotOffset);

        private Dictionary<Parameter, int>? _inlineParameters;
        private int _inlineParameterDepth;

        /// <summary>Enter an inline-lambda parameter scope. The depth keeps a
        /// parameter's slot from aliasing the instance handle while the body emits.</summary>
        public void PushInlineParameters() => _inlineParameterDepth++;

        public void PopInlineParameters() {
            if (_inlineParameterDepth > 0)
                _inlineParameterDepth--;
        }

        public void MapInlineParameter(Parameter parameter, int ringSlot) {
            _inlineParameters ??= new(ReferenceEqualityComparer.Instance);
            _inlineParameters[parameter] = ringSlot;
        }

        public bool TryGetInlineParameter(Parameter parameter, out int ringSlot) {
            if (_inlineParameters is { } map && map.TryGetValue(parameter, out ringSlot))
                return true;
            if (BoundDeclaredParameter(parameter) is { } declaration
                && !ReferenceEquals(declaration, parameter)
                && _inlineParameters is { } declared
                && declared.TryGetValue(declaration, out ringSlot))
                return true;
            ringSlot = 0;
            return false;
        }

        public void ClearInlineParameters() {
            _inlineParameters = null;
            _inlineParameterDepth = 0;
        }

        public bool HasInlineParameters => _inlineParameterDepth > 0;

        private readonly Stack<(LabelTarget breakLabel, LabelTarget continueLabel, string? name)> _loopScopes = new();

        public void PushLoopScope(LabelTarget breakLabel, LabelTarget continueLabel, string? name = null) {
            _loopScopes.Push((breakLabel, continueLabel, name));
        }

        public void PopLoopScope() => _loopScopes.Pop();

        public (LabelTarget breakLabel, LabelTarget continueLabel) ResolveLoopLabels(string? name) {
            if (_loopScopes.Count == 0)
                throw new InvalidOperationException("VM compile rejected: break/continue outside an enclosing loop.");
            if (name is null)
                return (_loopScopes.Peek().breakLabel, _loopScopes.Peek().continueLabel);
            foreach (var scope in _loopScopes) {
                if (scope.name == name)
                    return (scope.breakLabel, scope.continueLabel);
            }
            throw new InvalidOperationException(
                $"VM compile rejected: no enclosing loop with label '{name}'.");
        }

        private readonly Dictionary<string, LabelTarget> _labels = new();

        public LabelTarget GetLabel(string name) {
            if (!_labels.TryGetValue(name, out var target)) {
                target = Label(name);
                _labels[name] = target;
            }
            return target;
        }

        public bool HasLabel(string name) => _labels.ContainsKey(name);

        private readonly Dictionary<Parameter, int> _parameters = new(ReferenceEqualityComparer.Instance);
        private int _nextParamSlot;

        /// <summary>A node's CLR type, from the analysis only: the type pass owns
        /// resolution, and emit never peels a type-reference node by shape.</summary>
        public Type? TryClrType(Node node) =>
            Analysis?.GetResolvedType(node)?.GetRuntimeType();

        public int DeclareParameter(Parameter p) {
            int slot = _nextParamSlot++;
            _parameters[p] = slot;
            if (ParamSlotOffset == 0 && !HasInlineParameters) {
                while (_rootParameterClrTypes.Count <= slot)
                    _rootParameterClrTypes.Add(null);
                Type? t = TryClrType(p);
                if (t is null && p.TypeReference is not null)
                    t = TryClrType(p.TypeReference);
                _rootParameterClrTypes[slot] = t;
            }
            return slot;
        }

        private readonly List<Type?> _rootParameterClrTypes = [];
        public IReadOnlyList<Type?> RootParameterClrTypes => _rootParameterClrTypes;

        public bool TryGetParameterSlot(Parameter p, out int slot) {
            if (_parameters.TryGetValue(p, out slot))
                return true;
            if (BoundDeclaredParameter(p) is { } declaration
                && !ReferenceEquals(declaration, p)
                && _parameters.TryGetValue(declaration, out slot))
                return true;
            slot = 0;
            return false;
        }

        public int SaveAndResetParamSlots() {
            int saved = _nextParamSlot;
            _nextParamSlot = 0;
            return saved;
        }

        public void RestoreParamSlots(int saved) => _nextParamSlot = saved;

        public HashSet<object> CapturedBindings { get; set; } = new(ReferenceEqualityComparer.Instance);

        private readonly HashSet<object> _cellBacked = new(ReferenceEqualityComparer.Instance);

        public bool NeedsCell(Variable v) => CapturedBindings.Contains(v);

        public bool IsCellBacked(Variable v) => _cellBacked.Contains(v);

        public void MarkCellBacked(Variable v) => _cellBacked.Add(v);

        /// <summary>The parameter a use is bound to by the bind pass, or null for a
        /// local or an unbound use. The binding is the identity edge; no name lookup.</summary>
        public Parameter? BoundParameter(Variable variable) {
            if (Analysis?.GetMetadata<VariableAnalysisMetadata>(variable) is not { } metadata)
                return null;
            return metadata.ParameterReferences.TryGetValue(variable, out var parameter)
                ? parameter
                : null;
        }

        /// <summary>The declaration a use is bound to by the bind pass, or null when
        /// <paramref name="variable"/> is itself the declaration or the use is unbound.
        /// The binding is the identity edge; no name lookup.</summary>
        public Variable? DeclaredVariable(Variable variable) {
            if (Analysis?.GetMetadata<VariableAnalysisMetadata>(variable) is not { } metadata)
                return null;
            return metadata.VariableReferences.TryGetValue(variable, out var declaration)
                ? declaration
                : null;
        }

        /// <summary>The in-scope parameter a different <see cref="Parameter"/> instance
        /// was bound to, or null when <paramref name="parameter"/> is itself declared.</summary>
        public Parameter? BoundDeclaredParameter(Parameter parameter) =>
            Analysis?.GetMetadata<ParameterBindingMetadata>(parameter)?.Declaration;

        public Expression VariableReadRaw(Variable v) =>
            ResolveRaw(v) is { } storage
                ? RingCodec.ToWord(storage.Read(this))
                : throw Unresolved(v);

        public Expression VariableWriteRaw(Variable v, Expression value) =>
            ResolveRaw(v)?.Write(this, value) ?? throw Unresolved(v);

        private readonly Dictionary<object, int> _capturedBindings = new(ReferenceEqualityComparer.Instance);

        public void DeclareCapture(object binding, int captureIndex) {
            _capturedBindings[binding] = captureIndex;
        }

        public bool TryGetCapture(object binding, out int captureIndex) {
            if (_capturedBindings.TryGetValue(binding, out captureIndex))
                return true;
            if (binding is Parameter p) {
                foreach (var (key, idx) in _capturedBindings) {
                    if (key is Parameter cap && cap.Name == p.Name) {
                        captureIndex = idx;
                        return true;
                    }
                }
            }
            captureIndex = 0;
            return false;
        }

        private readonly List<Node?> _stepNodes = new();
        public IReadOnlyList<Node> StepNodes => _stepNodes.ToArray().Where(n => n is not null).Select(n => n!).ToList().AsReadOnly();

        public void RecordStepNode(int step, Node node) {
            while (_stepNodes.Count <= step)
                _stepNodes.Add(null);
            _stepNodes[step] = node;
        }

    }
}