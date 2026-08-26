using System.Linq.Expressions;
using System.Reflection;

using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm;

public static partial class DirectVmAbiEmitter {

    private static bool IsDoubleValue(AbiCtx ctx, Node node) {
        if (ctx.Analysis is null) return false;
        var meta = ctx.Analysis.GetMetadata<ValueRepresentationMetadata>(node);
        if (meta?.ClrType is null) return false;
        return meta.ClrType == typeof(double) || meta.ClrType == typeof(float);
    }

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
            SavedSp = Variable(typeof(int), "_savedSp");
            _locals.Add(SavedSp);
            ResultLocal = Variable(typeof(long), "_result");
            _locals.Add(ResultLocal);
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
        public ParameterExpression SavedSp { get; }
        public ParameterExpression ResultLocal { get; }
        public LabelTarget EntryLabel { get; }
        public LabelTarget ExitLabel { get; }
        public int LabelCounter { get; set; }
        public Expression? DebugHookProp { get; set; }
        public Expression CurrentAstNodeExpr =>
            Property(_stateParam, nameof(VmState.CurrentAstNode));
        public int CurrentLocalCount {
            get {
                int count = 0;
                foreach (var scope in _scopeStack)
                    count += scope.Count;
                return count;
            }
        }
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
        public IReadOnlyList<ParameterExpression> Locals => _locals;

        internal const int SmallArrayThreshold = 16;
        internal const int SmallArraySlotBase = 128;
        private int _nextSmallArraySlot = SmallArraySlotBase;

        public int AllocateSmallArray() {
            int baseOffset = _nextSmallArraySlot;
            _nextSmallArraySlot += SmallArrayThreshold;
            return baseOffset;
        }

        public Expression SlotsInitExpression =>
            Property(Property(_stateParam, StateStackProperty), ValueStackRawSlotsProperty);
        public Expression HeapInitExpression =>
            Property(_stateParam, "Heap");
        public Expression Registers =>
            Property(_stateParam, StateRegistersProperty);
        public Expression HeapRawSlots =>
            Property(HeapLocal, StateHeapRawSlotsProperty);
        public Expression ClosureHandle =>
            Property(_stateParam, StateClosureHandleProperty);
        public Expression SlotsStackPointer =>
            Property(Property(_stateParam, StateStackProperty), ValueStackStackPointerProperty);

        public int RingDepth { get; set; }
        private int _maxDepth;
        public int MaxRingDepth => _maxDepth;

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

        private Dictionary<Variable, int>? _frameLocalVars;

        public void TrackFrameLocalArray(Variable v, int baseOffset) {
            _frameLocalVars ??= new(ReferenceEqualityComparer.Instance);
            _frameLocalVars[v] = baseOffset;
        }

        public void UntrackFrameLocalArray(Variable v) {
            _frameLocalVars?.Remove(v);
        }

        public int? TryGetFrameLocalBase(Variable v) =>
            _frameLocalVars is { } dict && dict.TryGetValue(v, out int baseOffset) ? baseOffset : null;

        private readonly Stack<Dictionary<Variable, int>> _scopeStack = new();
        private readonly Dictionary<Variable, int> _variableRegisters = new(ReferenceEqualityComparer.Instance);
        private readonly Stack<List<Variable>> _scopeVars = new();
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

        public IReadOnlyList<Expression> EmitScopeLoads() {
            if (_scopeVars.Count == 0 || _scopeStack.Count == 0) return Array.Empty<Expression>();
            var vars = _scopeVars.Peek();
            var scope = _scopeStack.Peek();
            var loads = new List<Expression>(vars.Count);
            foreach (var v in vars) {
                if (_variableRegisters.TryGetValue(v, out int regIdx) && scope.TryGetValue(v, out int slot)) {
                    loads.Add(Assign(
                        _regVars[regIdx],
                        ArrayAccess(SlotsLocal, Add(FramePosLocal, Constant(slot)))));
                }
            }
            return loads;
        }

        public void DeclareVariable(Variable v) {
            if (_scopeStack.Count == 0)
                throw new InvalidOperationException("No active scope");
            int slot = _scopeStack.Peek().Count;
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
            foreach (var scope in _scopeStack) {
                if (scope.TryGetValue(v, out slot))
                    return true;
            }
            slot = -1;
            return false;
        }

        public int ParamSlotOffset { get; set; }

        public Expression VariableRead(int varIndex) =>
            ArrayAccess(SlotsLocal, Add(FramePosLocal, Constant(varIndex)));

        public Expression VariableRead(Variable v) {
            var raw = VariableReadRaw(v);
            if (!IsCellBacked(v))
                return raw;
            return ArrayAccess(
                Convert(ArrayAccess(HeapRawSlots, Convert(raw, typeof(int))), typeof(long[])),
                Constant(0));
        }

        public Expression VariableWrite(int varIndex, Expression value) =>
            Assign(VariableRead(varIndex), value);

        public Expression VariableWrite(Variable v, Expression value) {
            if (IsCellBacked(v))
                return Assign(VariableRead(v), value);
            return VariableWriteRaw(v, value);
        }

        public Expression ParameterRead(int paramIndex) =>
            ArrayAccess(SlotsLocal,
                Add(FramePosLocal, Constant(paramIndex - ParamSlotOffset)));

        private Dictionary<Parameter, int>? _inlineParameters;

        public void MapInlineParameter(Parameter parameter, int ringSlot) {
            _inlineParameters ??= new(ReferenceEqualityComparer.Instance);
            _inlineParameters[parameter] = ringSlot;
        }

        public bool TryGetInlineParameter(Parameter parameter, out int ringSlot) {
            if (_inlineParameters is { } map) {
                if (map.TryGetValue(parameter, out ringSlot))
                    return true;
                foreach (var (p, slot) in map) {
                    if (p.Name == parameter.Name) {
                        ringSlot = slot;
                        return true;
                    }
                }
            }
            ringSlot = 0;
            return false;
        }

        public void ClearInlineParameters() => _inlineParameters = null;

        public bool HasInlineParameters => _inlineParameters is not null;

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

        public Expression VariableReadRaw(Variable v) {
            if (_variableRegisters.TryGetValue(v, out int regIdx))
                return _regVars[regIdx];
            if (TryGetVariable(v, out int slotIndex))
                return VariableRead(slotIndex);
            throw new InvalidOperationException($"Variable '{v.Name}' not declared in any scope");
        }

        public Expression VariableWriteRaw(Variable v, Expression value) {
            if (_variableRegisters.TryGetValue(v, out int regIdx))
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

        private readonly Stack<CompileTimeFrame> _ctFrames = new();
        private int _ctSp;

        private sealed class CompileTimeFrame {
            public int ArgumentCount { get; }
            public int LocalCount { get; }
            public int BaseOffset { get; }
            public int HeaderSize { get; }
            public CompileTimeFrame(int args, int locals, int baseOffset, int headerSize) {
                ArgumentCount = args;
                LocalCount = locals;
                BaseOffset = baseOffset;
                HeaderSize = headerSize;
            }
        }

        public void EnterActivation(int argumentCount, int localCount, int headerSize = 0) {
            var frame = new CompileTimeFrame(argumentCount, localCount, _ctSp + headerSize, headerSize);
            _ctFrames.Push(frame);
            _ctSp += headerSize + argumentCount + localCount;
        }

        public void LeaveActivation() {
            if (_ctFrames.Count == 0) throw new InvalidOperationException("No active frame");
            var f = _ctFrames.Pop();
            _ctSp -= f.HeaderSize + f.ArgumentCount + f.LocalCount;
        }

        public int GetCompileTimeVariableOffset(Variable v) {
            if (!TryGetVariable(v, out int slotInScope))
                throw new InvalidOperationException($"Variable '{v.Name}' has no slot");
            return slotInScope;
        }

        public int GetCurrentFrameSize() =>
            _ctFrames.Count == 0 ? 0 : _ctFrames.Peek().ArgumentCount + _ctFrames.Peek().LocalCount + 2;
    }
}