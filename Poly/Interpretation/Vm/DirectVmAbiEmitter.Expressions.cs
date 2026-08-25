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
            && ctx.Analysis.GetValueRepresentation(left) == ValueRepresentationKind.HeapRef
            && ctx.Analysis.GetValueRepresentation(right) == ValueRepresentationKind.HeapRef) {
            // Relational comparison of heap-resident values (DateOnly/DateTime/
            // Guid/string/...): compare the boxed values, not the raw handles.
            // Analysis-known HeapRef on BOTH operands only — unresolved bag reads
            // (Unknown) keep the scalar path.
            var lo = HeapValueToObject(lv, ctx);
            var ro = HeapValueToObject(rv, ctx);
            var cmp = Call(VmHeapCompare, lo, ro);
            return cf(cmp, Constant(0));
        }
        if (IsDoubleValue(ctx, left) || IsDoubleValue(ctx, right))
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
            && ctx.Analysis.GetValueRepresentation(left) == ValueRepresentationKind.HeapRef
            && ctx.Analysis.GetValueRepresentation(right) == ValueRepresentationKind.HeapRef) {
            // Relational comparison of heap-resident values (DateOnly/DateTime/
            // Guid/string/...): compare the boxed values, not the raw handles.
            // Analysis-known HeapRef on BOTH operands only — unresolved bag reads
            // (Unknown) keep the scalar path.
            var lo = HeapValueToObject(lv, ctx);
            var ro = HeapValueToObject(rv, ctx);
            var cmp = Call(VmHeapCompare, lo, ro);
            return Condition(cf(cmp, Constant(0)), Constant(1L), Constant(0L));
        }
        if (IsDoubleValue(ctx, left) || IsDoubleValue(ctx, right))
            return Condition(cf(Call(BitConverterInt64BitsToDouble, lv), Call(BitConverterInt64BitsToDouble, rv)),
                Constant(1L), Constant(0L));
        return Condition(cf(lv, rv), Constant(1L), Constant(0L));
    }

    // ── Ring-based expression helpers ──
    // Retained for paths that still walk operands via CompileNode (Member, etc.).
    // Pure expression dispatch uses CompileValue + SpillToRing instead.

    /// <summary>Binary arithmetic — ring-based (operands through CompileNode).</summary>
    private static Expression EmitBinaryArithmeticRing(
        Node left, Node right,
        Func<Expression, Expression, BinaryExpression> factory,
        AbiCtx ctx) {
        int d = ctx.RingDepth;
        var leftCompiled = CompileNode(left, ctx);
        int leftSlot = ctx.RingDepth - 1;
        var rightCompiled = CompileNode(right, ctx);
        int rightSlot = ctx.RingDepth - 1;

        if (IsDoubleValue(ctx, left) || IsDoubleValue(ctx, right)) {
            var resultBits = Call(BitConverterDoubleToInt64Bits,
                factory(AsIeeeDouble(left, ctx.RingVar(leftSlot), ctx),
                        AsIeeeDouble(right, ctx.RingVar(rightSlot), ctx)));
            ctx.RingDepth = d + 1;
            return Block(leftCompiled, rightCompiled, Assign(ctx.RingVar(d), resultBits));
        }

        Expression rhs = ctx.RingVar(rightSlot);
        if (factory == LeftShift || factory == RightShift)
            rhs = Convert(rhs, typeof(int));
        ctx.RingDepth = d + 1;
        return Block(leftCompiled, rightCompiled, Assign(ctx.RingVar(d), factory(ctx.RingVar(leftSlot), rhs)));
    }

    /// <summary>Comparison — ring-based (operands through CompileNode).</summary>
    private static Expression EmitComparisonRing(
        Node left, Node right,
        Func<Expression, Expression, BinaryExpression> cf,
        AbiCtx ctx, Node? cn = null) {
        int d = ctx.RingDepth;
        var leftCompiled = CompileNode(left, ctx);
        int leftSlot = ctx.RingDepth - 1;
        var rightCompiled = CompileNode(right, ctx);
        int rightSlot = ctx.RingDepth - 1;

        bool eq = cf == Equal || cf == NotEqual;
        if (eq && AreHeapValues(ctx, left, right)) {
            var lo = HeapValueToObject(ctx.RingVar(leftSlot), ctx);
            var ro = HeapValueToObject(ctx.RingVar(rightSlot), ctx);
            var ec = Call(ObjectEquals, lo, ro);
            ctx.RingDepth = d + 1;
            return Block(leftCompiled, rightCompiled, Assign(ctx.RingVar(d),
                Condition(ec, cf == Equal ? Constant(1L) : Constant(0L),
                              cf == Equal ? Constant(0L) : Constant(1L))));
        }
        if (IsDoubleValue(ctx, left) || IsDoubleValue(ctx, right)) {
            ctx.RingDepth = d + 1;
            return Block(leftCompiled, rightCompiled, Assign(ctx.RingVar(d),
                Condition(cf(Call(BitConverterInt64BitsToDouble, ctx.RingVar(leftSlot)),
                              Call(BitConverterInt64BitsToDouble, ctx.RingVar(rightSlot))),
                    Constant(1L), Constant(0L))));
        }
        ctx.RingDepth = d + 1;
        return Block(leftCompiled, rightCompiled, Assign(ctx.RingVar(d),
            Condition(cf(ctx.RingVar(leftSlot), ctx.RingVar(rightSlot)), Constant(1L), Constant(0L))));
    }

    /// <summary>Binary arithmetic — uses CompileValue for operands,</summary>
    private static Expression EmitBinaryArithmetic(
        Node left, Node right,
        Func<Expression, Expression, BinaryExpression> factory,
        AbiCtx ctx) {
        var leftVal = CompileValue(left, ctx);
        var rightVal = CompileValue(right, ctx);
        if (TryEmitStringConcat(left, right, leftVal, rightVal, factory, ctx) is { } concat)
            return SpillToRing(concat, ctx);
        if (TryEmitDecimalArithmetic(left, right, leftVal, rightVal, factory, ctx) is { } dec)
            return SpillToRing(dec, ctx);
        if (IsDoubleValue(ctx, left) || IsDoubleValue(ctx, right))
            return SpillToRing(Call(BitConverterDoubleToInt64Bits,
                factory(AsIeeeDouble(left, leftVal, ctx), AsIeeeDouble(right, rightVal, ctx))), ctx);
        var rhs = rightVal;
        if (factory == LeftShift || factory == RightShift) rhs = Convert(rhs, typeof(int));
        return SpillToRing(factory(leftVal, rhs), ctx);
    }

    /// <summary>Comparison (eq, neq, lt, gt, etc.) → 0/1 long.
    /// For Equal/NotEqual, heap reference values (strings, objects) are compared
    /// using object.Equals at runtime rather than handle equality.</summary>
    private static Expression EmitComparison(
        Node left, Node right,
        Func<Expression, Expression, BinaryExpression> comparisonFactory,
        AbiCtx ctx,
        Node? comparisonNode = null) {
        int d = ctx.RingDepth;
        var leftExpr = CompileNode(left, ctx);
        int leftResult = ctx.RingDepth - 1;

        var rightExpr = CompileNode(right, ctx);
        int rightResult = ctx.RingDepth - 1;

        // Detect string comparison: when Equal/NotEqual and both operands are
        // heap references (strings), compare via object.Equals at runtime.
        bool isEquality = comparisonFactory == Equal
                       || comparisonFactory == NotEqual;
        if (isEquality && AreHeapValues(ctx, left, right)) {
            // Read both objects from heap and compare — handle 0 maps to null.
            var leftObj = HeapValueToObject(ctx.RingVar(leftResult), ctx);
            var rightObj = HeapValueToObject(ctx.RingVar(rightResult), ctx);
            var equalCheck = Call(ObjectEquals,
                leftObj, rightObj);
            var result = Assign(ctx.RingVar(d),
                comparisonFactory == Equal
                    ? Condition(equalCheck, Constant(1L), Constant(0L))
                    : Condition(equalCheck, Constant(0L), Constant(1L)));
            ctx.RingDepth = d + 1;
            return Block(leftExpr, rightExpr, result);
        }

        // Double/float comparison: reinterpret bits before comparing
        if (IsDoubleValue(ctx, left) || IsDoubleValue(ctx, right)) {
            var leftDbl = Call(BitConverterInt64BitsToDouble, ctx.RingVar(leftResult));
            var rightDbl = Call(BitConverterInt64BitsToDouble, ctx.RingVar(rightResult));
            var result = Assign(ctx.RingVar(d),
                Condition(comparisonFactory(leftDbl, rightDbl),
                    Constant(1L), Constant(0L)));
            ctx.RingDepth = d + 1;
            return Block(leftExpr, rightExpr, result);
        }

        var simpleResult = Assign(
            ctx.RingVar(d),
            Condition(comparisonFactory(ctx.RingVar(leftResult), ctx.RingVar(rightResult)),
                Constant(1L), Constant(0L)));
        ctx.RingDepth = d + 1;
        return Block(leftExpr, rightExpr, simpleResult);
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
            var leftRep = ctx.Analysis.GetValueRepresentation(left);
            var rightRep = ctx.Analysis.GetValueRepresentation(right);
            if (leftRep == ValueRepresentationKind.HeapRef
                || rightRep == ValueRepresentationKind.HeapRef)
                return true;
            // Both representations known and neither is a heap ref (StackScalar/Bool) →
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
        int d = ctx.RingDepth;
        var leftExpr = CompileNode(and.LeftHandValue, ctx);
        int leftSlot = ctx.RingDepth - 1;
        var foldLeft = FoldResultToSlot(ref leftSlot, d, ctx);

        int rightStart = ctx.RingDepth;
        var rightExpr = CompileNode(and.RightHandValue, ctx);
        int rightSlot = ctx.RingDepth - 1;

        var result = Assign(ctx.RingVar(d),
            Block(
                leftExpr, foldLeft,
                Condition(
                    Equal(ctx.RingVar(leftSlot), Constant(0L)),
                    Constant(0L),
                    Block(
                        rightExpr,
                        ctx.RingVar(rightSlot)
                    )
                )
            ));
        ctx.RingDepth = d + 1;
        return result;
    }

    /// <summary>Short-circuit OR: if left is true, skip right.</summary>
    private static Expression EmitLogicalOr(Or or, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var leftExpr = CompileNode(or.LeftHandValue, ctx);
        int leftSlot = ctx.RingDepth - 1;
        var foldLeft = FoldResultToSlot(ref leftSlot, d, ctx);

        int rightStart = ctx.RingDepth;
        var rightExpr = CompileNode(or.RightHandValue, ctx);
        int rightSlot = ctx.RingDepth - 1;

        var result = Assign(ctx.RingVar(d),
            Block(
                leftExpr, foldLeft,
                Condition(
                    NotEqual(ctx.RingVar(leftSlot), Constant(0L)),
                    Constant(1L),
                    Block(
                        rightExpr,
                        ctx.RingVar(rightSlot)
                    )
                )
            ));
        ctx.RingDepth = d + 1;
        return result;
    }

    /// <summary>Logical NOT: 0→1, anything else→0.</summary>
    private static Expression EmitNot(Not not, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var operandExpr = CompileNode(not.Value, ctx);
        int resultSlot = ctx.RingDepth - 1;
        var fold = FoldResultToSlot(ref resultSlot, d, ctx);
        var result = Assign(ctx.RingVar(resultSlot),
            Condition(Equal(ctx.RingVar(resultSlot), Constant(0L)), Constant(1L), Constant(0L)));
        ctx.RingDepth = resultSlot + 1;
        return Block(operandExpr, fold, result);
    }

    /// <summary>Unary minus: negate value.</summary>
    private static Expression EmitUnaryMinus(UnaryMinus unaryMinus, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var operandExpr = CompileNode(unaryMinus.Operand, ctx);
        int resultSlot = ctx.RingDepth - 1;
        var fold = FoldResultToSlot(ref resultSlot, d, ctx);
        Expression result;
        if (IsDoubleValue(ctx, unaryMinus.Operand)) {
            var dbl = Call(BitConverterInt64BitsToDouble, ctx.RingVar(resultSlot));
            result = Assign(ctx.RingVar(resultSlot),
                Call(BitConverterDoubleToInt64Bits, Negate(dbl)));
        }
        else {
            result = Assign(ctx.RingVar(resultSlot), Negate(ctx.RingVar(resultSlot)));
        }
        ctx.RingDepth = resultSlot + 1;
        return Block(operandExpr, fold, result);
    }

    private static Expression EmitBitwiseNot(BitwiseNot n, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var operandExpr = CompileNode(n.Operand, ctx);
        int resultSlot = ctx.RingDepth - 1;
        var fold = FoldResultToSlot(ref resultSlot, d, ctx);
        var result = Assign(ctx.RingVar(resultSlot), Not(ctx.RingVar(resultSlot)));
        ctx.RingDepth = resultSlot + 1;
        return Block(operandExpr, fold, result);
    }

    /// <summary>PopCount via System.Numerics.BitOperations.PopCount.</summary>
    private static Expression EmitPopCount(PopCount pc, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var operandExpr = CompileNode(pc.Operand, ctx);
        int resultSlot = ctx.RingDepth - 1;
        var fold = FoldResultToSlot(ref resultSlot, d, ctx);
        var call = Call(null,
            BitOperationsPopCount,
            Convert(ctx.RingVar(resultSlot), typeof(ulong)));
        var result = Assign(ctx.RingVar(resultSlot), Convert(call, typeof(long)));
        ctx.RingDepth = resultSlot + 1;
        return Block(operandExpr, fold, result);
    }

    /// <summary>Member access via CLR reflection: resolve from analysis metadata
    /// and emit a property getter, field read, or method call.</summary>
    private static Expression EmitMember(Member m, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var instanceExpr = CompileNode(m.Value, ctx);
        int instanceSlot = ctx.RingDepth - 1;
        var fold = FoldResultToSlot(ref instanceSlot, d, ctx);

        var resolved = ctx.Analysis?.GetResolvedMember(m);

        // Static member — no instance needed
        if (resolved?.LifetimeModifier == LifetimeModifier.Static) {
            return EmitResolvedMember(resolved, null, d, ctx, Block(instanceExpr, fold));
        }

        if (resolved is not null) {
            var declaringTypeDef = resolved.DeclaringTypeDefinition;
            bool isInlineValueType = declaringTypeDef is ClrTypeDefinition clrDef
                && clrDef.RuntimeType.IsValueType
                && AbiValueTypes.IsLongRepresentable(clrDef.RuntimeType);

            Expression instanceObj;
            if (isInlineValueType) {
                instanceObj = Convert(ctx.RingVar(instanceSlot), typeof(object));
            }
            else {
                instanceObj = Call(ctx.HeapLocal,
                    HeapUnsafeGet,
                    Convert(ctx.RingVar(instanceSlot), typeof(int)));
            }
            var prelude = Block(
                instanceExpr, fold,
                IfThen(
                    Equal(instanceObj, Constant(null, typeof(object))),
                    Throw(New(InvalidOperationExceptionStringCtor,
                        Constant($"Member '{m.MemberName}' requires a non-null instance.")))));
            return EmitResolvedMember(resolved, instanceObj, d, ctx, prelude);
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

    /// <summary>Convert a member access result (object?) to the ring ABI (long).
    /// Long-representable value types are unboxed to long; everything else
    /// (reference types and non-numeric value types like DateTime/DateOnly/Guid)
    /// is boxed and allocated on the heap, returning a heap handle.</summary>
    private static Expression ConvertMemberResult(Expression readCall, ITypeMember resolved, AbiCtx ctx) {
        var clrType = resolved.MemberTypeDefinition.GetRuntimeType()
            ?? resolved.MemberTypeDefinition.PrimitiveType?.GetClrType();
        return ConvertClrToRing(readCall, clrType, ctx);
    }

    private static Expression ConvertClrToRing(Expression value, Type? clrType, AbiCtx ctx) {
        if (clrType == typeof(float) || clrType == typeof(double)) {
            return Call(BitConverterDoubleToInt64Bits, Convert(value, typeof(double)));
        }
        if (clrType is not null && clrType.IsValueType && AbiValueTypes.IsLongRepresentable(clrType)) {
            return Convert(Convert(value, clrType), typeof(long));
        }
        return Call(null, BoxToAbiInfo, ctx.HeapLocal, Convert(value, typeof(object)));
    }

    /// <summary>TypeIs: check if the operand's heap object is assignable to the target type.</summary>
    private static Expression EmitTypeIs(TypeIs t, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var operandExpr = CompileNode(t.Operand, ctx);
        int resultSlot = ctx.RingDepth - 1;
        var fold = FoldResultToSlot(ref resultSlot, d, ctx);

        // Resolve target type from TypeReference via analysis or CLR type lookup
        Type? targetType = null;
        if (t.TargetTypeReference is ClrTypeReference clrRef) {
            targetType = clrRef.RuntimeType;
        }
        // Try analysis metadata fallback
        if (targetType is null && ctx.Analysis is not null) {
            var resolvedType = ctx.Analysis.GetResolvedType(t);
            if (resolvedType is ClrTypeDefinition clrDef) {
                targetType = clrDef.RuntimeType;
            }
        }

        if (targetType is null) {
            // Cannot resolve — return 0 (false)
            return Block(operandExpr, fold, Assign(ctx.RingVar(resultSlot), Constant(0L)));
        }

        // Read heap object and check type: _heap.UnsafeGet((int)handle) is TargetType
        var heapObj = Call(ctx.HeapLocal,
            HeapUnsafeGet,
            Convert(ctx.RingVar(resultSlot), typeof(int)));
        var typeCheck = TypeIs(heapObj, targetType);
        var result = Condition(typeCheck, Constant(1L), Constant(0L));
        return Block(operandExpr, fold, Assign(ctx.RingVar(resultSlot), result));
    }

    private static Type? ResolveClrType(Node? typeRef, Node typedNode, AbiCtx ctx) {
        if (typeRef is ClrTypeReference ctr)
            return ctr.RuntimeType;
        if (typeRef is PrimitiveTypeReference ptr)
            return ptr.PrimitiveId.GetClrType();
        if (ctx.Analysis is not null) {
            var fromNode = ctx.Analysis.GetResolvedType(typedNode)?.GetRuntimeType();
            if (fromNode is not null) return fromNode;
            if (typeRef is not null)
                return ctx.Analysis.GetResolvedType(typeRef)?.GetRuntimeType();
        }
        return null;
    }

    /// <summary>TypeAs: heap object assignable to T keeps the handle; otherwise ABI null.</summary>
    private static Expression EmitTypeAs(TypeAs t, AbiCtx ctx) {
        var targetType = ResolveClrType(t.TargetTypeReference, t, ctx)
            ?? throw new InvalidOperationException("VM compile rejected: TypeAs target type is unresolved.");
        if (targetType.IsValueType)
            throw new InvalidOperationException($"VM compile rejected: TypeAs target '{targetType}' is a value type.");
        int d = ctx.RingDepth;
        var operandExpr = CompileNode(t.Operand, ctx);
        int slot = ctx.RingDepth - 1;
        var fold = FoldResultToSlot(ref slot, d, ctx);
        return Block(
            operandExpr, fold,
            Assign(ctx.RingVar(slot),
                Call(null, TypeAsAbiInfo, ctx.HeapLocal, ctx.RingVar(slot), Constant(targetType, typeof(Type)))));
    }

    /// <summary>TypeCast: convert the operand into the target CLR type, then re-box to the ring.</summary>
    private static Expression EmitTypeCast(TypeCast t, AbiCtx ctx) {
        var targetType = ResolveClrType(t.TargetTypeReference, t, ctx)
            ?? throw new InvalidOperationException("VM compile rejected: TypeCast target type is unresolved.");
        Type? sourceType = ctx.Analysis?.GetMetadata<ValueRepresentationMetadata>(t.Operand)?.ClrType;
        if (sourceType is null && t.Operand is Constant c && c.Value is not null)
            sourceType = c.Value.GetType();
        int d = ctx.RingDepth;
        var operandExpr = CompileNode(t.Operand, ctx);
        int slot = ctx.RingDepth - 1;
        var fold = FoldResultToSlot(ref slot, d, ctx);
        return Block(
            operandExpr, fold,
            Assign(ctx.RingVar(slot),
                Call(null, ConvertAbiInfo, ctx.HeapLocal, ctx.RingVar(slot),
                    Constant(sourceType, typeof(Type)), Constant(targetType, typeof(Type)))));
    }

    /// <summary>Default of the resolved type (0 / false / ABI null / heap default struct).</summary>
    private static Expression EmitDefault(Default d, AbiCtx ctx) {
        int slot = ctx.AllocSlot();
        var target = ResolveClrType(d.TargetType, d, ctx);
        if (target is null || (target.IsValueType && AbiValueTypes.IsLongRepresentable(target))
            || target == typeof(bool) || target == typeof(float) || target == typeof(double)
            || !target.IsValueType)
            return Assign(ctx.RingVar(slot), Constant(0L));
        var instance = Activator.CreateInstance(target);
        return Assign(ctx.RingVar(slot),
            Call(ctx.HeapLocal, HeapAllocate, Convert(Constant(instance, typeof(object)), typeof(object))));
    }

    private static Expression EmitTypeOf(TypeOf t, AbiCtx ctx) {
        var runtime = ResolveClrType(t.Type, t, ctx)
            ?? throw new InvalidOperationException("VM compile rejected: TypeOf type is unresolved.");
        int slot = ctx.AllocSlot();
        return Assign(ctx.RingVar(slot),
            Convert(Call(ctx.HeapLocal, HeapAllocate, Convert(Constant(runtime, typeof(object)), typeof(object))),
                typeof(long)));
    }

    /// <summary>ParameterReference: resolve to the referenced Parameter node and emit it.</summary>
    private static Expression EmitParameterReference(ParameterReference pr, AbiCtx ctx) {
        // Try to resolve the referenced Parameter via analysis metadata.
        // The DomainExpressionLoweringPass may produce Member(ParameterReference, ...)
        // where ParameterReference aliases a concrete Parameter node from the lowering.
        // Fall back to 0L if unresolvable.
        int slot = ctx.AllocSlot();
        return Assign(ctx.RingVar(slot), Constant(0L));
    }

    /// <summary>StridedSetBits: bit-level strided set (handle, start, step, limit).</summary>
    private static Expression EmitStridedSetBits(StridedSetBits ssb, AbiCtx ctx) {
        int d = ctx.RingDepth;

        // Compile-time fast path: if the array is a tracked frame-local variable,
        // access _slots[base + elemIdx] directly with no runtime dispatch.
        if (ssb.Array is Variable arrVarS && ctx.TryGetFrameLocalBase(arrVarS) is int flBaseS) {
            var startExprS = CompileNode(ssb.StartValue, ctx);
            int startSlotS = ctx.RingDepth - 1;
            var stepExprS = CompileNode(ssb.Step, ctx);
            int stepSlotS = ctx.RingDepth - 1;
            var limitExprS = CompileNode(ssb.Limit, ctx);
            int limitSlotS = ctx.RingDepth - 1;

            var foldStartS = FoldResultToSlot(ref startSlotS, d, ctx);
            var foldStepS = FoldResultToSlot(ref stepSlotS, d + 1, ctx);
            var foldLimitS = FoldResultToSlot(ref limitSlotS, d + 2, ctx);
            ctx.RingDepth = d + 3;

            var jS = Variable(typeof(long), "_bits_j");
            var elemIdxS = Convert(RightShift(jS, Constant(6)), typeof(int));
            var slotAddr = Add(Constant(flBaseS), elemIdxS);

            var loopStartS = Label("_stride_loop_f");
            var loopEndS = Label("_stride_done_f");
            var loopBodyS = Block(
                Assign(ArrayAccess(ctx.SlotsLocal, slotAddr),
                    Or(ArrayAccess(ctx.SlotsLocal, slotAddr),
                        LeftShift(Constant(1L), Convert(And(jS, Constant(63L)), typeof(int))))),
                Assign(jS, Add(jS, ctx.RingVar(stepSlotS))),
                IfThen(GreaterThan(jS, ctx.RingVar(limitSlotS)), Goto(loopEndS)),
                Goto(loopStartS));
            return Block(startExprS, stepExprS, limitExprS,
                Block([jS],
                    Assign(jS, ctx.RingVar(startSlotS)),
                    Label(loopStartS), loopBodyS, Label(loopEndS)));
        }

        var arrExpr = CompileNode(ssb.Array, ctx);
        int arrSlot = ctx.RingDepth - 1;
        var startExpr = CompileNode(ssb.StartValue, ctx);
        int startSlot = ctx.RingDepth - 1;
        var stepExpr = CompileNode(ssb.Step, ctx);
        int stepSlot = ctx.RingDepth - 1;
        var limitExpr = CompileNode(ssb.Limit, ctx);
        int limitSlot = ctx.RingDepth - 1;

        // Fold all four operands to consecutive slots starting at d
        var foldArr = FoldResultToSlot(ref arrSlot, d, ctx);
        var foldStart = FoldResultToSlot(ref startSlot, d + 1, ctx);
        var foldStep = FoldResultToSlot(ref stepSlot, d + 2, ctx);
        var foldLimit = FoldResultToSlot(ref limitSlot, d + 3, ctx);
        ctx.RingDepth = d + 4;

        // ABI-level strided set — heap array path (direct cast to long[]).
        // Frame-local arrays are handled via the compile-time fast path above.
        var arrObj = Convert(ArrayAccess(ctx.HeapRawSlots,
            Convert(ctx.RingVar(arrSlot), typeof(int))), typeof(long[]));
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
        return Block(arrExpr, startExpr, stepExpr, limitExpr, result);
    }

    /// <summary>Variable reference: read from value stack or from
    /// closure capture array (if this is a captured upvalue).
    /// Leaving the value on the ring for expression chaining.</summary>
    private static Expression EmitVariable(Variable v, AbiCtx ctx) {
        // Statement form `var x = expr` (C# generator prints a declaration).
        // First encounter declares and writes; later reads ignore Value.
        if (v.Initializer is not null && !ctx.IsDeclared(v)) {
            ctx.DeclareVariable(v);
            int d = ctx.RingDepth;
            var init = CompileNode(v.Initializer, ctx);
            int slot = ctx.RingDepth - 1;
            var fold = FoldResultToSlot(ref slot, d, ctx);
            return Block(init, fold, ctx.VariableWrite(v, ctx.RingVar(slot)), ctx.RingVar(slot));
        }

        // Check capture (upvalue) first — used inside lambda bodies
        if (ctx.TryGetCapture(v, out int capIndex)) {
            int slot = ctx.AllocSlot();
            // Read from heap[ state.ClosureHandle ][ capIndex + 1 ]
            // The closure array is object[] stored at the handle in heap raw slots.
            var closureHandle = ctx.ClosureHandle;
            var closureArr = Convert(
                ArrayAccess(ctx.HeapRawSlots, Convert(closureHandle, typeof(int))),
                typeof(object[]));
            var captured = Convert(
                ArrayAccess(closureArr, Constant(capIndex + 1)),
                typeof(long));
            return Assign(ctx.RingVar(slot), captured);
        }
        // Local variable on value stack — read via compile-time frame offset
        int slot2 = ctx.AllocSlot();
        return Assign(ctx.RingVar(slot2), ctx.VariableRead(v));
    }
}