using Poly.Syntax.Nodes;

namespace Poly.Syntax.Primitives;

/// <summary>
/// Stack-based expression reconstructor.
///
/// Processes primitives left-to-right, maintaining a virtual stack of partially-constructed
/// <see cref="Node"/> trees. When a primitive is encountered that pops operands (e.g.
/// <see cref="BinaryOp"/>), the operands are taken from the virtual stack and the result
/// is pushed back.
///
/// Control-flow primitives (<see cref="Goto"/>, <see cref="CondGoto"/>, <see cref="Label"/>,
/// <see cref="Return"/>, <see cref="Throw"/>) are NOT processed — they signal a structural
/// boundary and cause the processor to stop.
/// </summary>
internal sealed class ExpressionReconstructor {
    private readonly List<Node> _stack = new();
    private readonly SlotAnalyzer _slotAnalyzer;
    private readonly AnalysisContext? _context;

    /// <summary>Gets the current virtual stack.</summary>
    public IReadOnlyList<Node> Stack => _stack;

    /// <summary>True when the virtual stack contains exactly one value (a complete expression).</summary>
    public bool HasResult => _stack.Count == 1;

    /// <summary>The reconstructed expression, or null if the stack doesn't have exactly one value.</summary>
    public Node? Result => _stack.Count == 1 ? _stack[0] : null;

    public ExpressionReconstructor(SlotAnalyzer slotAnalyzer, AnalysisContext? context = null) {
        _slotAnalyzer = slotAnalyzer;
        _context = context;
    }

    /// <summary>
    /// Process primitives from <paramref name="startIndex"/> until a control-flow boundary
    /// or the end of the sequence.
    /// </summary>
    /// <returns>The number of primitives consumed.</returns>
    public int Process(IReadOnlyList<PrimitiveNode> primitives, int startIndex) {
        _stack.Clear();
        int i = startIndex;

        for (; i < primitives.Count; i++) {
            if (!TryConsume(primitives[i]))
                break;
        }

        return i - startIndex;
    }

    /// <summary>
    /// Process primitives and verify that the stack ends with exactly one result.
    /// </summary>
    public bool TryParse(IReadOnlyList<PrimitiveNode> primitives, int startIndex, out Node? result) {
        var consumed = Process(primitives, startIndex);
        if (consumed > 0 && HasResult && consumed == primitives.Count - startIndex) {
            result = Result;
            return true;
        }
        result = null;
        return false;
    }

    /// <summary>
    /// Attempt to consume a single primitive. Returns false for control-flow primitives
    /// or when there are insufficient operands on the virtual stack.
    /// </summary>
    private bool TryConsume(PrimitiveNode prim) {
        switch (prim) {
            case PushConstant pc:
                _stack.Add(new Constant(pc.Value));
                return true;

            case LoadLocal ll:
                _stack.Add(_slotAnalyzer.CreateSlotReference(ll.SlotIndex));
                return true;

            case Parameter p:
                _stack.Add(new Variable($"p{p.SlotIndex}"));
                return true;

            case BinaryOp bop:
                if (_stack.Count < 2) return false;
                var right = Pop();
                var left = Pop();
                _stack.Add(CreateBinary(left, right, bop.Op, bop.ComparisonType));
                return true;

            case UnaryOp uop:
                if (_stack.Count < 1) return false;
                var operand = Pop();
                _stack.Add(CreateUnary(operand, uop.Op));
                return true;

            case Dup:
                if (_stack.Count < 1) return false;
                _stack.Add(_stack[^1]); // Share reference to top of stack
                return true;

            case Discard:
                if (_stack.Count < 1) return false;
                Pop();
                return true;

            case StoreLocal sl:
                if (_stack.Count < 1) return false;
                var value = Pop();
                // StoreLocal is (1,0) — value is consumed, nothing pushed
                // Record as an assignment for reference
                return true;

            case CountBits:
                if (_stack.Count < 1) return false;
                var cbOperand = Pop();
                _stack.Add(new PopCount(cbOperand));
                return true;

            case ArrayLoad:
                if (_stack.Count < 2) return false;
                var index = Pop();
                var arr = Pop();
                _stack.Add(new IndexAccess(arr, index));
                return true;

            case NewArray:
                if (_stack.Count < 1) return false;
                var length = Pop();
                _stack.Add(new Poly.Syntax.Nodes.NewArray(new TypeReference("?"), length));
                return true;

            case ArrayStore:
                if (_stack.Count < 3) return false;
                var storeIndex = Pop();
                var storeArr = Pop();
                var storeValue = Pop();
                // ArrayStore is (3,0) — nothing pushed back
                return true;

            case StridedSet:
                if (_stack.Count < 4) return false;
                Pop(); // limit
                Pop(); // step
                Pop(); // start
                Pop(); // handle
                // StridedSet is (4,0) — nothing pushed back
                return true;

            case CallExternal ce:
                return ConsumeCallExternal(ce);

            case Call c:
                return ConsumeCall(c);

            case StoreUpvalue su:
                if (_stack.Count < 1) return false;
                Pop();
                return true;

            case LoadUpvalue lu:
                _stack.Add(new Variable($"upvalue{lu.UpvalueIndex}"));
                return true;

            case AllocClosure ac:
                // Pop upvalueCount captured variables from stack
                for (int i = 0; i < ac.UpvalueCount; i++) {
                    if (_stack.Count < 1) return false;
                    Pop();
                }
                // Lambda body primitives are in a separate function index
                _stack.Add(new Lambda(
                    Array.Empty<Poly.Syntax.Nodes.Parameter>(),
                    new Block(new Constant(0L))));
                return true;

            case LoadHeapConstant lhc:
                _stack.Add(new Variable($"$heapconst{lhc.Handle}"));
                return true;

            // ── Control-flow primitives — signal structural boundary ──
            case Goto:
            case CondGoto:
            case Label:
            case Return:
            case Throw:
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// Peek the top of virtual stack without removal.
    /// </summary>
    public Node Peek() {
        if (_stack.Count == 0)
            throw new InvalidOperationException("Virtual stack is empty");
        return _stack[^1];
    }

    /// <summary>
    /// Pop the top value from the virtual stack.
    /// </summary>
    private Node Pop() {
        if (_stack.Count == 0)
            throw new InvalidOperationException("Virtual stack is empty");
        var result = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        return result;
    }

    /// <summary>
    /// Create a binary expression node from an OpKind.
    /// </summary>
    internal static Node CreateBinary(Node left, Node right, OpKind op, System.Type? comparisonType = null) {
        // Note: comparisonType is used for CLR reference-type equality comparisons
        // At reconstruction time we preserve the semantic meaning but may lose
        // the exact type — that's acceptable for reconstruction.
        return op switch {
            OpKind.Add => new Add(left, right),
            OpKind.Sub => new Subtract(left, right),
            OpKind.Mul => new Multiply(left, right),
            OpKind.Div => new Divide(left, right),
            OpKind.Mod => new Modulo(left, right),
            OpKind.Eq => new Equal(left, right),
            OpKind.Neq => new NotEqual(left, right),
            OpKind.Gt => new GreaterThan(left, right),
            OpKind.Gte => new GreaterThanOrEqual(left, right),
            OpKind.Lt => new LessThan(left, right),
            OpKind.Lte => new LessThanOrEqual(left, right),
            OpKind.And => new And(left, right),
            OpKind.Or => new Or(left, right),
            OpKind.Xor => new BitwiseXor(left, right),
            OpKind.Shl => new ShiftLeft(left, right),
            OpKind.Shr => new ShiftRight(left, right),
            _ => throw new NotSupportedException($"Unknown OpKind: {op}")
        };
    }

    /// <summary>
    /// Create a unary expression node from a UnaryOpKind.
    /// </summary>
    internal static Node CreateUnary(Node operand, UnaryOpKind op) => op switch {
        UnaryOpKind.Neg => new UnaryMinus(operand),
        UnaryOpKind.Not => new Not(operand),
        UnaryOpKind.BitNot => new BitwiseNot(operand),
        _ => throw new NotSupportedException($"Unknown UnaryOpKind: {op}")
    };

    /// <summary>
    /// Consume a CallExternal primitive: pop args and instance from virtual stack,
    /// create an Invoke node.
    /// </summary>
    private bool ConsumeCallExternal(CallExternal ce) {
        var args = new List<Node>();

        // Pop arguments in reverse order (last on stack = last argument)
        for (int i = 0; i < ce.ArgCount; i++) {
            if (_stack.Count < 1) return false;
            args.Insert(0, Pop());
        }

        if (ce.IsStatic) {
            // Static method — no instance
            var member = new Member(new Constant(0L), ce.Target.Name);
            _stack.Add(new Invoke(member, args.ToArray()));
        }
        else {
            // Instance method — pop the instance
            if (_stack.Count < 1) return false;
            var instance = Pop();
            var member = new Member(instance, ce.Target.Name);
            _stack.Add(new Invoke(member, args.ToArray()));
        }
        return true;
    }

    /// <summary>
    /// Consume a Call primitive: pop args and callee from virtual stack.
    /// </summary>
    private bool ConsumeCall(Call c) {
        var args = new List<Node>();

        // Pop arguments (last on stack = last argument)
        for (int i = 0; i < c.ArgCount; i++) {
            if (_stack.Count < 1) return false;
            args.Insert(0, Pop());
        }

        // Pop the callee target
        if (_stack.Count < 1) return false;
        var target = Pop();

        _stack.Add(new Invoke(target, args.ToArray()));
        return true;
    }
}