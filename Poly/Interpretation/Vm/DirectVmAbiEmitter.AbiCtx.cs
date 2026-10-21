using System.Linq.Expressions;
using System.Reflection;

using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm;

public static partial class DirectVmAbiEmitter {

    public sealed class AbiCtx {
        private readonly ParameterExpression _stateParam;
        private readonly List<ParameterExpression> _ringVars = new();
        private readonly List<ParameterExpression> _locals = new();

        public AbiCtx() : this(8) { }

        public AbiCtx(int registerCount) {
            _registerCount = Math.Clamp(registerCount, 8, MaxRegisterCount);
            _regUsed = new bool[_registerCount];
            _stateParam = Parameter(typeof(VmState), "state");
            ProgramCounter = Variable(typeof(int), "_pc");
            _locals.Add(ProgramCounter);
            SlotsLocal = Variable(typeof(long[]), "_slots");
            _locals.Add(SlotsLocal);
            HeapLocal = Variable(typeof(Heap), "_heap");
            _locals.Add(HeapLocal);
            FramePosLocal = Variable(typeof(int), "_fp");
            _locals.Add(FramePosLocal);
            InstanceHandle = Variable(typeof(long), "_this");
            _locals.Add(InstanceHandle);
            _regVars = new List<ParameterExpression>(_registerCount);
            for (int i = 0; i < _registerCount; i++) {
                var r = Variable(typeof(long), $"_reg{i}");
                _regVars.Add(r);
                _locals.Add(r);
            }
            EntryLabel = Label("_entry");
            ExitLabel = Label("_exit");
        }

        public ParameterExpression State => _stateParam;
        public ParameterExpression ProgramCounter { get; }
        public ParameterExpression SlotsLocal { get; }
        public ParameterExpression HeapLocal { get; }
        public ParameterExpression FramePosLocal { get; }
        public ParameterExpression InstanceHandle { get; }
        public LabelTarget EntryLabel { get; }
        public LabelTarget ExitLabel { get; }
        public Expression? DebugHookProp { get; set; }
        public Expression CurrentAstNodeExpr =>
            Property(_stateParam, nameof(VmState.CurrentAstNode));
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

        private readonly Dictionary<int, LabelTarget> _resumeLabels = new();

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
        public int RegisterCount => _registerCount;
        public Expression? FunctionTableExpr { get; set; }
        public bool IsCompiledFunctionBody { get; set; }
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

        public int RingDepth { get; set; }
        private int _maxDepth;
        public int MaxRingDepth => _maxDepth;

        /// <summary>Open a scoped ring region. Each <see cref="RingRegion.Add"/>
        /// folds its result into the next contiguous slot, so callers never
        /// hand-manage <see cref="RingDepth"/> or the fold.</summary>
        internal RingRegion OpenRing() => new(this);

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
            private readonly AbiCtx _ctx;
            private readonly int _base;
            private readonly List<Expression> _steps = new();
            private int _count;

            internal RingRegion(AbiCtx ctx) {
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
            /// slot and leave <see cref="RingDepth"/> just past it. Returns its slot.</summary>
            public int Add(Expression compiled) {
                int result = _ctx.RingDepth - 1;
                int target = _base + _count;
                _steps.Add(compiled);
                if (result != target)
                    _steps.Add(Assign(_ctx.RingVar(target), _ctx.RingVar(result)));
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

        public ParameterExpression RingVar(int absoluteIndex) {
            while (_ringVars.Count <= absoluteIndex) {
                var v = Variable(typeof(long), $"_r{_ringVars.Count}");
                _ringVars.Add(v);
                _locals.Add(v);
            }
            if (absoluteIndex + 1 > _maxDepth)
                _maxDepth = absoluteIndex + 1;
            return _ringVars[absoluteIndex];
        }

        public int AllocSlot() {
            int slot = RingDepth;
            RingDepth = slot + 1;
            RingVar(slot);
            return slot;
        }

        private readonly Stack<Dictionary<Variable, int>> _scopeStack = new();
        private readonly Dictionary<Variable, int> _variableRegisters = new(ReferenceEqualityComparer.Instance);
        private readonly Stack<List<Variable>> _scopeVars = new();
        private int _nextFrameSlot;
        private const int MaxRegisterCount = 32;
        private int _registerCount;
        private readonly List<ParameterExpression> _regVars;
        private bool[] _regUsed;

        public void PushScope() {
            _scopeStack.Push(new Dictionary<Variable, int>(ReferenceEqualityComparer.Instance));
            _scopeVars.Push(new List<Variable>());
        }

        public void PopScope() {
            if (_scopeVars.Count > 0) {
                foreach (var v in _scopeVars.Peek()) {
                    if (_variableRegisters.TryGetValue(v, out int regIdx)) {
                        _regUsed[regIdx] = false;
                        _variableRegisters.Remove(v);
                    }
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
                if (_variableRegisters.TryGetValue(v, out int regIdx) && scope.TryGetValue(v, out int slot)) {
                    stores.Add(Assign(
                        ArrayAccess(SlotsLocal, Add(FramePosLocal, Constant(slot))),
                        _regVars[regIdx]));
                }
            }
            return stores;
        }

        /// <summary>
        /// Keep locals from colliding with root SetArgs slots (slot 0 = this,
        /// slots 1..N = declared root parameters). EmitScopeStores writes
        /// register-backed variables into <c>_slots[FP+slot]</c>; without this
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
            int regIdx = -1;
            while (regIdx < 0) {
                for (int i = 0; i < _registerCount; i++) {
                    if (!_regUsed[i]) { regIdx = i; break; }
                }
                if (regIdx < 0 && _registerCount < MaxRegisterCount) {
                    GrowRegisterFile();
                }
                else {
                    break;
                }
            }
            if (regIdx < 0) {
                _scopeVars.Peek().Add(v);
                _variableLayouts.Add(new VariableLayout(v.Name, slot, NeedsCell(v)));
                return;
            }
            _regUsed[regIdx] = true;
            _variableRegisters[v] = regIdx;
            _scopeVars.Peek().Add(v);
            _variableLayouts.Add(new VariableLayout(v.Name, slot, NeedsCell(v)));
        }

        private void GrowRegisterFile() {
            int old = _registerCount;
            int grown = Math.Min(old + 8, MaxRegisterCount);
            var newRegUsed = new bool[grown];
            Array.Copy(_regUsed, newRegUsed, old);
            _regUsed = newRegUsed;
            for (int i = old; i < grown; i++) {
                var r = Variable(typeof(long), $"_reg{i}");
                _regVars.Add(r);
                _locals.Add(r);
            }
            _registerCount = grown;
        }

        private readonly List<VariableLayout> _variableLayouts = new();
        public IReadOnlyList<VariableLayout> VariableLayouts => _variableLayouts;

        public bool IsDeclared(Variable v) =>
            _variableRegisters.ContainsKey(v) || TryGetVariable(v, out _);

        public bool TryGetVariable(Variable v, out int slot) {
            // Identity first: a Variable node is its binding, so a node that is a
            // declaration resolves to that declaration even when an inner scope
            // shadows the name.
            foreach (var scope in _scopeStack) {
                if (scope.TryGetValue(v, out slot))
                    return true;
            }
            // Name fallback from the innermost scope outward: catch VariableName is
            // a string and body refs are new Variable(name), and a shadowing
            // declaration must beat an outer binding of the same name.
            foreach (var scope in _scopeStack) {
                foreach (var (key, s) in scope) {
                    if (key.Name == v.Name) {
                        slot = s;
                        return true;
                    }
                }
            }
            slot = -1;
            return false;
        }

        private bool TryGetRegister(Variable v, out int regIdx) {
            // Identity first: a Variable node is its binding, so a node that is a
            // declaration resolves to that declaration even when an inner scope
            // shadows the name.
            if (_variableRegisters.TryGetValue(v, out regIdx))
                return true;
            // If v is itself a declaration in some scope, the register fallback must not
            // guess by name: a same-named inner binding could then shadow it here while
            // the scope-aware TryGetVariable still resolves v by identity (reads and
            // writes would disagree). Report not-found so the caller uses that path.
            foreach (var scope in _scopeStack) {
                if (scope.ContainsKey(v)) {
                    regIdx = -1;
                    return false;
                }
            }
            // Name fallback from the innermost scope outward. A flat name scan over
            // _variableRegisters is unordered and would let an outer shadowed binding
            // win; the innermost declaration of the name is the correct binding.
            foreach (var vars in _scopeVars) {
                foreach (var key in vars) {
                    if (!string.Equals(key.Name, v.Name, StringComparison.Ordinal))
                        continue;
                    // The innermost binding with this name decides. If it is
                    // slot-backed (no free register), report not-found so the caller
                    // falls through to the scope-aware TryGetVariable.
                    if (_variableRegisters.TryGetValue(key, out regIdx))
                        return true;
                    regIdx = -1;
                    return false;
                }
            }
            regIdx = -1;
            return false;
        }

        public int ParamSlotOffset { get; set; }

        public Expression VariableRead(int varIndex) =>
            ArrayAccess(SlotsLocal, Add(FramePosLocal, Constant(varIndex)));

        public Expression VariableRead(Variable v) {
            var raw = VariableReadRaw(v);
            if (!IsCellBacked(v))
                return raw;
            return RingCodec.CellRead(HeapLocal, raw);
        }

        public Expression VariableWrite(int varIndex, Expression value) =>
            Assign(VariableRead(varIndex), value);

        public Expression VariableWrite(Variable v, Expression value) {
            if (IsCellBacked(v))
                return RingCodec.CellWrite(HeapLocal, VariableReadRaw(v), value);
            return VariableWriteRaw(v, value);
        }

        public Expression ParameterRead(int paramIndex) =>
            ArrayAccess(SlotsLocal,
                Add(FramePosLocal, Constant(paramIndex - ParamSlotOffset)));

        private Dictionary<Parameter, int>? _inlineParameters;
        private readonly List<Dictionary<string, int>> _inlineParameterScopes = new();
        private readonly List<Dictionary<string, int>> _frameParameterScopes = new();

        /// <summary>Enter an inline-lambda parameter scope. Names resolve
        /// innermost-first, so an inner lambda parameter shadows an outer one or
        /// an outer local of the same name.</summary>
        public void PushInlineParameters() => _inlineParameterScopes.Add(new(StringComparer.Ordinal));

        public void PopInlineParameters() {
            if (_inlineParameterScopes.Count > 0)
                _inlineParameterScopes.RemoveAt(_inlineParameterScopes.Count - 1);
        }

        public void MapInlineParameter(Parameter parameter, int ringSlot) {
            _inlineParameters ??= new(ReferenceEqualityComparer.Instance);
            _inlineParameters[parameter] = ringSlot;
            if (_inlineParameterScopes.Count > 0)
                _inlineParameterScopes[^1][parameter.Name] = ringSlot;
        }

        public bool TryGetInlineParameter(Parameter parameter, out int ringSlot) {
            if (_inlineParameters is { } map && map.TryGetValue(parameter, out ringSlot))
                return true;
            // Fall back to the *live* parameter scopes by name. The flat map is never
            // trimmed when a scope pops, so scanning it by name could bind a popped
            // parameter to a ring slot the emitter has since reused.
            return TryGetInlineParameter(parameter.Name, out ringSlot);
        }

        /// <summary>Resolve a body <see cref="Variable"/> whose name is an inline
        /// lambda parameter to that parameter's argument ring slot. The reader
        /// emits a fresh <see cref="Variable"/> per identifier, so name is the
        /// only link between a lambda parameter and its uses.</summary>
        public bool TryGetInlineParameter(string name, out int ringSlot) {
            for (int i = _inlineParameterScopes.Count - 1; i >= 0; i--) {
                if (_inlineParameterScopes[i].TryGetValue(name, out ringSlot))
                    return true;
            }
            ringSlot = 0;
            return false;
        }

        public void ClearInlineParameters() {
            _inlineParameters = null;
            _inlineParameterScopes.Clear();
        }

        public bool HasInlineParameters => _inlineParameterScopes.Count > 0;

        /// <summary>Enter a lambda-function parameter scope whose values live in
        /// value-stack slots (<see cref="ParameterRead"/>), not ring registers.</summary>
        public void PushFrameParameters() => _frameParameterScopes.Add(new(StringComparer.Ordinal));

        public void PopFrameParameters() {
            if (_frameParameterScopes.Count > 0)
                _frameParameterScopes.RemoveAt(_frameParameterScopes.Count - 1);
        }

        /// <summary>Record a frame parameter's slot in the current scope. Callers pair
        /// this with <see cref="PushFrameParameters"/>; indexing directly fails loud if
        /// that pairing is ever broken (an auto-created scope would leak instead).</summary>
        public void MapFrameParameter(Parameter parameter, int paramIndex) =>
            _frameParameterScopes[^1][parameter.Name] = paramIndex;
        public bool TryGetFrameParameter(string name, out int paramIndex) {
            for (int i = _frameParameterScopes.Count - 1; i >= 0; i--) {
                if (_frameParameterScopes[i].TryGetValue(name, out paramIndex))
                    return true;
            }
            paramIndex = 0;
            return false;
        }

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

        public int DeclareParameter(Parameter p) {
            int slot = _nextParamSlot++;
            _parameters[p] = slot;
            if (ParamSlotOffset == 0 && !HasInlineParameters) {
                while (_rootParameterClrTypes.Count <= slot)
                    _rootParameterClrTypes.Add(null);
                Type? t = p.TypeReference is ClrTypeReference ctr ? ctr.RuntimeType : null;
                t ??= Analysis?.GetResolvedType(p)?.GetRuntimeType();
                _rootParameterClrTypes[slot] = t;
            }
            return slot;
        }

        private readonly List<Type?> _rootParameterClrTypes = [];
        public IReadOnlyList<Type?> RootParameterClrTypes => _rootParameterClrTypes;

        public bool TryGetParameterSlot(Parameter p, out int slot) {
            if (_parameters.TryGetValue(p, out slot))
                return true;
            foreach (var (param, s) in _parameters) {
                if (param.Name == p.Name) {
                    slot = s;
                    return true;
                }
            }
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

        public Expression VariableReadRaw(Variable v) {
            if (BoundParameter(v) is { } parameter) {
                // Read the same storage the write path uses. ParameterValue's
                // slot-0 InstanceHandle snapshot is for ThisReference; a bound
                // parameter must see the live slot after a write.
                if (TryGetInlineParameter(parameter, out int inlineSlot))
                    return RingVar(inlineSlot);
                if (TryGetParameterSlot(parameter, out int paramIndex))
                    return ParameterRead(paramIndex);
                throw new InvalidOperationException(
                    $"Parameter '{parameter.Name}' not declared in any scope");
            }
            if (TryGetRegister(v, out int regIdx))
                return _regVars[regIdx];
            if (TryGetVariable(v, out int slotIndex))
                return VariableRead(slotIndex);
            throw new InvalidOperationException($"Variable '{v.Name}' not declared in any scope");
        }

        public Expression VariableWriteRaw(Variable v, Expression value) {
            if (BoundParameter(v) is { } parameter) {
                if (TryGetInlineParameter(parameter, out int inlineSlot))
                    return Assign(RingVar(inlineSlot), value);
                if (TryGetParameterSlot(parameter, out int paramIndex))
                    return Assign(ParameterRead(paramIndex), value);
                throw new InvalidOperationException(
                    $"Parameter '{parameter.Name}' not declared in any scope");
            }
            if (TryGetRegister(v, out int regIdx))
                return Assign(_regVars[regIdx], value);
            if (TryGetVariable(v, out int slotIndex))
                return VariableWrite(slotIndex, value);
            throw new InvalidOperationException($"Variable '{v.Name}' not declared in any scope");
        }

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