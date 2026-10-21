using System.Linq.Expressions;
using System.Reflection;

using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm;

public static partial class DirectVmAbiEmitter {
    /// <summary>Comparison as a bool Expression (no 0/1 long).</summary>
    private static Expression CompileCompareTest(
        Node left, Node right,
        Func<Expression, Expression, BinaryExpression> cf,
        AbiCtx ctx) {
        var lv = CompileValue(left, ctx);
        var rv = CompileValue(right, ctx);
        bool eq = cf == Equal || cf == NotEqual;
        if (eq && AreHeapValues(ctx, left, right)) {
            var lo = HeapValueToObject(lv, ctx);
            var ro = HeapValueToObject(rv, ctx);
            var ec = Call(ObjectEquals, lo, ro);
            return cf == Equal ? ec : Not(ec);
        }
        if (!eq && ctx.Analysis is not null
            && ctx.KindOf(left) == ValueRepresentationKind.HeapRef
            && ctx.KindOf(right) == ValueRepresentationKind.HeapRef) {
            // Relational comparison of heap-resident values (DateOnly/DateTime/
            // Guid/string/...): compare the boxed values, not the raw handles.
            // Analysis-known HeapRef on BOTH operands only — unresolved bag reads
            // (Unknown) keep the scalar path.
            var lo = HeapValueToObject(lv, ctx);
            var ro = HeapValueToObject(rv, ctx);
            var cmp = Call(VmHeapCompare, lo, ro);
            return cf(cmp, Constant(0));
        }
        if (RingCodec.IsIeee(left, ctx.Analysis) || RingCodec.IsIeee(right, ctx.Analysis))
            return cf(Call(BitConverterInt64BitsToDouble, lv), Call(BitConverterInt64BitsToDouble, rv));
        return cf(lv, rv);
    }

    private static Expression EmitComparisonValue(
        Node left, Node right,
        Func<Expression, Expression, BinaryExpression> cf,
        AbiCtx ctx, Node? cn = null) {
        var lv = CompileValue(left, ctx);
        var rv = CompileValue(right, ctx);
        bool eq = cf == Equal || cf == NotEqual;
        if (eq && AreHeapValues(ctx, left, right)) {
            var lo = HeapValueToObject(lv, ctx);
            var ro = HeapValueToObject(rv, ctx);
            var ec = Call(ObjectEquals, lo, ro);
            return Condition(ec, cf == Equal ? Constant(1L) : Constant(0L),
                                cf == Equal ? Constant(0L) : Constant(1L));
        }
        if (!eq && ctx.Analysis is not null
            && ctx.KindOf(left) == ValueRepresentationKind.HeapRef
            && ctx.KindOf(right) == ValueRepresentationKind.HeapRef) {
            // Relational comparison of heap-resident values (DateOnly/DateTime/
            // Guid/string/...): compare the boxed values, not the raw handles.
            // Analysis-known HeapRef on BOTH operands only — unresolved bag reads
            // (Unknown) keep the scalar path.
            var lo = HeapValueToObject(lv, ctx);
            var ro = HeapValueToObject(rv, ctx);
            var cmp = Call(VmHeapCompare, lo, ro);
            return Condition(cf(cmp, Constant(0)), Constant(1L), Constant(0L));
        }
        if (RingCodec.IsIeee(left, ctx.Analysis) || RingCodec.IsIeee(right, ctx.Analysis))
            return Condition(cf(Call(BitConverterInt64BitsToDouble, lv), Call(BitConverterInt64BitsToDouble, rv)),
                Constant(1L), Constant(0L));
        return Condition(cf(lv, rv), Constant(1L), Constant(0L));
    }

    // ── Ring-based expression helpers ──

    /// <summary>Binary arithmetic — uses CompileValue for operands,</summary>
    private static Expression EmitBinaryArithmetic(
        Node left, Node right,
        Func<Expression, Expression, BinaryExpression> factory,
        AbiCtx ctx) {
        var leftVal = CompileValue(left, ctx);
        var rightVal = CompileValue(right, ctx);
        if (RingCodec.IsIeee(left, ctx.Analysis) || RingCodec.IsIeee(right, ctx.Analysis))
            return SpillToRing(Call(BitConverterDoubleToInt64Bits,
                factory(AsIeeeDouble(left, leftVal, ctx), AsIeeeDouble(right, rightVal, ctx))), ctx);
        var rhs = rightVal;
        if (factory == LeftShift || factory == RightShift) rhs = Convert(rhs, typeof(int));
        return SpillToRing(factory(leftVal, rhs), ctx);
    }

    /// <summary>Converts a long value (heap handle or 0 for null) to an object
    /// reference suitable for comparison. Handle 0 maps to CLR null rather
    /// than being dereferenced via <c>Heap.UnsafeGet</c>.</summary>
    private static Expression HeapValueToObject(Expression handle, AbiCtx ctx) {
        var intHandle = Convert(handle, typeof(int));
        var deref = Call(ctx.HeapLocal,
            HeapUnsafeGet, intHandle);
        return Condition(Equal(handle, Constant(0L)),
            Constant(null, typeof(object)),
            deref);
    }

    /// <summary>Check if both nodes likely produce heap reference values that
    /// should be compared by value rather than handle.</summary>
    private static bool AreHeapValues(AbiCtx ctx, Node left, Node right) {
        if (ctx.Analysis is not null) {
            var leftRep = ctx.KindOf(left);
            var rightRep = ctx.KindOf(right);
            if (leftRep == ValueRepresentationKind.HeapRef
                || rightRep == ValueRepresentationKind.HeapRef)
                return true;
            // Both representations known and neither is a heap ref (StackScalar) →
            // the comparison is scalar. Do NOT fall through to the Member heuristic:
            // a value-typed (e.g. long) member read produces a scalar, and treating it
            // as a heap handle reads garbage heap slots (broken `== 0` in quantifiers).
            if (leftRep != ValueRepresentationKind.Unknown
                && rightRep != ValueRepresentationKind.Unknown)
                return false;
        }
        if (left is Constant cl && cl.Value is string) return true;
        if (right is Constant cr && cr.Value is string) return true;
        if (left is Member) return true;
        if (right is Member) return true;
        return false;
    }

    /// <summary>Short-circuit AND: if left is false, skip right.</summary>
    private static Expression EmitLogicalAnd(And and, AbiCtx ctx) {
        var ring = ctx.OpenRing();
        int leftSlot = ring.Add(CompileNode(and.LeftHandValue, ctx));

        var rightExpr = CompileNode(and.RightHandValue, ctx);
        int rightSlot = ctx.RingDepth - 1;

        var result = Assign(ctx.RingVar(ring.Base),
            ring.Then(
                Condition(
                    Equal(ctx.RingVar(leftSlot), Constant(0L)),
                    Constant(0L),
                    Block(
                        rightExpr,
                        ctx.RingVar(rightSlot)))));
        ctx.RingDepth = ring.Base + 1;
        return result;
    }

    /// <summary>Short-circuit OR: if left is true, skip right.</summary>
    private static Expression EmitLogicalOr(Or or, AbiCtx ctx) {
        var ring = ctx.OpenRing();
        int leftSlot = ring.Add(CompileNode(or.LeftHandValue, ctx));

        var rightExpr = CompileNode(or.RightHandValue, ctx);
        int rightSlot = ctx.RingDepth - 1;

        var result = Assign(ctx.RingVar(ring.Base),
            ring.Then(
                Condition(
                    NotEqual(ctx.RingVar(leftSlot), Constant(0L)),
                    Constant(1L),
                    Block(
                        rightExpr,
                        ctx.RingVar(rightSlot)))));
        ctx.RingDepth = ring.Base + 1;
        return result;
    }

    /// <summary>Logical NOT: 0→1, anything else→0.</summary>
    private static Expression EmitNot(Not not, AbiCtx ctx) {
        var ring = ctx.OpenRing();
        int slot = ring.Add(CompileNode(not.Value, ctx));
        var result = Assign(ctx.RingVar(slot),
            Condition(Equal(ctx.RingVar(slot), Constant(0L)), Constant(1L), Constant(0L)));
        return ring.Then(result);
    }

    /// <summary>Unary minus: negate value.</summary>
    private static Expression EmitUnaryMinus(UnaryMinus unaryMinus, AbiCtx ctx) {
        var ring = ctx.OpenRing();
        int slot = ring.Add(CompileNode(unaryMinus.Operand, ctx));
        Expression result;
        if (RingCodec.IsIeee(unaryMinus.Operand, ctx.Analysis)) {
            var dbl = Call(BitConverterInt64BitsToDouble, ctx.RingVar(slot));
            result = Assign(ctx.RingVar(slot),
                Call(BitConverterDoubleToInt64Bits, Negate(dbl)));
        }
        else {
            result = Assign(ctx.RingVar(slot), Negate(ctx.RingVar(slot)));
        }
        return ring.Then(result);
    }

    private static Expression EmitBitwiseNot(BitwiseNot n, AbiCtx ctx) {
        var ring = ctx.OpenRing();
        int slot = ring.Add(CompileNode(n.Operand, ctx));
        return ring.Then(Assign(ctx.RingVar(slot), Not(ctx.RingVar(slot))));
    }

    /// <summary>PopCount via System.Numerics.BitOperations.PopCount.</summary>
    private static Expression EmitPopCount(PopCount pc, AbiCtx ctx) {
        var ring = ctx.OpenRing();
        int slot = ring.Add(CompileNode(pc.Operand, ctx));
        var call = Call(null, BitOperationsPopCount,
            Convert(ctx.RingVar(slot), typeof(ulong)));
        return ring.Then(Assign(ctx.RingVar(slot), Convert(call, typeof(long))));
    }

    /// <summary>Member access via CLR reflection: resolve from analysis metadata
    /// and emit a property getter, field read, or method call.</summary>
    private static bool IsTypeNameReceiver(Node node, AbiCtx ctx) =>
        node is NamedTypeReference or TypeReference or PrimitiveTypeReference or TypeDefinitionReference
        || (ctx.Analysis?.GetNodeReplacement(node) is { } replaced && replaced != node
            && IsTypeNameReceiver(replaced, ctx));

    private static Expression EmitMember(Member m, AbiCtx ctx) {
        var ring = ctx.OpenRing();
        var resolved = ctx.Analysis?.GetResolvedMember(m);

        // Type-name receiver (`DateTime.UtcNow`): the left side is a type, not a value.
        if (IsTypeNameReceiver(m.Value, ctx)) {
            if (resolved?.LifetimeModifier != LifetimeModifier.Static)
                throw new InvalidOperationException(
                    $"Member '{m.MemberName}' on a type name must resolve as a static member.");
            int slot = ctx.AllocSlot();
            return EmitResolvedMember(resolved, null, slot, ctx, Empty());
        }

        int instanceSlot = ring.Add(CompileNode(m.Value, ctx));

        if (resolved?.LifetimeModifier == LifetimeModifier.Static) {
            return EmitResolvedMember(resolved, null, ring.Base, ctx, Block(ring.Steps));
        }

        if (resolved is not null) {
            var declaringTypeDef = resolved.DeclaringTypeDefinition;
            bool isInlineValueType = declaringTypeDef is ClrTypeDefinition clrDef
                && clrDef.RuntimeType.IsValueType
                && RingCodec.IsInline(clrDef.RuntimeType);

            Expression instanceObj;
            if (isInlineValueType) {
                instanceObj = Convert(ctx.RingVar(instanceSlot), typeof(object));
            }
            else {
                instanceObj = Call(ctx.HeapLocal,
                    HeapUnsafeGet,
                    Convert(ctx.RingVar(instanceSlot), typeof(int)));
            }
            var prelude = ring.Then(
                IfThen(
                    Equal(instanceObj, Constant(null, typeof(object))),
                    Throw(New(InvalidOperationExceptionStringCtor,
                        Constant($"Member '{m.MemberName}' requires a non-null instance.")))));
            return EmitResolvedMember(resolved, instanceObj, ring.Base, ctx, prelude);
        }

        throw new InvalidOperationException(
            $"Member '{m.MemberName}' is not resolved.");
    }

    /// <summary>Emit the resolved member access expression and store the result
    /// on the ring.  Uses the member's <see cref="ITypeMember.EmitRead"/> hook so
    /// the emitter stays provider-agnostic — CLR types, AST-backed types, and
    /// future provider types each return their own expression trees.</summary>
    private static Expression EmitResolvedMember(
        ITypeMember resolved,
        Expression? instanceObj,
        int resultSlot,
        AbiCtx ctx,
        Expression instanceExpr) {

        // Polymorphic EmitRead — each ITypeMember implementation provides its
        // own expression tree. CLR properties use Property(inst, propInfo),
        // AST properties use Dictionary indexer, fields use Field(inst, fieldInfo).
        if (resolved.EmitRead(instanceObj) is Expression readExpr) {
            return Block(instanceExpr, Assign(ctx.RingVar(resultSlot),
                ConvertMemberResult(readExpr, resolved, ctx)));
        }

        // Parameterless method call (e.g. ToString, GetHashCode) — invoke via MethodInfo.
        // Methods don't have an EmitRead path; they need explicit invocation.
        if (resolved is ITypeMethod method) {
            var clrMethod = resolved as ClrMethod;
            var methodInfo = clrMethod?.MethodInfo;
            if (methodInfo is not null && methodInfo.GetParameters().Length == 0) {
                Expression? instanceForCall = instanceObj;
                if (instanceObj is not null && methodInfo.DeclaringType?.IsValueType == true) {
                    instanceForCall = Convert(instanceObj, methodInfo.DeclaringType);
                }
                Expression resultExpr = instanceForCall is not null
                    ? Call(instanceForCall, methodInfo)
                    : Call(null, methodInfo);
                return Block(instanceExpr, Assign(ctx.RingVar(resultSlot),
                    ConvertMemberResult(resultExpr, resolved, ctx)));
            }
        }

        // Fallback: return instance
        return instanceExpr;
    }

    /// <summary>Convert a member access result (object?) to the ring ABI (long).</summary>
    private static Expression ConvertMemberResult(Expression readCall, ITypeMember resolved, AbiCtx ctx) {
        var clrType = resolved.MemberTypeDefinition.GetRuntimeType()
            ?? resolved.MemberTypeDefinition.PrimitiveType?.GetClrType();
        return RingCodec.FromClr(readCall, clrType, ctx.HeapLocal);
    }

    /// <summary>TypeIs: test the operand against the target type. A scalar operand is
    /// answered from its static type; a heap operand is tested against the target CLR
    /// type at runtime (so a null handle is false). An unresolvable target fails closed.</summary>
    private static Expression EmitTypeIs(TypeIs t, AbiCtx ctx) {
        var ring = ctx.OpenRing();
        int resultSlot = ring.Add(CompileNode(t.Operand, ctx));

        // The reader folds `x is T` to a primitive/named reference, not a ClrTypeReference,
        // and analysis resolves the TypeIs node itself to bool — so resolve the target from
        // the type reference, never from the node. An unresolvable target (an array or map
        // reference, an unknown name, `void`) fails closed instead of testing against bool.
        var targetType = ResolveClrType(t.TargetTypeReference, t.TargetTypeReference, ctx);
        if (targetType is null || targetType == typeof(void)) {
            throw new InvalidOperationException(
                $"VM compile rejected: cannot resolve the type in '{t}'.");
        }

        // A scalar operand's static type is a value type, so the test is a compile-time
        // answer (`1 is int` true, `1 is long` false — C# `is` tests the runtime type and
        // never widens). A heap operand is a handle (0 = null) tested at runtime, which
        // keeps `string s = null; s is string` false and resolves interfaces for free.
        switch (ctx.RepresentationOf(t.Operand).Kind) {
            case ValueRepresentationKind.StackScalar: {
                    var operandStatic = ctx.ClrTypeOf(t.Operand);
                    if (operandStatic is null) {
                        throw new InvalidOperationException(
                            $"VM compile rejected: cannot resolve the static type of '{t.Operand}' in '{t}'.");
                    }
                    var answer = Ast.Nodes.TypeIs.StaticTypeIsMatch(operandStatic, targetType) ? 1L : 0L;
                    return ring.Then(Assign(ctx.RingVar(resultSlot), Constant(answer)));
                }
            case ValueRepresentationKind.HeapRef: {
                    var heapObj = Call(ctx.HeapLocal,
                        HeapUnsafeGet,
                        Convert(ctx.RingVar(resultSlot), typeof(int)));
                    var result = Condition(Expression.TypeIs(heapObj, targetType), Constant(1L), Constant(0L));
                    return ring.Then(Assign(ctx.RingVar(resultSlot), result));
                }
            default:
                throw new InvalidOperationException(
                    $"VM compile rejected: cannot tell whether '{t.Operand}' is a scalar or a reference in '{t}'.");
        }
    }

    private static Type? ResolveClrType(Node? typeRef, Node typedNode, AbiCtx ctx) {
        if (typeRef is ClrTypeReference ctr)
            return ctr.RuntimeType;
        if (typeRef is PrimitiveTypeReference ptr)
            return ptr.PrimitiveId.GetClrType();
        // Patron? is the same reference type as Patron. The annotation is not a
        // separate CLR type, and the cast node itself often has no resolution.
        if (typeRef is OptionalTypeReference optional)
            return ResolveClrType(optional.InnerType, optional.InnerType, ctx);
        if (ctx.Analysis is not null) {
            var fromNode = ctx.Analysis.GetResolvedType(typedNode)?.GetRuntimeType();
            if (fromNode is not null) return fromNode;
            if (typeRef is not null)
                return ctx.Analysis.GetResolvedType(typeRef)?.GetRuntimeType();
        }
        return null;
    }

    // Well-known awaitables run through these Ref-bound helpers. Their bodies
    // statically reference the BCL GetAwaiter/GetResult, so trimming keeps those
    // members and no provider reflection runs for the common shapes. Any other
    // structurally awaitable type falls back to EmitStructuralAwait (provider).
    private static readonly MethodInfo AwaitTaskVoidInfo =
        Ref.Method((Expression<Action<Task>>)(t => AwaitVoid(t)));
    private static readonly MethodInfo AwaitValueTaskVoidInfo =
        Ref.Method((Expression<Action<ValueTask>>)(t => AwaitVoid(t)));
    private static readonly MethodInfo AwaitTaskResultInfo =
        Ref.Method((Expression<Func<Task<object?>, object?>>)(t => AwaitResult(t)));
    private static readonly MethodInfo AwaitValueTaskResultInfo =
        Ref.Method((Expression<Func<ValueTask<object?>, object?>>)(t => AwaitResult(t)));

    private static void AwaitVoid(Task task) => task.GetAwaiter().GetResult();
    private static void AwaitVoid(ValueTask task) => task.GetAwaiter().GetResult();
    private static T AwaitResult<T>(Task<T> task) => task.GetAwaiter().GetResult();
    private static T AwaitResult<T>(ValueTask<T> task) => task.GetAwaiter().GetResult();

    /// <summary>Await: block on the operand and yield its result —
    /// <c>operand.GetAwaiter().GetResult()</c>, the same shape the LINQ checker emits.
    /// <c>Task</c> / <c>Task&lt;T&gt;</c> / <c>ValueTask</c> / <c>ValueTask&lt;T&gt;</c> run through the
    /// Ref-bound helpers; any other structurally awaitable type resolves
    /// <c>GetAwaiter</c> / <c>GetResult</c> through the type provider.</summary>
    private static Expression EmitAwait(Await await, AbiCtx ctx) {
        var operandType = ctx.Analysis?.GetResolvedType(await.Operand)?.GetRuntimeType();
        if (operandType is null)
            throw new InvalidOperationException(
                $"VM compile rejected: Await operand '{await.Operand}' has no resolved CLR type.");

        var ring = ctx.OpenRing();
        int slot = ring.Add(CompileNode(await.Operand, ctx));
        // A reference awaitable (Task) or a boxed struct awaitable (ValueTask, custom)
        // both arrive as a heap handle; deref then convert to the awaitable type.
        var operandObj = Convert(
            Call(ctx.HeapLocal, HeapUnsafeGet, Convert(ctx.RingVar(slot), typeof(int))),
            operandType);

        MethodInfo? helper = null;
        Type? resultType = null;
        if (operandType == typeof(Task)) {
            helper = AwaitTaskVoidInfo;
        }
        else if (operandType == typeof(ValueTask)) {
            helper = AwaitValueTaskVoidInfo;
        }
        else if (operandType.IsGenericType && operandType.GetGenericTypeDefinition() == typeof(Task<>)) {
            resultType = operandType.GetGenericArguments()[0];
            helper = AwaitTaskResultInfo.GetGenericMethodDefinition().MakeGenericMethod(resultType);
        }
        else if (operandType.IsGenericType && operandType.GetGenericTypeDefinition() == typeof(ValueTask<>)) {
            resultType = operandType.GetGenericArguments()[0];
            helper = AwaitValueTaskResultInfo.GetGenericMethodDefinition().MakeGenericMethod(resultType);
        }

        if (helper is null)
            return EmitStructuralAwait(await, operandObj, ring, slot, ctx);

        var call = Call(helper, operandObj);
        if (resultType is null) {
            return ring.Then(Block(call, Assign(ctx.RingVar(slot), Constant(0L))));
        }
        return ring.Then(Assign(ctx.RingVar(slot), RingCodec.FromClr(call, resultType, ctx.HeapLocal)));
    }

    /// <summary>Await of an awaitable that is not one of the well-known BCL shapes.
    /// <c>GetResult</c> runs only after <c>IsCompleted</c>, blocking on
    /// <c>UnsafeOnCompleted</c> / <c>OnCompleted</c> when it is not.</summary>
    private static Expression EmitStructuralAwait(
        Await await, Expression operandObj, AbiCtx.RingRegion ring, int slot, AbiCtx ctx) {
        var operandTypeDef = ctx.Analysis?.GetResolvedType(await.Operand)
            ?? throw new InvalidOperationException(
                $"VM compile rejected: Await operand '{await.Operand}' has no resolved type.");
        var binding = AwaitPattern.TryBind(operandTypeDef)
            ?? throw new InvalidOperationException(
                $"VM compile rejected: Await operand type '{operandTypeDef.FullName}' is not awaitable. "
                + "It needs a parameterless GetAwaiter(), and the awaiter needs IsCompleted, "
                + "OnCompleted(Action) or UnsafeOnCompleted(Action), and GetResult().");

        var result = AwaitPattern.CompileGetResult(operandObj, binding);
        if (binding.GetResult.ReturnType == typeof(void)) {
            return ring.Then(Block(result, Assign(ctx.RingVar(slot), Constant(0L))));
        }
        return ring.Then(
            Assign(ctx.RingVar(slot), RingCodec.FromClr(result, binding.GetResult.ReturnType, ctx.HeapLocal)));
    }

    /// <summary>TypeAs: heap object assignable to T keeps the handle; otherwise ABI null.</summary>
    private static Expression EmitTypeAs(TypeAs t, AbiCtx ctx) {
        var targetType = ResolveClrType(t.TargetTypeReference, t, ctx)
            ?? throw new InvalidOperationException("VM compile rejected: TypeAs target type is unresolved.");
        if (targetType.IsValueType)
            throw new InvalidOperationException($"VM compile rejected: TypeAs target '{targetType}' is a value type.");
        var ring = ctx.OpenRing();
        int slot = ring.Add(CompileNode(t.Operand, ctx));
        return ring.Then(
            Assign(ctx.RingVar(slot),
                Call(null, TypeAsAbiInfo, ctx.HeapLocal, ctx.RingVar(slot), Constant(targetType, typeof(Type)))));
    }

    /// <summary>TypeCast after analysis rewrites: identity, reference downcast, or unbox.
    /// Numeric and operator conversions are <c>Invoke</c> replacements.</summary>
    private static Expression EmitTypeCast(TypeCast t, AbiCtx ctx) {
        var targetType = ResolveClrType(t.TargetTypeReference, t, ctx)
            ?? throw new InvalidOperationException("VM compile rejected: TypeCast target type is unresolved.");
        Type? sourceType = ctx.ClrTypeOf(t.Operand);
        if (sourceType is null && t.Operand is Constant c && c.Value is not null)
            sourceType = c.Value.GetType();
        var ring = ctx.OpenRing();
        int slot = ring.Add(CompileNode(t.Operand, ctx));
        if (sourceType is null || sourceType == targetType || targetType.IsAssignableFrom(sourceType))
            return Block(ring.Steps);
        return ring.Then(
            Assign(ctx.RingVar(slot),
                Call(null, CastAbiInfo, ctx.HeapLocal, ctx.RingVar(slot),
                    Constant(targetType, typeof(Type)))));
    }

    /// <summary>Default of the resolved type (0 / false / ABI null / heap default struct).</summary>
    private static Expression EmitDefault(Default d, AbiCtx ctx) {
        int slot = ctx.AllocSlot();
        var target = ResolveClrType(d.TargetType, d, ctx);
        if (target is null || (target.IsValueType && RingCodec.IsInline(target))
            || target == typeof(bool) || target == typeof(float) || target == typeof(double)
            || !target.IsValueType)
            return Assign(ctx.RingVar(slot), Constant(0L));
        var instance = Activator.CreateInstance(target);
        return Assign(ctx.RingVar(slot),
            Convert(Call(ctx.HeapLocal, HeapAllocate,
                Convert(Constant(instance, typeof(object)), typeof(object))), typeof(long)));
    }

    private static Expression EmitTypeOf(TypeOf t, AbiCtx ctx) {
        var runtime = ResolveClrType(t.Type, t, ctx)
            ?? throw new InvalidOperationException("VM compile rejected: TypeOf type is unresolved.");
        int slot = ctx.AllocSlot();
        return Assign(ctx.RingVar(slot),
            Convert(Call(ctx.HeapLocal, HeapAllocate, Convert(Constant(runtime, typeof(object)), typeof(object))),
                typeof(long)));
    }

    /// <summary>StridedSetBits: bit-level strided set (handle, start, step, limit).</summary>
    private static Expression EmitStridedSetBits(StridedSetBits ssb, AbiCtx ctx) {
        var ring = ctx.OpenRing();
        int arrSlot = ring.Add(CompileNode(ssb.Array, ctx));
        int startSlot = ring.Add(CompileNode(ssb.StartValue, ctx));
        int stepSlot = ring.Add(CompileNode(ssb.Step, ctx));
        int limitSlot = ring.Add(CompileNode(ssb.Limit, ctx));

        // ABI-level strided set — heap array path (direct cast to long[]).
        var arrObj = RingCodec.LongArray(ctx.HeapLocal, ctx.RingVar(arrSlot));
        var j = Variable(typeof(long), "_bits_j");
        var loopStart = Label("_stride_loop");
        var loopEnd = Label("_stride_done");
        var loopBody = Block(
            Assign(ArrayAccess(arrObj, Convert(RightShift(j, Constant(6)), typeof(int))),
                Or(ArrayAccess(arrObj, Convert(RightShift(j, Constant(6)), typeof(int))),
                    LeftShift(Constant(1L), Convert(And(j, Constant(63L)), typeof(int))))),
            Assign(j, Add(j, ctx.RingVar(stepSlot))), // j += step
            IfThen(GreaterThan(j, ctx.RingVar(limitSlot)), Goto(loopEnd)),
            Goto(loopStart)
        );
        var result = Block(
            [j],
            Assign(j, ctx.RingVar(startSlot)), // j = start
            Label(loopStart),
            loopBody,
            Label(loopEnd)
        );
        return ring.Then(result);
    }

    /// <summary>Variable reference: read from value stack or from
    /// closure capture array (if this is a captured upvalue).
    /// Leaving the value on the ring for expression chaining.</summary>
    private static Expression EmitVariable(Variable v, AbiCtx ctx) {
        if (ctx.TryGetCapture(v, out int capIndex)) {
            int slot = ctx.AllocSlot();
            return Assign(ctx.RingVar(slot), EmitCaptureCellValue(ctx, capIndex));
        }

        int slot2 = ctx.AllocSlot();
        return Assign(ctx.RingVar(slot2), ctx.VariableRead(v));
    }
}