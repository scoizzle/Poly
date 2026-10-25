using System.Linq.Expressions;
using System.Reflection;

using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Abi;

public static partial class AbiEmitter {
    /// <summary>Comparison as a bool Expression (no 0/1 long).</summary>
    private static Expression CompileCompareTest(
        Node left, Node right,
        Func<Expression, Expression, BinaryExpression> cf,
        Context ctx) {
        bool eq = cf == Equal || cf == NotEqual;
        // A plain scalar slot holds the value itself, so it can never be null: the
        // answer is known from the static type and must not reach the heap path,
        // which would dereference the word as a handle. An optional scalar is
        // heap-resident (Nullable<T>), so its null test keeps the sentinel path.
        if (eq && FoldScalarNullTest(left, right, cf, ctx) is { } folded)
            return folded;
        // A nullable value is a heap handle, so a value comparison must unbox it and
        // apply C#'s lifted rule: an absent operand is false for ordering and equal
        // only to another absent operand.
        if (TryLiftedComparison(left, right, cf, eq, ctx) is { } lifted)
            return lifted;
        var lv = ValueOf(left, ctx);
        var rv = ValueOf(right, ctx);
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
        bool leftIeee = RingCodec.IsIeee(left, ctx.Analysis);
        bool rightIeee = RingCodec.IsIeee(right, ctx.Analysis);
        if (leftIeee || rightIeee) {
            var target = IeeeArithmeticType(
                leftIeee ? ctx.ClrTypeOf(left) : null,
                rightIeee ? ctx.ClrTypeOf(right) : null);
            return cf(
                AsIeee(left, lv, target, ctx),
                AsIeee(right, rv, target, ctx));
        }
        // The operands must compare at one width: `Expression.Equal(int, long)` has no
        // such overload. Two ints compare as ints; one long makes it a 64-bit compare.
        var width = lv.Type == rv.Type ? lv.Type
            : lv.Type == typeof(int) || rv.Type == typeof(int) ? typeof(long)
            : lv.Type;
        return cf(RingCodec.AsRepresentation(lv, width), RingCodec.AsRepresentation(rv, width));
    }

    /// <summary>Comparison as a ring value: the bool test the condition path already uses,
    /// materialised as 0/1. One body means the two forms cannot drift apart.</summary>
    private static Expression EmitComparisonValue(
        Node left, Node right,
        Func<Expression, Expression, BinaryExpression> cf,
        Context ctx) =>
        Condition(CompileCompareTest(left, right, cf, ctx), Constant(1L), Constant(0L));

    /// <summary>Comparison in ring form: the value form, spilled.</summary>
    private static Expression EmitComparison(
        Node left, Node right,
        Func<Expression, Expression, BinaryExpression> cf,
        Context ctx) =>
        SpillToRing(EmitComparisonValue(left, right, cf, ctx), ctx);

    // ── Ring-based expression helpers ──

    /// <summary>Binary arithmetic in ring form: the value form, spilled. Keeping
    /// one body means the IEEE and shift rules cannot drift between the two.</summary>
    private static Expression EmitBinaryArithmetic(
        Node owner, Node left, Node right,
        Func<Expression, Expression, BinaryExpression> factory,
        Context ctx) =>
        SpillToRing(EmitBinaryArithmeticValue(owner, left, right, factory, ctx), ctx);

    /// <summary>Converts a long value (heap handle or 0 for null) to an object
    /// reference suitable for comparison. Handle 0 maps to CLR null rather
    /// than being dereferenced via <c>Heap.UnsafeGet</c>.</summary>
    private static Expression HeapValueToObject(Expression handle, Context ctx) {
        var intHandle = Convert(handle, typeof(int));
        var deref = Call(ctx.HeapLocal,
            HeapUnsafeGet, intHandle);
        return Condition(Equal(handle, Constant(0L)),
            Constant(null, typeof(object)),
            deref);
    }

    /// <summary>A scalar compared with the null constant is a compile-time answer:
    /// <c>== null</c> is false and <c>!= null</c> is true, because the slot holds a
    /// value rather than a null sentinel. The scalar operand still emits — its own
    /// effects (a store, a call) are kept in the tree — only the null test folds, so
    /// no heap or word sentinel check is ever emitted for this comparison.</summary>
    private static Expression? FoldScalarNullTest(
        Node left, Node right,
        Func<Expression, Expression, BinaryExpression> cf,
        Context ctx) {
        if (ctx.Analysis is null)
            return null;
        var scalar = left is Constant { Value: null } ? right
            : right is Constant { Value: null } ? left
            : null;
        if (scalar is null || ctx.KindOf(scalar) != ValueRepresentationKind.StackScalar)
            return null;
        return Block(ValueOf(scalar, ctx),
            Constant(cf == Equal ? false : true));
    }

    /// <summary>The underlying CLR type of a nullable value operand, or null when the
    /// node is not a nullable value.</summary>
    private static Type? NullableUnderlyingOf(Node node, Context ctx) =>
        ctx.ClrTypeOf(node) is { } clr ? Nullable.GetUnderlyingType(clr) : null;

    /// <summary>True when a nullable operand's slot is the heap handle the ABI emits
    /// for a parameter (root SetArgs or a call frame). A nullable produced by the
    /// legacy member/return encoder is still a scalar word and keeps that path.</summary>
    private static bool IsHeapNullableOperand(Node node, Context ctx) =>
        node is Parameter || (node is Variable v && ctx.BoundParameter(v) is not null);

    /// <summary>True for a heap value that is a reference or a non-inline value type —
    /// the existing heap path's operands, not a nullable value.</summary>
    private static bool IsNonNullableHeap(Node node, Context ctx) =>
        ctx.Analysis is not null
        && ctx.KindOf(node) == ValueRepresentationKind.HeapRef
        && NullableUnderlyingOf(node, ctx) is null;

    /// <summary>A nullable heap slot's presence: handle 0 is the null sentinel.</summary>
    private static Expression NullablePresent(Expression handle) =>
        NotEqual(handle, Constant(0L));

    /// <summary>The underlying value of a nullable heap slot. The caller guards
    /// presence, so handle 0 is never unboxed.</summary>
    private static Expression UnboxNullable(Expression handle, Type underlying, Context ctx) =>
        Convert(Call(ctx.HeapLocal, HeapUnsafeGet, Convert(handle, typeof(int))), underlying);

    private static bool IsIeeeType(Type type) => type == typeof(float) || type == typeof(double);

    /// <summary>An unboxed value at the width an integral operation runs at. A bool is
    /// 0/1 and an enum is its underlying integer, because the ring word carries the
    /// number, not the declared CLR type.</summary>
    private static Expression LiftedOperand(Expression value, Type valueType, Type width) {
        if (valueType == typeof(bool))
            value = Condition(value, Constant(1L), Constant(0L));
        else if (value.Type.IsEnum)
            value = Convert(value, typeof(long));
        else if (value.Type != typeof(int) && value.Type != typeof(long) && !IsIeeeType(value.Type))
            value = Convert(value, typeof(long));
        return RingCodec.AsRepresentation(value, width);
    }

    /// <summary>A value comparison where at least one operand is a nullable value.
    /// Returns null when neither side is nullable, or when the other side is the null
    /// constant (the sentinel path) or a reference/non-inline heap value (the existing
    /// heap path). Absent is false for ordering, and equal only to absent.</summary>
    private static Expression? TryLiftedComparison(
        Node left, Node right,
        Func<Expression, Expression, BinaryExpression> cf,
        bool eq, Context ctx) {
        if (ctx.Analysis is null)
            return null;
        var leftUnderlying = NullableUnderlyingOf(left, ctx);
        var rightUnderlying = NullableUnderlyingOf(right, ctx);
        if (leftUnderlying is null && rightUnderlying is null)
            return null;
        bool anyHeap = (leftUnderlying is not null && IsHeapNullableOperand(left, ctx))
            || (rightUnderlying is not null && IsHeapNullableOperand(right, ctx));
        bool allHeap = (leftUnderlying is null || IsHeapNullableOperand(left, ctx))
            && (rightUnderlying is null || IsHeapNullableOperand(right, ctx));
        if (anyHeap && !allHeap)
            throw new InvalidOperationException(
                "VM compile rejected: cannot compare a heap optional value with a "
                + "scalar-encoded optional value.");
        if (!anyHeap)
            return null;
        if (left is Constant { Value: null } || right is Constant { Value: null })
            return null;
        if (IsNonNullableHeap(left, ctx) || IsNonNullableHeap(right, ctx))
            return null;

        var lv = ValueOf(left, ctx);
        var rv = ValueOf(right, ctx);
        var leftPresent = leftUnderlying is null ? null : NullablePresent(lv);
        var rightPresent = rightUnderlying is null ? null : NullablePresent(rv);
        var leftType = leftUnderlying ?? ctx.ClrTypeOf(left) ?? lv.Type;
        var rightType = rightUnderlying ?? ctx.ClrTypeOf(right) ?? rv.Type;
        var leftValue = leftPresent is null
            ? lv
            : Condition(leftPresent, UnboxNullable(lv, leftType, ctx), Default(leftType));
        var rightValue = rightPresent is null
            ? rv
            : Condition(rightPresent, UnboxNullable(rv, rightType, ctx), Default(rightType));
        Expression present = leftPresent is null ? rightPresent!
            : rightPresent is null ? leftPresent
            : AndAlso(leftPresent, rightPresent);

        bool ieee = IsIeeeType(leftType) || IsIeeeType(rightType);
        Expression Value(Expression value, Type type) =>
            ieee ? Convert(value, typeof(double)) : LiftedOperand(value, type, typeof(long));
        if (!eq)
            return AndAlso(present, cf(Value(leftValue, leftType), Value(rightValue, rightType)));

        // Equality is true when both are present and their values match, or when both
        // are absent. A scalar side is always present.
        var areEqual = AndAlso(present, System.Linq.Expressions.Expression.Equal(
            Value(leftValue, leftType), Value(rightValue, rightType)));
        if (leftPresent is not null && rightPresent is not null)
            areEqual = OrElse(areEqual, AndAlso(Not(leftPresent), Not(rightPresent)));
        return cf == Equal ? areEqual : Not(areEqual);
    }

    /// <summary>Check if both nodes likely produce heap reference values that
    /// should be compared by value rather than handle.</summary>
    private static bool AreHeapValues(Context ctx, Node left, Node right) {
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

    /// <summary>Short-circuit AND: if left is false, skip right. Answers in the left
    /// operand's ring slot — an in-place shape, not a spill of a value form.</summary>
    private static Expression EmitLogicalAnd(And and, Context ctx) {
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
                        ctx.RingWord(rightSlot, rightExpr.Type)))));
        ctx.RingDepth = ring.Base + 1;
        return result;
    }

    /// <summary>Short-circuit OR: if left is true, skip right. Answers in the left
    /// operand's ring slot — an in-place shape, not a spill of a value form.</summary>
    private static Expression EmitLogicalOr(Or or, Context ctx) {
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
                        ctx.RingWord(rightSlot, rightExpr.Type)))));
        ctx.RingDepth = ring.Base + 1;
        return result;
    }

    /// <summary>Logical NOT: 0→1, anything else→0, in the operand's own ring slot — an
    /// in-place shape, not a spill of a value form.</summary>
    private static Expression EmitNot(Not not, Context ctx) {
        var ring = ctx.OpenRing();
        int slot = ring.Add(CompileNode(not.Value, ctx));
        var result = Assign(ctx.RingVar(slot),
            Condition(Equal(ctx.RingVar(slot), Constant(0L)), Constant(1L), Constant(0L)));
        return ring.Then(result);
    }

    /// <summary>Unary minus: negate the operand in its own ring slot — an in-place shape,
    /// not a spill of a value form.</summary>
    private static Expression EmitUnaryMinus(UnaryMinus unaryMinus, Context ctx) {
        if (NullableUnderlyingOf(unaryMinus.Operand, ctx) is { } underlying
            && IsHeapNullableOperand(unaryMinus.Operand, ctx))
            return EmitLiftedUnary(unaryMinus.Operand, underlying, Negate, ctx);
        var ring = ctx.OpenRing();
        int slot = ring.Add(CompileNode(unaryMinus.Operand, ctx));
        Expression result;
        if (RingCodec.IsIeee(unaryMinus.Operand, ctx.Analysis)) {
            var dbl = RingCodec.FromIeee(ctx.RingVar(slot));
            result = Assign(ctx.RingVar(slot),
                RingCodec.ToIeee(Negate(dbl)));
        }
        else {
            result = Assign(ctx.RingVar(slot), Negate(ctx.RingVar(slot)));
        }
        return ring.Then(result);
    }

    /// <summary>Bitwise NOT in the operand's own ring slot — an in-place shape, not a spill
    /// of a value form.</summary>
    private static Expression EmitBitwiseNot(BitwiseNot n, Context ctx) {
        if (NullableUnderlyingOf(n.Operand, ctx) is { } underlying
            && IsHeapNullableOperand(n.Operand, ctx))
            return EmitLiftedUnary(n.Operand, underlying, Not, ctx);
        var ring = ctx.OpenRing();
        int slot = ring.Add(CompileNode(n.Operand, ctx));
        return ring.Then(Assign(ctx.RingVar(slot), Not(ctx.RingVar(slot))));
    }

    /// <summary>Unary operator on a nullable value: unbox, apply, box. An absent
    /// operand yields the null sentinel rather than the handle.</summary>
    private static Expression EmitLiftedUnary(
        Node operand, Type underlying, Func<Expression, Expression> op, Context ctx) {
        var ring = ctx.OpenRing();
        int slot = ring.Add(CompileNode(operand, ctx));
        var handle = ctx.RingVar(slot);
        var present = NullablePresent(handle);
        var value = Condition(present, UnboxNullable(handle, underlying, ctx), Default(underlying));
        Expression result;
        if (IsIeeeType(underlying)) {
            var target = underlying == typeof(float) ? typeof(float) : typeof(double);
            result = op(Convert(value, typeof(double)));
            if (result.Type != target)
                result = Convert(result, target);
        }
        else {
            result = op(LiftedOperand(value, underlying, RingCodec.SlotTypeOf(underlying)));
        }
        Expression boxed = underlying == typeof(bool)
            ? Condition(
                NotEqual(Convert(result, typeof(long)), Constant(0L)),
                Constant(true, typeof(object)),
                Constant(false, typeof(object)))
            : Convert(Convert(result, underlying), typeof(object));
        int outSlot = ctx.AllocSlot();
        return ring.Then(Assign(ctx.RingVar(outSlot), Condition(
            present,
            Convert(Call(ctx.HeapLocal, HeapAllocate, boxed), typeof(long)),
            Constant(0L))));
    }

    /// <summary>PopCount via System.Numerics.BitOperations.PopCount, in the operand's own
    /// ring slot — an in-place shape, not a spill of a value form.</summary>
    private static Expression EmitPopCount(PopCount pc, Context ctx) {
        var ring = ctx.OpenRing();
        int slot = ring.Add(CompileNode(pc.Operand, ctx));
        var call = Call(null, BitOperationsPopCount,
            Convert(ctx.RingVar(slot), typeof(ulong)));
        return ring.Then(Assign(ctx.RingVar(slot), Convert(call, typeof(long))));
    }

    /// <summary>Member access via CLR reflection: resolve from analysis metadata
    /// and emit a property getter, field read, or method call.</summary>
    private static bool IsTypeNameReceiver(Node node, Context ctx) =>
        node is NamedTypeReference or TypeReference or PrimitiveTypeReference or TypeDefinitionReference
        || (ctx.Analysis?.GetNodeReplacement(node) is { } replaced && replaced != node
            && IsTypeNameReceiver(replaced, ctx));

    private static Expression EmitMember(Member m, Context ctx) {
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
        Context ctx,
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
    private static Expression ConvertMemberResult(Expression readCall, ITypeMember resolved, Context ctx) {
        var clrType = resolved.MemberTypeDefinition.GetRuntimeType()
            ?? resolved.MemberTypeDefinition.PrimitiveType?.GetClrType();
        return RingCodec.FromClr(readCall, clrType, ctx.HeapLocal);
    }

    /// <summary>TypeIs: test the operand against the target type. A scalar operand is
    /// answered from its static type; a heap operand is tested against the target CLR
    /// type at runtime (so a null handle is false). An unresolvable target fails closed.</summary>
    private static Expression EmitTypeIs(TypeIs t, Context ctx) {
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

    private static Type? ResolveClrType(Node? typeRef, Node typedNode, Context ctx) =>
        ctx.TryClrType(typedNode)
        ?? (typeRef is null ? null : ctx.TryClrType(typeRef));

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
    private static Expression EmitAwait(Await await, Context ctx) {
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
        Await await, Expression operandObj, Context.RingRegion ring, int slot, Context ctx) {
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
    private static Expression EmitTypeAs(TypeAs t, Context ctx) {
        var targetType = ResolveClrType(t.TargetTypeReference, t.TargetTypeReference, ctx)
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
    private static Expression EmitTypeCast(TypeCast t, Context ctx) {
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
    private static Expression EmitDefault(Default d, Context ctx) {
        int slot = ctx.AllocSlot();
        var target = ResolveClrType(d.TargetType, d, ctx);
        if (target == typeof(double) || target == typeof(float))
            return Assign(ctx.RingVar(slot, target), Default(target));
        if (target is null || (target.IsValueType && RingCodec.IsInline(target))
            || target == typeof(bool)
            || !target.IsValueType)
            return Assign(ctx.RingVar(slot), Constant(0L));
        var instance = Activator.CreateInstance(target);
        return Assign(ctx.RingVar(slot),
            Convert(Call(ctx.HeapLocal, HeapAllocate,
                Convert(Constant(instance, typeof(object)), typeof(object))), typeof(long)));
    }

    private static Expression EmitTypeOf(TypeOf t, Context ctx) {
        var runtime = ResolveClrType(t.Type, t.Type, ctx)
            ?? throw new InvalidOperationException("VM compile rejected: TypeOf type is unresolved.");
        // The Type is known here, so its heap entry is seeded once per program (Build) and this
        // arm is a local read. Allocating inline made a typeof in a loop one heap entry per
        // iteration, and nothing recycles a type object.
        int slot = ctx.AllocSlot();
        return Assign(ctx.RingVar(slot), ctx.TypeHandleLocal(runtime));
    }

    /// <summary>StridedSetBits: bit-level strided set (handle, start, step, limit).</summary>
    private static Expression EmitStridedSetBits(StridedSetBits ssb, Context ctx) {
        var ring = ctx.OpenRing();
        // A promoted receiver is the cached CLR local itself: the loop reads and writes it
        // directly, so neither the read nor the write re-derefs the heap.
        ParameterExpression? cachedArray = null;
        bool cached = ssb.Array is Variable receiver
            && ctx.TryArrayCache(receiver, out cachedArray)
            && cachedArray!.Type == typeof(long[]);
        int arrSlot = cached ? -1 : ring.Add(CompileNode(ssb.Array, ctx));
        int startSlot = ring.Add(CompileNode(ssb.StartValue, ctx));
        int stepSlot = ring.Add(CompileNode(ssb.Step, ctx));
        int limitSlot = ring.Add(CompileNode(ssb.Limit, ctx));

        // ABI-level strided set: the promoted local, or the heap array cast to long[].
        var arrObj = cached ? cachedArray! : RingCodec.LongArray(ctx.HeapLocal, ctx.RingVar(arrSlot));
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

    /// <summary>Variable reference: reads whatever storage the binding resolves
    /// to — a frame slot, a register, or a capture cell — and leaves the value
    /// on the ring for expression chaining. An IEEE register holds the value itself, so
    /// the read is a copy; a word-backed storage (frame slot, capture cell) converts once
    /// here, at the storage boundary, instead of once per use inside the expression that
    /// follows.</summary>
    private static Expression EmitVariable(Variable v, Context ctx) {
        if (ctx.Resolve(v) is null)
            throw new InvalidOperationException(
                $"VM compile rejected: variable '{v.Name}' is not declared in any scope.");
        int slot = ctx.AllocSlot();
        var slotType = ctx.SlotTypeOf(v);
        return Assign(ctx.RingVar(slot, slotType), ctx.VariableReadAs(v, slotType));
    }
}