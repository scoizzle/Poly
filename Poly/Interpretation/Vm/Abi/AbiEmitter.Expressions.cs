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
        var lv = ValueOf(left, ctx);
        var rv = ValueOf(right, ctx);
        bool eq = cf == Equal || cf == NotEqual;
        if (!eq && (IsNullConstant(left) || IsNullConstant(right)))
            throw new InvalidOperationException(
                "VM compile rejected: no ordering comparison with null.");
        if (Nullable.GetUnderlyingType(lv.Type) is not null
            || Nullable.GetUnderlyingType(rv.Type) is not null)
            return CompileNullableCompare(left, right, lv, rv, cf, eq, ctx);
        // A required value cannot be null. The answer is a constant, and the word
        // is not a handle: dereferencing it is how `x != null` came back false.
        if (eq && (IsNullConstant(left) || IsNullConstant(right))) {
            if (IsNullConstant(left) && IsNullConstant(right))
                return Constant(cf == Equal);
            var otherNode = IsNullConstant(left) ? right : left;
            var otherVal = IsNullConstant(left) ? rv : lv;
            var otherClr = ctx.ClrTypeOf(otherNode);
            if (otherClr is { IsValueType: true } && Nullable.GetUnderlyingType(otherClr) is null)
                return Block(otherVal, Constant(cf == NotEqual));
        }
        if (!eq && (IsBoolOperand(left, lv, ctx) || IsBoolOperand(right, rv, ctx)))
            throw new InvalidOperationException(
                "VM compile rejected: no ordering operator for 'Boolean'.");
        if (TryDecimalCompare(left, right, lv, rv, cf, ctx) is { } decimalCompare)
            return decimalCompare;
        if (eq && AreHeapValues(ctx, left, right)) {
            var lo = CompareObject(left, lv, ctx);
            var ro = CompareObject(right, rv, ctx);
            var ec = Call(ObjectEquals, lo, ro);
            return cf == Equal ? ec : Not(ec);
        }
        if (!eq && ctx.Analysis is not null
            && ctx.KindOf(left) == ValueRepresentationKind.HeapRef
            && ctx.KindOf(right) == ValueRepresentationKind.HeapRef) {
            // Relational comparison of heap-resident values (Guid, string, decimal):
            // compare the boxed values, not the raw handles.
            // Analysis-known HeapRef on BOTH operands only — unresolved bag reads
            // (Unknown) keep the scalar path.
            var lo = CompareObject(left, lv, ctx);
            var ro = CompareObject(right, rv, ctx);
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
                RingCodec.FromFrame(lv, ctx.ClrTypeOf(left) ?? lv.Type, target, ctx.HeapLocal),
                RingCodec.FromFrame(rv, ctx.ClrTypeOf(right) ?? rv.Type, target, ctx.HeapLocal));
        }
        // A temporal word orders as the CLR value. ToBinary packs Kind into the
        // high bits, so a raw-word compare is not DateTime's own order.
        if (TemporalWord(ctx.ClrTypeOf(left)) || TemporalWord(ctx.ClrTypeOf(right)))
            return CompareTemporal(left, right, lv, rv, cf, eq, ctx);
        // The operands must compare at one width: `Expression.Equal(int, long)` has no
        // such overload. Two ints compare as ints; one long makes it a 64-bit compare.
        var width = lv.Type == rv.Type ? lv.Type
            : lv.Type == typeof(int) || rv.Type == typeof(int) ? typeof(long)
            : lv.Type;
        return cf(RingCodec.AsRepresentation(lv, width), RingCodec.AsRepresentation(rv, width));
    }

    private static bool IsNullConstant(Node node) => node is Constant { Value: null };

    private static bool IsBoolOperand(Node node, Expression value, Context ctx) =>
        UnderlyingOf(node, value, ctx) == typeof(bool);

    /// <summary>Lifted comparison. Equality of <c>bool?</c> stays bool. Ordering of
    /// bool, and a pair C# has no operator for, fail closed.</summary>
    private static Expression CompileNullableCompare(
        Node left, Node right, Expression lv, Expression rv,
        Func<Expression, Expression, BinaryExpression> cf, bool eq, Context ctx) {
        var leftType = UnderlyingOf(left, lv, ctx);
        var rightType = UnderlyingOf(right, rv, ctx);
        if (leftType is null)
            leftType = rightType;
        if (rightType is null)
            rightType = leftType;
        if (leftType is null || rightType is null)
            throw new InvalidOperationException(
                "VM compile rejected: cannot resolve a nullable comparison.");
        Type promoted;
        if (leftType == typeof(bool) || rightType == typeof(bool)) {
            if (!eq || leftType != typeof(bool) || rightType != typeof(bool))
                throw RejectBinary(leftType, rightType);
            promoted = typeof(bool);
        }
        else {
            promoted = CSharpBinaryNumericType(leftType, rightType, shift: false);
        }
        var nullable = typeof(Nullable<>).MakeGenericType(promoted);
        return cf(
            AsNullableOperand(left, lv, nullable, ctx),
            AsNullableOperand(right, rv, nullable, ctx));
    }

    /// <summary>A required <c>decimal</c> compared with an integral converts the
    /// integral. <c>decimal</c> against <c>float</c> or <c>double</c> has no operator.
    /// Same-type decimal stays on the heap path.</summary>
    private static Expression? TryDecimalCompare(
        Node left, Node right, Expression lv, Expression rv,
        Func<Expression, Expression, BinaryExpression> cf, Context ctx) {
        var leftType = UnderlyingOf(left, lv, ctx);
        var rightType = UnderlyingOf(right, rv, ctx);
        if (leftType != typeof(decimal) && rightType != typeof(decimal))
            return null;
        if (leftType is not null && RingCodec.IsIeee(leftType)
            || rightType is not null && RingCodec.IsIeee(rightType))
            throw RejectBinary(leftType, rightType);
        if (leftType == typeof(decimal) && rightType == typeof(decimal))
            return null;
        if (!ConvertsToDecimal(leftType) || !ConvertsToDecimal(rightType))
            return null;
        return cf(
            RingCodec.FromFrame(lv, UnderlyingOf(left, lv, ctx) ?? lv.Type, typeof(decimal), ctx.HeapLocal),
            RingCodec.FromFrame(rv, UnderlyingOf(right, rv, ctx) ?? rv.Type, typeof(decimal), ctx.HeapLocal));
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
        Node left, Node right,
        Func<Expression, Expression, BinaryExpression> factory,
        Context ctx) =>
        SpillToRing(EmitBinaryArithmeticValue(left, right, factory, ctx), ctx);

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

    /// <summary>One side of a heap comparison. A temporal word is the CLR value;
    /// a handle is still the heap object (0 is null).</summary>
    private static Expression CompareObject(Node node, Expression value, Context ctx) {
        var clr = ctx.ClrTypeOf(node);
        if (TemporalWord(clr))
            return Convert(LocalType.Decode(value, clr, clr!, ctx.HeapLocal), typeof(object));
        return HeapValueToObject(value, ctx);
    }

    private static Expression CompareTemporal(
        Node left, Node right, Expression lv, Expression rv,
        Func<Expression, Expression, BinaryExpression> cf, bool eq, Context ctx) {
        var leftClr = ctx.ClrTypeOf(left);
        var rightClr = ctx.ClrTypeOf(right);
        var l = TemporalWord(leftClr)
            ? LocalType.Decode(lv, leftClr, leftClr!, ctx.HeapLocal)
            : lv;
        var r = TemporalWord(rightClr)
            ? LocalType.Decode(rv, rightClr, rightClr!, ctx.HeapLocal)
            : rv;
        if (l.Type == r.Type)
            return cf(l, r);
        var lo = Convert(l, typeof(object));
        var ro = Convert(r, typeof(object));
        if (!eq)
            return cf(Call(VmHeapCompare, lo, ro), Constant(0));
        var same = Call(ObjectEquals, lo, ro);
        return cf == Equal ? same : Not(same);
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
    /// not a spill of a value form. A nullable operand is already the value, so negating
    /// the frame word would negate the handle.</summary>
    private static Expression EmitUnaryMinus(UnaryMinus unaryMinus, Context ctx) {
        var ring = ctx.OpenRing();
        var compiled = Compile(unaryMinus.Operand, ctx);
        if (NullableValue(compiled, ctx) is { } nullable)
            return SpillToRing(Negate(nullable), ctx);
        int slot = ring.Add(compiled.Code);
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
    /// of a value form. A nullable operand uses the lifted operator.</summary>
    private static Expression EmitBitwiseNot(BitwiseNot n, Context ctx) {
        var ring = ctx.OpenRing();
        var compiled = Compile(n.Operand, ctx);
        if (NullableValue(compiled, ctx) is { } nullable)
            return SpillToRing(Not(nullable), ctx);
        int slot = ring.Add(compiled.Code);
        return ring.Then(Assign(ctx.RingVar(slot), Not(ctx.RingVar(slot))));
    }

    /// <summary>The operand's <c>Nullable&lt;T&gt;</c> value, or null when the slot is a word.</summary>
    private static Expression? NullableValue(CompiledNode compiled, Context ctx) =>
        compiled.Result?.Representation is { } rep && Nullable.GetUnderlyingType(rep) is not null
            ? Block(compiled.Code, compiled.Result.Read(ctx))
            : null;

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
            var inlineClr = declaringTypeDef is ClrTypeDefinition clrDef
                && clrDef.RuntimeType.IsValueType
                && RingCodec.IsInline(clrDef.RuntimeType)
                ? clrDef.RuntimeType
                : null;

            Expression instanceObj;
            if (inlineClr is not null) {
                // A temporal word is not a boxed long. DateTime.Year needs the value.
                instanceObj = LocalType.Of(inlineClr) is LocalType.TemporalLocal { IsWord: true }
                    ? Convert(
                        LocalType.Decode(ctx.RingVar(instanceSlot), inlineClr, inlineClr, ctx.HeapLocal),
                        typeof(object))
                    : Convert(ctx.RingVar(instanceSlot), typeof(object));
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
            return Block(instanceExpr, StoreOnRing(ctx, resultSlot,
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
                return Block(instanceExpr, StoreOnRing(ctx, resultSlot,
                    ConvertMemberResult(resultExpr, resolved, ctx)));
            }
        }

        // Fallback: return instance
        return instanceExpr;
    }

    /// <summary>Convert a member access result to the ring. A <c>Nullable&lt;T&gt;</c>
    /// stays nullable: converting it through <c>FromClr</c> reads <c>.Value</c> and
    /// throws when the member is null.</summary>
    private static Expression ConvertMemberResult(Expression readCall, ITypeMember resolved, Context ctx) {
        var clrType = resolved.MemberTypeDefinition.GetRuntimeType()
            ?? resolved.MemberTypeDefinition.PrimitiveType?.GetClrType();
        if (clrType is not null && Nullable.GetUnderlyingType(clrType) is not null)
            return NullableMemberRead(readCall, clrType);
        // A dictionary property arrives as object. Null is unset (the bag stores
        // null for a missing date). Unboxing that null is a raw NRE; the call
        // used to see a zero handle and name the property.
        if (clrType is not null
            && TemporalWord(clrType)
            && !readCall.Type.IsValueType)
            return TemporalMemberWord(readCall, clrType, resolved.Name, ctx);
        return RingCodec.ToFrame(readCall, clrType ?? readCall.Type, ctx.HeapLocal);
    }

    /// <summary>A temporal property read. A null bag value names the member;
    /// a boxed value becomes its word.</summary>
    private static Expression TemporalMemberWord(Expression read, Type clr, string name, Context ctx) {
        var box = Variable(typeof(object), "_temporal");
        var word = LocalType.Encode(Convert(box, clr), clr, ctx.HeapLocal);
        var ctor = typeof(Poly.DomainModeling.Runtime.ConstraintFailureException)
            .GetConstructor([typeof(string)]);
        if (ctor is null)
            throw new InvalidOperationException(
                "VM compile rejected: ConstraintFailureException(string) is missing.");
        return Block([box],
            Assign(box, read.Type == typeof(object) ? read : Convert(read, typeof(object))),
            Condition(
                Equal(box, Constant(null, typeof(object))),
                Throw(New(ctor, Constant($"'{name}' is not set")), typeof(long)),
                word));
    }

    private static Expression StoreOnRing(Context ctx, int resultSlot, Expression value) =>
        Assign(ctx.RingVar(resultSlot, RingCodec.SlotTypeOf(value.Type)), value);

    /// <summary>A nullable member as <c>Nullable&lt;T&gt;</c>. A CLR or dictionary read
    /// arrives as <c>object</c>: null is absent, and a boxed value unboxes. The read
    /// runs once.</summary>
    private static Expression NullableMemberRead(Expression read, Type nullable) {
        if (read.Type == nullable)
            return read;
        if (Nullable.GetUnderlyingType(read.Type) is not null)
            return Convert(read, nullable);
        if (read.Type != typeof(object))
            return Convert(read, nullable);
        var local = Variable(typeof(object), "_member");
        return Block([local],
            Assign(local, read),
            Condition(
                Equal(local, Constant(null, typeof(object))),
                Default(nullable),
                Convert(local, nullable)));
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
        return ring.Then(Assign(ctx.RingVar(slot), RingCodec.ToFrame(call, resultType, ctx.HeapLocal)));
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
            Assign(ctx.RingVar(slot), RingCodec.ToFrame(result, binding.GetResult.ReturnType, ctx.HeapLocal)));
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
        // An unchecked integer cast wraps, the way `(short)32768` is -32768.
        // Convert.ToInt16 throws, so this is not that call. A reference cast
        // stays on the heap helper below.
        if (IsIntegralCast(sourceType) && IsIntegralCast(targetType)) {
            var asSource = LocalType.Decode(ctx.RingVar(slot), sourceType, sourceType, ctx.HeapLocal);
            Expression converted = t.IsChecked
                ? ConvertChecked(asSource, targetType)
                : Convert(asSource, targetType);
            return ring.Then(Assign(
                ctx.RingVar(slot),
                LocalType.Encode(converted, targetType, ctx.HeapLocal)));
        }
        return ring.Then(
            Assign(ctx.RingVar(slot),
                Call(null, CastAbiInfo, ctx.HeapLocal, ctx.RingVar(slot),
                    Constant(targetType, typeof(Type)))));
    }

    /// <summary>An integer width or an enum. The unchecked cast between two of
    /// these is a wrap, not a heap cast.</summary>
    private static bool IsIntegralCast(Type type) =>
        type == typeof(byte) || type == typeof(sbyte) || type == typeof(short)
        || type == typeof(ushort) || type == typeof(int) || type == typeof(uint)
        || type == typeof(long) || type == typeof(ulong) || type == typeof(char)
        || type.IsEnum;

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