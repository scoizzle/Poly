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
    private static readonly PropertyInfo ValueStackRawSlotsPropertyInfo = Ref<ValueStack>.Property(s => (object)s.RawSlots);

    private readonly ParameterExpression _stateParam;
    private readonly List<ParameterExpression> _locals = new();
    private readonly Dictionary<int, LabelTarget> _labelTargets = new();

    /// <summary>ParameterExpression for each µop's produced value. Indexed by µop PC.</summary>
    private readonly List<ParameterExpression> _valueSlots = new();

    public ParameterExpression State => _stateParam;
    public Expression ProgramCounter { get; }
    public Expression ValueStack { get; }
    public Expression RawSlots { get; }
    public Expression Registers { get; }
    public Expression InstructionCounters { get; }
    public IReadOnlyList<ParameterExpression> Locals => _locals;

    public int CurrentLabelIndex { get; set; }
    public int NextLabelIndex => CurrentLabelIndex + 1;

    public LabelTarget EntryLabel { get; } = Label("entry");
    public LabelTarget ExitLabel { get; } = Label("exit");

    public CompilationContext() {
        _stateParam = Parameter(typeof(VmState), "state");
        ProgramCounter = Property(State, StateProgramCounterPropertyInfo);
        ValueStack = Property(State, StateStackPropertyInfo);
        RawSlots = Property(ValueStack, ValueStackRawSlotsPropertyInfo);
        Registers = Property(State, StateRegistersPropertyInfo);
        InstructionCounters = Property(State, StateInstructionCountersPropertyInfo);
    }

    /// <summary>Get or create the ParameterExpression for _v{pc}.
    /// All value slots are pre-created before the µop walk.</summary>
    public ParameterExpression ValueSlot(int pc) {
        while (_valueSlots.Count <= pc) {
            var slot = Variable(typeof(long), $"_v{_valueSlots.Count}");
            _valueSlots.Add(slot);
            _locals.Add(slot);
        }
        return _valueSlots[pc];
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