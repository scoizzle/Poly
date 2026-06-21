using System.Linq.Expressions;
using System.Reflection;

using Poly.Interpretation.Vm.Instructions;
using Poly.Syntax;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm;

/// <summary>
/// Compilation context: labels, expression references, and per-µop local storage.
/// No eval stack, no TempVar tracking — producer relationships are carried by
/// <see cref="Instruction.ConsumedFromPcs"/> set during lowering.
/// </summary>
public sealed class CompilationContext {
    private static readonly PropertyInfo StateStackPropertyInfo = Ref<VmState>.Property(e => e.Stack);
    private static readonly PropertyInfo StateRegistersPropertyInfo = Ref<VmState>.Property(e => e.Registers);
    private static readonly PropertyInfo StateProgramCounterPropertyInfo = Ref<VmState>.Property(e => e.ProgramCounter);
    private static readonly PropertyInfo StateInstructionCountersPropertyInfo = Ref<VmState>.Property(e => e.InstructionCounters);
    private static readonly PropertyInfo StateHeapPropertyInfo = Ref<VmState>.Property(e => e.Heap);
    private static readonly PropertyInfo StateHeapRawSlotsPropertyInfo = Ref<VmState>.Property(e => e.Heap.RawSlots);
    private static readonly PropertyInfo ValueStackRawSlotsPropertyInfo = Ref<ValueStack>.Property(s => s.RawSlots);

    private readonly ParameterExpression _stateParam;
    private readonly List<ParameterExpression> _locals = new();
    private readonly Dictionary<int, LabelTarget> _labelTargets = new();

    // ── Ring-based µop value allocation ─────────────────────────────
    // Instead of one _v{pc} per producer µop, each value is stored at
    // _r{k} where k = its ring (eval-stack) depth at push time.
    // This keeps locals = max ring depth (~10-20) regardless of µop count.
    private readonly Dictionary<int, int> _pcToRingIdx = new();
    private readonly List<ParameterExpression> _ringRegisters = new();

    public ParameterExpression State => _stateParam;
    /// <summary>Local <c>_pc</c> — fast local for the current µop index.
    /// Only flushed to <c>state.ProgramCounter</c> at suspension points.</summary>
    public ParameterExpression ProgramCounter { get; }
    /// <summary>Direct access to <c>state.ProgramCounter</c> for suspension flushing.</summary>
    public Expression StateProgramCounter { get; }
    /// <summary>Local <c>_slots</c> — cached <c>state.Stack.RawSlots</c> array.</summary>
    public ParameterExpression SlotsLocal { get; }
    /// <summary>Expression for the preamble: <c>state.Stack.RawSlots</c> to init <see cref="SlotsLocal"/>.</summary>
    public Expression SlotsInitExpression { get; }
    /// <summary>Expression for the preamble: <c>state.Heap</c> to init <see cref="HeapLocal"/>.</summary>
    public Expression HeapInitExpression { get; }
    /// <summary>Local <c>_heap</c> — cached <c>state.Heap</c> reference.</summary>
    public ParameterExpression HeapLocal { get; }
    public Expression ValueStack { get; }
    public Expression Heap { get; }
    public Expression HeapRawSlots { get; }
    public Expression RawSlots { get; }
    public Expression Registers { get; }
    public Expression InstructionCounters { get; }
    public IReadOnlyList<ParameterExpression> Locals => _locals;

    public int CurrentLabelIndex { get; set; }
    public int NextLabelIndex => CurrentLabelIndex + 1;

    /// <summary>When true, Jump µops insert a loop-iteration counter and
    /// throw <c>InvalidOperationException</c> if <c>state.MaxLoopIterations</c>
    /// is exceeded.  Zero overhead when false (no expression generated).</summary>
    public bool LimitLoops { get; set; }

    /// <summary>Local boolean: true when <c>state.MaxLoopIterations != -1</c>.
    /// Computed once in the preamble so Jump µops don't re-read the property.</summary>
    public ParameterExpression LoopLimitActive { get; }
    /// <summary>Local copy of <c>state.MaxLoopIterations</c> for fast access in Jump µops.</summary>
    public ParameterExpression LoopMaxIter { get; }

    public LabelTarget EntryLabel { get; } = Label("entry");
    public LabelTarget ExitLabel { get; } = Label("exit");

    public CompilationContext() {
        _stateParam = Parameter(typeof(VmState), "state");
        ProgramCounter = Variable(typeof(int), "_pc");
        StateProgramCounter = Property(State, StateProgramCounterPropertyInfo);
        _locals.Add(ProgramCounter);
        SlotsLocal = Variable(typeof(long[]), "_slots");
        _locals.Add(SlotsLocal);
        HeapLocal = Variable(typeof(Heap), "_heap");
        _locals.Add(HeapLocal);
        HeapInitExpression = Property(State, StateHeapPropertyInfo);
        ValueStack = Property(State, StateStackPropertyInfo);
        SlotsInitExpression = Property(ValueStack, ValueStackRawSlotsPropertyInfo);
        RawSlots = SlotsLocal;
        Registers = Property(State, StateRegistersPropertyInfo);
        InstructionCounters = Property(State, StateInstructionCountersPropertyInfo);
        Heap = HeapLocal;
        HeapRawSlots = Property(HeapLocal, StateHeapRawSlotsPropertyInfo);
        LoopLimitActive = Variable(typeof(bool), "_loopLimitActive");
        LoopMaxIter = Variable(typeof(long), "_loopMaxIter");
        _locals.Add(LoopLimitActive);
        _locals.Add(LoopMaxIter);
    }

    private int _registerLimit;
    private int _maxFrameDepth;
    private static readonly PropertyInfo StateFrameBasePropertyInfo = Ref<VmState>.Property(e => e.FrameBase);

    /// <summary>Configure ring-based µop value allocation.
    /// <paramref name="ringMap"/> maps each producer PC → its ring depth index.
    /// Creates <c>_r{0..limit-1}</c> locals; deeper indices spill to
    /// <c>_slots[FB + maxFrameDepth + spillIdx]</c> on the value stack.</summary>
    public void ConfigureRingAllocation(Dictionary<int, int> ringMap, int maxActiveLocalDepth, int maxFrameDepth) {
        _registerLimit = maxActiveLocalDepth;
        _maxFrameDepth = maxFrameDepth;
        _pcToRingIdx.Clear();
        _ringRegisters.Clear();
        foreach (var kv in ringMap) {
            _pcToRingIdx[kv.Key] = kv.Value;
        }

        int regCount = Math.Min(maxActiveLocalDepth, ringMap.Count > 0 ? ringMap.Values.Max() + 1 : 0);
        for (int i = 0; i < regCount; i++) {
            var reg = Variable(typeof(long), $"_r{i}");
            _ringRegisters.Add(reg);
            _locals.Add(reg);
        }
    }

    /// <summary>Return the expression for the value produced by µop at <paramref name="pc"/>.
    /// Reads from <c>_r{k}</c> local (k &lt; limit) or
    /// <c>_slots[FB + maxFrameDepth + k]</c> (spilled to value stack).</summary>
    public Expression ValueSlot(int pc) {
        if (!_pcToRingIdx.TryGetValue(pc, out int ringIdx))
            throw new InvalidOperationException($"PC {pc} has no ring allocation");
        if (ringIdx < _registerLimit)
            return _ringRegisters[ringIdx];
        int spillOffset = _maxFrameDepth + ringIdx;
        var fb = Property(State, StateFrameBasePropertyInfo);
        return ArrayAccess(RawSlots, Add(fb, Constant(spillOffset)));
    }

    /// <summary>Resolve a consumed value, applying φ when the value's source
    /// differs across predecessor paths.  The secondary path is identified by
    /// <see cref="Instruction.PhiSourcePcs"/> — when <c>state.ProgramCounter</c>
    /// matches, the alternate producer is used instead of the primary.</summary>
    public Expression ResolveValue(Instruction op, int index) {
        var primary = ValueSlot(op.ConsumedFromPcs![index]);
        if (op.PhiSourcePcs is { } srcs && op.PhiAltPcs is { } alts
            && index < srcs.Length && srcs[index] >= 0) {
            var alt = ValueSlot(alts[index]);
            return Condition(Equal(ProgramCounter, Constant(srcs[index])), alt, primary);
        }
        return primary;
    }

    // ── Label management ──

    public LabelTarget GetLabel(int pc) {
        if (!_labelTargets.TryGetValue(pc, out var target)) {
            target = Label($"pc_{pc}");
            _labelTargets[pc] = target;
        }
        return target;
    }

    public void MarkLabel(int pc) {
        if (!_labelTargets.ContainsKey(pc))
            _labelTargets[pc] = Label($"pc_{pc}");
    }
}