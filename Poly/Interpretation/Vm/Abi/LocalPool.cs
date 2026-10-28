using System.Linq.Expressions;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Abi;

/// <summary>
/// The one pool of CLR locals for a compiled body. Ring temps share one index
/// space: leasing the next slot and rewinding the depth are the same cursor the
/// ring used, so a block reuses <c>_r0</c> across statements and the declared
/// indices stay the prefix <c>0..Max-1</c>. Helper locals (<c>_coal</c>,
/// <c>_shift</c>, <c>_callSp</c>, <c>_callerFp</c>) lease by name and return to
/// a free list when the emitting function ends, so a later statement reuses the
/// same local and a nested use gets the next name. Type handles, bridge spills,
/// and promoted arrays are held for the method.
/// </summary>
internal sealed class LocalPool {
    private readonly List<ParameterExpression> _locals;
    private readonly Dictionary<(int Index, Type Representation), ParameterExpression> _ringVars = new();
    private readonly Dictionary<Type, ParameterExpression> _typeHandles = new();
    private readonly Dictionary<(bool Register, int Index, Type ElementType), ParameterExpression> _arrayLocals = new();
    private readonly Dictionary<(bool Register, int Index), ParameterExpression> _promotedArrays = new();
    private readonly Dictionary<(Type Type, string Stem), Stack<ParameterExpression>> _freeHelpers = new();
    private readonly Dictionary<(Type Type, string Stem), int> _helperCount = new();
    private readonly HashSet<ParameterExpression> _liveHelpers = new();
    private int _maxDepth;
    private int _bridgeLocalCounter;

    internal LocalPool(List<ParameterExpression> locals) => _locals = locals;

    internal int Depth { get; set; }

    internal int MaxDepth => _maxDepth;

    internal IEnumerable<KeyValuePair<Type, ParameterExpression>> TypeHandles => _typeHandles;

    /// <summary>Helper locals leased and not yet returned. A suspend spill reads this set.</summary>
    internal IReadOnlyCollection<ParameterExpression> LiveHelpers => _liveHelpers;

    internal int Alloc() {
        int slot = Depth;
        Depth = slot + 1;
        if (slot + 1 > _maxDepth)
            _maxDepth = slot + 1;
        return slot;
    }

    /// <summary>The ring local for <paramref name="absoluteIndex"/> at
    /// <paramref name="representation"/>, declared on first use. Depth rewind
    /// reuses an index at a different representation, so the table is one local
    /// per (index, representation).</summary>
    internal ParameterExpression Ring(int absoluteIndex, Type representation) {
        if (!_ringVars.TryGetValue((absoluteIndex, representation), out var local)) {
            local = Variable(representation, RingLocalName(absoluteIndex, representation));
            _ringVars[(absoluteIndex, representation)] = local;
            _locals.Add(local);
        }
        if (absoluteIndex + 1 > _maxDepth)
            _maxDepth = absoluteIndex + 1;
        return local;
    }

    /// <summary>Every ring local whose index is still inside <paramref name="depth"/>.
    /// A rewind leaves the local declared; the depth is what makes it live. Several
    /// representations of one index are all returned — the caller spills each once.</summary>
    internal void CollectBelow(int depth, List<ParameterExpression> into) {
        foreach (var (key, local) in _ringVars)
            if (key.Index < depth)
                into.Add(local);
    }

    /// <summary>Allocate the next ring slot and return its local. The slot stays
    /// live: the caller reads it, and a later rewind releases it.</summary>
    internal ParameterExpression HoldRing(Type representation) {
        int slot = Alloc();
        Depth = slot + 1;
        return Ring(slot, representation);
    }

    internal ParameterExpression TypeHandle(Type runtimeType) {
        if (!_typeHandles.TryGetValue(runtimeType, out var local)) {
            local = Variable(typeof(long), $"_type{_typeHandles.Count}");
            _typeHandles[runtimeType] = local;
            _locals.Add(local);
        }
        return local;
    }

    internal ParameterExpression Bridge(Type type, string name) {
        var variable = Variable(type, $"_lambda_{name}_{_bridgeLocalCounter++}");
        _locals.Add(variable);
        return variable;
    }

    internal ParameterExpression ArrayLocal((bool Register, int Index) slot, Type elementType, string name) {
        var key = (slot.Register, slot.Index, elementType);
        if (!_arrayLocals.TryGetValue(key, out var local)) {
            local = Variable(elementType, name);
            _arrayLocals[key] = local;
            _locals.Add(local);
        }
        _promotedArrays[(slot.Register, slot.Index)] = local;
        return local;
    }

    internal bool TryPromoted((bool Register, int Index) slot, out ParameterExpression local) =>
        _promotedArrays.TryGetValue(slot, out local!);

    internal void ForgetPromoted(bool register, int index) =>
        _promotedArrays.Remove((register, index));

    /// <summary>Lease a helper local. The first simultaneous lease keeps
    /// <paramref name="stem"/>; a nested lease takes the next name. Disposing
    /// returns the local to the free list so the next statement reuses it.</summary>
    internal HelperLease LeaseHelper(Type type, string stem) {
        var key = (type, stem);
        if (_freeHelpers.TryGetValue(key, out var free) && free.TryPop(out var reused)) {
            _liveHelpers.Add(reused);
            return new HelperLease(this, key, reused);
        }
        int n = _helperCount.GetValueOrDefault(key);
        _helperCount[key] = n + 1;
        var created = Variable(type, n == 0 ? stem : $"{stem}{n + 1}");
        _locals.Add(created);
        _liveHelpers.Add(created);
        return new HelperLease(this, key, created);
    }

    internal void ReleaseHelper((Type Type, string Stem) key, ParameterExpression local) {
        if (!_liveHelpers.Remove(local))
            return;
        if (!_freeHelpers.TryGetValue(key, out var free)) {
            free = new Stack<ParameterExpression>();
            _freeHelpers[key] = free;
        }
        free.Push(local);
    }

    private static string RingLocalName(int index, Type representation) =>
        representation == typeof(double) ? $"_d{index}"
            : representation == typeof(float) ? $"_f{index}"
            : representation == typeof(int) ? $"_i{index}"
            : Nullable.GetUnderlyingType(representation) is { } underlying
                ? $"_n{index}_{underlying.Name}"
            : $"_r{index}";

    /// <summary>An emit-time lease. Disposing it does not delete the local —
    /// the parameter stays on the method — it only lets the next statement
    /// reuse it.</summary>
    internal readonly struct HelperLease : IDisposable {
        private readonly LocalPool _pool;
        private readonly (Type Type, string Stem) _key;

        internal HelperLease(LocalPool pool, (Type Type, string Stem) key, ParameterExpression local) {
            _pool = pool;
            _key = key;
            Local = local;
        }

        internal ParameterExpression Local { get; }

        public void Dispose() => _pool.ReleaseHelper(_key, Local);
    }
}

/// <summary>A value the emitter has placed. <see cref="Code"/> runs to produce it;
/// <see cref="Read"/> names the value after that code has run (the same expression
/// when the node landed nowhere in particular); <see cref="Place"/> is where the
/// emitter put it, when it has a place; and <see cref="Type"/> is the
/// <see cref="LocalType"/> of that place. Compile returns this instead of a bare
/// ring index, so a consumer reads <see cref="Read"/> or re-reads
/// <see cref="SlotLocation.Read"/> rather than naming a slot.</summary>
internal readonly record struct Value(Expression Code, Expression Read, SlotLocation? Place, LocalType? Type) {
    /// <summary>The CLR type of the value's own shape.</summary>
    public Type ClrType => Type?.Clr ?? Code.Type;

    /// <summary>The ring index of a placed value. Cursor arithmetic is the one
    /// place an index is still named; it is derived from the place, not threaded
    /// through the emitter as a separate operand.</summary>
    public int RingSlot => Place is SlotLocation.Ring ring
        ? ring.Slot
        : throw new InvalidOperationException("the value has no ring slot.");

    /// <summary>The read as the code a consumer that has not run the node yet needs.</summary>
    public static implicit operator Expression(Value value) => value.Code;
}

/// <summary>The default emit shape. <see cref="Yield"/> spills a computation
/// into the next ring slot and keeps that slot. Disposing without a yield
/// rewinds to the depth the scope opened at, so a sub-expression's temps
/// cannot leak.</summary>
internal sealed class EmitScope : IDisposable {
    private readonly AbiEmitter.Context _ctx;
    private readonly int _entry;
    private int? _yielded;

    internal EmitScope(AbiEmitter.Context ctx) {
        _ctx = ctx;
        _entry = ctx.RingDepth;
    }

    internal Expression Yield(Expression value) {
        var slotType = RingCodec.SlotTypeOf(value.Type);
        var local = _ctx.HoldRing(slotType);
        _yielded = _ctx.RingDepth - 1;
        // A ulong or enum result is a word, not an assignment to a long local's CLR
        // type. An IEEE slot already matches and keeps the value itself.
        var stored = local.Type == value.Type
            ? value
            : RingCodec.ToFrame(value, value.Type, _ctx.HeapLocal);
        return Place(local, stored);
    }

    public void Dispose() => _ctx.RingDepth = _yielded is int slot ? slot + 1 : _entry;

    /// <summary>A resume label cannot sit in an assignment's value. When the value is a
    /// block that contains one, the earlier statements stay statements and only the
    /// tail — which has no resume label — is assigned into the slot. Every other
    /// yield is the assignment it always was.</summary>
    private Expression Place(ParameterExpression local, Expression value) {
        if (value is BlockExpression block && ContainsResume(block)) {
            var parts = new List<Expression>(block.Expressions.Count);
            for (int i = 0; i < block.Expressions.Count - 1; i++)
                parts.Add(block.Expressions[i]);
            parts.Add(Place(local, block.Expressions[^1]));
            return block.Variables.Count == 0 ? Block(parts) : Block(block.Variables, parts);
        }
        return Assign(local, value);
    }

    private bool ContainsResume(Expression value) {
        var finder = new ResumeFinder(_ctx);
        finder.Visit(value);
        return finder.Found;
    }

    private sealed class ResumeFinder(AbiEmitter.Context ctx) : ExpressionVisitor {
        internal bool Found;

        public override Expression? Visit(Expression? node) {
            if (Found || node is null)
                return node;
            return base.Visit(node);
        }

        protected override Expression VisitLabel(LabelExpression node) {
            if (ctx.IsResumeTarget(node.Target))
                Found = true;
            return base.VisitLabel(node);
        }
    }
}