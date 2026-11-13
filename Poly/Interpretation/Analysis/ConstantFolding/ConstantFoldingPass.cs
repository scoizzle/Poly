using System.Globalization;
using System.Numerics;
using System.Reflection;

using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation.Analysis.ConstantFolding;

/// <summary>
/// Metadata indicating a node's constant-folded value.
/// </summary>
public sealed record ConstantValueMetadata(object? Value) : IAnalysisMetadata;

/// <summary>
/// Performs constant folding optimization by evaluating constant expressions at analysis time.
/// This pass identifies nodes that can be computed at compile time and stores their values.
/// </summary>
public sealed class ConstantFoldingPass : INodeAnalyzer {
    public const string Id = "ConstantFolding";
    public string PassName => Id;
    public void Analyze(AnalysisContext context, Node node) {
        if (node is Constant) {
            return;
        }

        // `nameof(arg)` is the last identifier of `arg`. Fold it here — before the
        // compatibility pass rejects the TypeReference callee — and publish it as a
        // constant so a parent literal `Add` folds in this same pass.
        if (node is Invoke { Delegate: TypeReference { TypeName: "nameof" } } nameOfInvoke) {
            if (nameOfInvoke.Arguments.Length == 1
                && NameOfArgument(context, nameOfInvoke.Arguments[0]) is { } name) {
                context.SetMetadata(node, new ConstantValueMetadata(name));
                context.SetNodeReplacement(node, TypeMatchReplacement(node, new Constant(name), context));
            }
            else {
                context.ReportError(node, "nameof(...) requires a single name argument.");
            }
            return;
        }

        // Post-order traversal: analyze children first, then fold parent
        this.AnalyzeChildren(context, node);

        // Try to fold this node if all operands are constants
        var foldedValue = TryFold(context, node);
        if (foldedValue.HasValue) {
            context.SetMetadata(node, new ConstantValueMetadata(foldedValue.Value));
            var foldedNode = TypeMatchReplacement(node, new Constant(foldedValue.Value), context);
            context.SetNodeReplacement(node, foldedNode);
            return;
        }

        var simplified = TrySimplify(context, node);
        if (simplified != null) {
            var typed = TypeMatchReplacement(node, simplified, context);
            context.SetNodeReplacement(node, typed);
        }
    }

    /// <summary>The name a <c>nameof(arg)</c> yields: the last identifier of the argument,
    /// following the argument's replacement (a dotted CLR type name is rewritten).</summary>
    private static string? NameOfArgument(AnalysisContext context, Node argument) {
        var effective = context.GetNodeReplacement(argument) ?? argument;
        return effective switch {
            Variable v => v.Name,
            Parameter p => p.Name,
            Member m => m.MemberName,
            ClrTypeReference c => c.RuntimeType.Name,
            NamedTypeReference n => LastNameSegment(n.FullName),
            TypeReference t => LastNameSegment(t.TypeName),
            PrimitiveTypeReference prim => prim.PrimitiveId.GetCSharpKeyword(),
            _ => null
        };
    }

    private static string LastNameSegment(string name) {
        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name;
    }

    private FoldResult TryFold(AnalysisContext context, Node node, IReadOnlyDictionary<NodeId, object?>? parameterValues = null) {
        return node switch {
            Constant c => FoldResult.Success(c.Value),
            Parameter parameter => FoldParameter(parameter, parameterValues),

            // Operators: the type provider states which apply and what they mean (GetOperator).
            Add add => FoldOperator(context, OperatorKind.Add, add.LeftHandValue, add.RightHandValue, parameterValues),
            Subtract sub => FoldOperator(context, OperatorKind.Subtract, sub.LeftHandValue, sub.RightHandValue, parameterValues),
            Multiply mul => FoldOperator(context, OperatorKind.Multiply, mul.LeftHandValue, mul.RightHandValue, parameterValues),
            Divide div => FoldOperator(context, OperatorKind.Divide, div.LeftHandValue, div.RightHandValue, parameterValues),
            Modulo mod => FoldOperator(context, OperatorKind.Modulo, mod.LeftHandValue, mod.RightHandValue, parameterValues),
            UnaryMinus neg => FoldOperator(context, OperatorKind.UnaryMinus, neg.Operand, null, parameterValues),

            // Boolean operations
            And and => FoldAnd(context, and, parameterValues),
            Or or => FoldOr(context, or, parameterValues),
            Not not => FoldUnaryBoolean(context, not.Value, a => !a, parameterValues),

            // Comparison and equality operations
            GreaterThan gt => FoldOperator(context, OperatorKind.GreaterThan, gt.LeftHandValue, gt.RightHandValue, parameterValues),
            GreaterThanOrEqual gte => FoldOperator(context, OperatorKind.GreaterThanOrEqual, gte.LeftHandValue, gte.RightHandValue, parameterValues),
            LessThan lt => FoldOperator(context, OperatorKind.LessThan, lt.LeftHandValue, lt.RightHandValue, parameterValues),
            LessThanOrEqual lte => FoldOperator(context, OperatorKind.LessThanOrEqual, lte.LeftHandValue, lte.RightHandValue, parameterValues),
            Equal eq => FoldOperator(context, OperatorKind.Equal, eq.LeftHandValue, eq.RightHandValue, parameterValues),
            NotEqual neq => FoldOperator(context, OperatorKind.NotEqual, neq.LeftHandValue, neq.RightHandValue, parameterValues),

            // Conditional with constant condition
            Conditional cond => FoldConditional(context, cond, parameterValues),
            IfStatement ifStmt => FoldIfStatement(context, ifStmt, parameterValues),

            // Coalesce with non-null left
            Coalesce coalesce => FoldCoalesce(context, coalesce, parameterValues),

            // Bitwise operations
            BitwiseAnd ba => FoldOperator(context, OperatorKind.BitwiseAnd, ba.LeftHandValue, ba.RightHandValue, parameterValues),
            BitwiseOr bor => FoldOperator(context, OperatorKind.BitwiseOr, bor.LeftHandValue, bor.RightHandValue, parameterValues),
            BitwiseXor bx => FoldOperator(context, OperatorKind.BitwiseXor, bx.LeftHandValue, bx.RightHandValue, parameterValues),
            ShiftLeft sl => FoldOperator(context, OperatorKind.ShiftLeft, sl.LeftHandValue, sl.RightHandValue, parameterValues),
            ShiftRight sr => FoldOperator(context, OperatorKind.ShiftRight, sr.LeftHandValue, sr.RightHandValue, parameterValues),
            BitwiseNot bn => FoldOperator(context, OperatorKind.BitwiseNot, bn.Operand, null, parameterValues),

            // Lambda invocation with constant arguments
            Invoke invoke => FoldInvocation(context, invoke, parameterValues),

            _ => FoldResult.NotFoldable
        };
    }

    private static FoldResult FoldParameter(Parameter parameter, IReadOnlyDictionary<NodeId, object?>? parameterValues) {
        if (parameterValues != null && parameterValues.TryGetValue(parameter.Id, out var value)) {
            return FoldResult.Success(value);
        }

        return FoldResult.NotFoldable;
    }

    private Node? TrySimplify(AnalysisContext context, Node node) {
        return node switch {
            Add add => SimplifyAddition(context, add),
            Subtract sub => SimplifySubtraction(context, sub),
            Multiply mul => SimplifyMultiplication(context, mul),
            Divide div => SimplifyDivision(context, div),
            Modulo mod => SimplifyModulo(context, mod),
            UnaryMinus neg => SimplifyUnaryMinus(context, neg),
            BitwiseNot bn => SimplifyBitwiseNot(context, bn),
            Not n => SimplifyNot(context, n),
            BitwiseAnd ba => SimplifyBitwiseAnd(context, ba),
            BitwiseOr bo => SimplifyBitwiseOr(context, bo),
            BitwiseXor bx => SimplifyBitwiseXor(context, bx),
            And and => SimplifyAnd(context, and),
            Or or => SimplifyOr(context, or),
            _ => null
        };
    }

    /// <summary>If the replacement node's type differs from the original
    /// node's resolved type, wrap in a <see cref="TypeCast"/> to preserve
    /// the original type, and copy the resolved type onto the cast so
    /// downstream passes see consistent types.</summary>
    private static Node TypeMatchReplacement(Node original, Node replacement, AnalysisContext context) {
        // A lowered non-constant node (shift, mask) performs the same operation as
        // the original, so it carries the original's resolved type and emit can
        // classify it from the type alone.
        if (replacement is not Constant) {
            // Identity folds return an operand that is already in the tree; stamping
            // the operation's type onto it would relabel a real value (0 + date).
            if (context.GetResolvedType(original) is { } originalType
                && !IsWithin(original, replacement))
                context.SetResolvedType(replacement, originalType);
            return replacement;
        }
        var c = (Constant)replacement;
        if (c.Value is null)
            return replacement;

        var resolvedType = context.GetResolvedType(original);
        if (resolvedType is null)
            return replacement;

        if (context.TypeDefinitions.GetTypeDefinition(c.Value.GetType()) is { } valueType
            && TypeDefinitionExtensions.IsSameType(valueType, resolvedType))
            return replacement;

        // Type mismatch — wrap in a cast to preserve the original type,
        // and propagate the resolved type so backends can lower it.
        var cast = new TypeCast(replacement, new TypeReference(resolvedType.FullName));
        context.SetResolvedType(cast, resolvedType);
        return cast;
    }

    /// <summary>True when <paramref name="candidate"/> is the original node or one of
    /// its descendants, i.e. an operand the fold kept rather than a new node.</summary>
    private static bool IsWithin(Node root, Node candidate) {
        if (ReferenceEquals(root, candidate))
            return true;
        foreach (var child in root.Children) {
            if (child is not null && IsWithin(child, candidate))
                return true;
        }
        return false;
    }

    private static bool IsPowerOf2(long n) => n > 0 && (n & (n - 1)) == 0;

    private FoldResult FoldBinaryBoolean(AnalysisContext context, FoldResult leftValue, Node right, Func<bool, bool, bool> operation, IReadOnlyDictionary<NodeId, object?>? parameterValues = null) {
        var rightValue = GetConstantValue(context, right, parameterValues);

        if (!leftValue.HasValue || !rightValue.HasValue)
            return FoldResult.NotFoldable;

        if (leftValue.Value is bool leftBool && rightValue.Value is bool rightBool) {
            return FoldResult.Success(operation(leftBool, rightBool));
        }

        return FoldResult.NotFoldable;
    }

    private FoldResult FoldAnd(AnalysisContext context, And and, IReadOnlyDictionary<NodeId, object?>? parameterValues = null) {
        var leftValue = GetConstantValue(context, and.LeftHandValue, parameterValues);
        if (leftValue.HasValue && leftValue.Value is bool leftBool && !leftBool && RightCanBeBoolean(context, and.RightHandValue, parameterValues)) {
            return FoldResult.Success(false);
        }

        return FoldBinaryBoolean(context, leftValue, and.RightHandValue, (a, b) => a && b, parameterValues);
    }

    private FoldResult FoldOr(AnalysisContext context, Or or, IReadOnlyDictionary<NodeId, object?>? parameterValues = null) {
        var leftValue = GetConstantValue(context, or.LeftHandValue, parameterValues);
        if (leftValue.HasValue && leftValue.Value is bool leftBool && leftBool && RightCanBeBoolean(context, or.RightHandValue, parameterValues)) {
            return FoldResult.Success(true);
        }

        return FoldBinaryBoolean(context, leftValue, or.RightHandValue, (a, b) => a || b, parameterValues);
    }

    /// <summary>
    /// A short-circuit answers from its left operand only when its right operand can be a Boolean: a constant right
    /// operand holds a <c>bool</c>, any other has no resolved type or a Boolean one. Otherwise the node stays for the
    /// compatibility pass, which rejects <c>false and 5</c> as C# does.
    /// </summary>
    private bool RightCanBeBoolean(AnalysisContext context, Node right, IReadOnlyDictionary<NodeId, object?>? parameterValues = null) {
        var value = GetConstantValue(context, right, parameterValues);
        if (value.HasValue)
            return value.Value is bool;
        return context.GetResolvedType(right) is not { } type
            || (type.TryGetRuntimeType(out var clr) && clr == typeof(bool));
    }

    private FoldResult FoldUnaryBoolean(AnalysisContext context, Node operand, Func<bool, bool> operation, IReadOnlyDictionary<NodeId, object?>? parameterValues = null) {
        var value = GetConstantValue(context, operand, parameterValues);
        if (!value.HasValue)
            return FoldResult.NotFoldable;

        if (value.Value is bool boolValue) {
            return FoldResult.Success(operation(boolValue));
        }

        return FoldResult.NotFoldable;
    }

    private Node? SimplifyAddition(AnalysisContext context, Add add) {
        if (IsZero(context, add.LeftHandValue)) {
            return add.RightHandValue;
        }

        if (IsZero(context, add.RightHandValue)) {
            return add.LeftHandValue;
        }

        return null;
    }

    private Node? SimplifySubtraction(AnalysisContext context, Subtract subtract) {
        return IsZero(context, subtract.RightHandValue) ? subtract.LeftHandValue : null;
    }

    private Node? SimplifyMultiplication(AnalysisContext context, Multiply multiply) {
        if (IsOne(context, multiply.LeftHandValue))
            return multiply.RightHandValue;
        if (IsOne(context, multiply.RightHandValue))
            return multiply.LeftHandValue;
        if (IsZero(context, multiply.LeftHandValue) || IsZero(context, multiply.RightHandValue))
            return new Constant(0L);
        // x * 2^n → shift left by n (integer operands only)
        if (IsIntegerOperand(context, multiply.LeftHandValue)) {
            var mulRightVal = GetConstantValue(context, multiply.RightHandValue);
            if (mulRightVal.HasValue && ToLong(mulRightVal.Value) is long mr && IsPowerOf2(mr))
                return new ShiftLeft(multiply.LeftHandValue, new Constant((int)double.Log2(mr)));
        }
        return null;
    }

    private Node? SimplifyDivision(AnalysisContext context, Divide divide) {
        if (IsOne(context, divide.RightHandValue))
            return divide.LeftHandValue;
        // x / 2^n → shift right by n. Only for an unsigned dividend: a signed x >> n floors toward
        // negative infinity while x / 2^n truncates toward zero, so -7 / 2 is -3 but -7 >> 1 is -4.
        if (IsUnsignedOperand(context, divide.LeftHandValue)) {
            var divRightVal = GetConstantValue(context, divide.RightHandValue);
            if (divRightVal.HasValue && ToLong(divRightVal.Value) is long dr && IsPowerOf2(dr))
                return new ShiftRight(divide.LeftHandValue, new Constant((int)double.Log2(dr)));
        }
        return null;
    }

    private Node? SimplifyModulo(AnalysisContext context, Modulo mod) {
        // x % 2^n → bitwise and with (2^n - 1); the mask must match the
        // left operand's unsigned type so the emitted And is type-correct.
        // Unsigned only, for the same reason as division: -7 % 2 is -1, and -7 & 1 is 1.
        if (!IsUnsignedOperand(context, mod.LeftHandValue))
            return null;

        var modRightVal = GetConstantValue(context, mod.RightHandValue);
        if (!modRightVal.HasValue || ToLong(modRightVal.Value) is not long mr || !IsPowerOf2(mr))
            return null;

        var mask = BuildMaskConstant(context, mod.LeftHandValue, mr - 1);
        return mask is null ? null : new BitwiseAnd(mod.LeftHandValue, mask);
    }

    private static bool IsIntegerOperand(AnalysisContext context, Node node) =>
        context.GetResolvedType(node)?.PrimitiveType is { } primitive && primitive.IsInteger();

    /// <summary>
    /// True only for an unsigned integral operand. <c>x &gt;&gt; n</c> and <c>x &amp; (2^n - 1)</c> equal
    /// <c>x / 2^n</c> and <c>x % 2^n</c> for an unsigned <c>x</c>. A signed <c>x</c> does not qualify:
    /// C# division and remainder truncate toward zero, while a shift and a mask work on the two's
    /// complement bits, so the two disagree for every negative dividend.
    /// </summary>
    private static bool IsUnsignedOperand(AnalysisContext context, Node node) =>
        context.GetResolvedType(node)?.PrimitiveType is
            PrimitiveType.UInt8 or PrimitiveType.UInt16 or PrimitiveType.UInt32 or PrimitiveType.UInt64;

    private static Constant? BuildMaskConstant(AnalysisContext context, Node left, long mask) {
        object? value = context.GetResolvedType(left)?.PrimitiveType switch {
            PrimitiveType.UInt8 => (byte)mask,
            PrimitiveType.UInt16 => (ushort)mask,
            PrimitiveType.UInt32 => (uint)mask,
            PrimitiveType.UInt64 => (ulong)mask,
            _ => (object?)null
        };
        return value is null ? null : new Constant(value);
    }

    private Node? SimplifyUnaryMinus(AnalysisContext context, UnaryMinus neg) {
        // -(-x) → x
        return neg.Operand is UnaryMinus inner ? inner.Operand : null;
    }

    private Node? SimplifyBitwiseNot(AnalysisContext context, BitwiseNot bn) {
        // ~~x → x
        return bn.Operand is BitwiseNot inner ? inner.Operand : null;
    }

    private Node? SimplifyNot(AnalysisContext context, Not n) {
        // !!x → x
        return n.Value is Not inner ? inner.Value : null;
    }

    private Node? SimplifyBitwiseAnd(AnalysisContext context, BitwiseAnd ba) {
        if (IsZero(context, ba.RightHandValue))
            return new Constant(0L);
        var baVal = GetConstantValue(context, ba.RightHandValue);
        if (baVal.HasValue && ToLong(baVal.Value) == -1)
            return ba.LeftHandValue;
        return null;
    }

    private Node? SimplifyBitwiseOr(AnalysisContext context, BitwiseOr bo) {
        if (IsZero(context, bo.RightHandValue))
            return bo.LeftHandValue;
        var boVal = GetConstantValue(context, bo.RightHandValue);
        if (boVal.HasValue && ToLong(boVal.Value) == -1)
            return new Constant(-1L);
        return null;
    }

    private Node? SimplifyBitwiseXor(AnalysisContext context, BitwiseXor bx) {
        if (IsZero(context, bx.RightHandValue))
            return bx.LeftHandValue;
        return null;
    }

    private static long? ToLong(object? value) => value switch {
        int i => i,
        long l => l,
        short s => s,
        byte b => b,
        uint u => u,
        _ => null
    };

    private Node? SimplifyAnd(AnalysisContext context, And and) {
        var leftValue = GetConstantValue(context, and.LeftHandValue);
        if (!leftValue.HasValue || leftValue.Value is not bool leftBool || !RightCanBeBoolean(context, and.RightHandValue)) {
            return null;
        }

        return leftBool ? and.RightHandValue : and.LeftHandValue;
    }

    private Node? SimplifyOr(AnalysisContext context, Or or) {
        var leftValue = GetConstantValue(context, or.LeftHandValue);
        if (!leftValue.HasValue || leftValue.Value is not bool leftBool || !RightCanBeBoolean(context, or.RightHandValue)) {
            return null;
        }

        return leftBool ? or.LeftHandValue : or.RightHandValue;
    }

    private FoldResult FoldConditional(AnalysisContext context, Conditional cond, IReadOnlyDictionary<NodeId, object?>? parameterValues = null) {
        var condValue = GetConstantValue(context, cond.Condition, parameterValues);
        if (!condValue.HasValue || condValue.Value is not bool boolCond)
            return FoldResult.NotFoldable;

        // If condition is constant, result is the appropriate branch
        var selectedBranch = boolCond ? cond.IfTrue : cond.IfFalse;
        var branchValue = GetConstantValue(context, selectedBranch, parameterValues);
        return branchValue.HasValue ? FoldResult.Success(branchValue.Value) : FoldResult.NotFoldable;
    }

    private FoldResult FoldIfStatement(AnalysisContext context, IfStatement ifStmt, IReadOnlyDictionary<NodeId, object?>? parameterValues = null) {
        var condValue = GetConstantValue(context, ifStmt.Condition, parameterValues);
        if (!condValue.HasValue || condValue.Value is not bool boolCond)
            return FoldResult.NotFoldable;

        // If condition is constant true, result is then branch
        // If condition is constant false, result is else branch (if present)
        if (boolCond) {
            var thenValue = GetConstantValue(context, ifStmt.ThenBranch, parameterValues);
            return thenValue.HasValue ? FoldResult.Success(thenValue.Value) : FoldResult.NotFoldable;
        }
        else if (ifStmt.ElseBranch != null) {
            var elseValue = GetConstantValue(context, ifStmt.ElseBranch, parameterValues);
            return elseValue.HasValue ? FoldResult.Success(elseValue.Value) : FoldResult.NotFoldable;
        }

        return FoldResult.NotFoldable;
    }

    private FoldResult FoldCoalesce(AnalysisContext context, Coalesce coalesce, IReadOnlyDictionary<NodeId, object?>? parameterValues = null) {
        var leftValue = GetConstantValue(context, coalesce.LeftHandValue, parameterValues);
        if (!leftValue.HasValue)
            return FoldResult.NotFoldable;

        if (leftValue.Value != null) {
            return FoldResult.Success(leftValue.Value);
        }

        // Left is null — result is right
        var rv = GetConstantValue(context, coalesce.RightHandValue, parameterValues);
        return rv.HasValue ? FoldResult.Success(rv.Value) : FoldResult.NotFoldable;
    }

    private FoldResult FoldInvocation(AnalysisContext context, Invoke invoke, IReadOnlyDictionary<NodeId, object?>? parameterValues = null) {
        if (invoke.Delegate is not Lambda lambda || lambda.Parameters.Count != invoke.Arguments.Length) {
            return FoldResult.NotFoldable;
        }

        Dictionary<NodeId, object?> boundParameters = parameterValues != null
            ? new Dictionary<NodeId, object?>(parameterValues)
            : [];

        for (var i = 0; i < lambda.Parameters.Count; i++) {
            var argumentValue = GetConstantValue(context, invoke.Arguments[i], parameterValues);
            if (!argumentValue.HasValue) {
                return FoldResult.NotFoldable;
            }

            boundParameters[lambda.Parameters[i].Id] = argumentValue.Value;
        }

        return GetConstantValue(context, lambda.Body, boundParameters);
    }

    private FoldResult GetConstantValue(AnalysisContext context, Node node, IReadOnlyDictionary<NodeId, object?>? parameterValues = null) {
        if (node is Parameter parameter) {
            return FoldParameter(parameter, parameterValues);
        }

        // Check if we already computed a constant value for this node
        var metadata = context.GetMetadata<ConstantValueMetadata>(node);
        if (metadata != null) {
            return FoldResult.Success(metadata.Value);
        }

        // Check if it's a literal constant
        if (node is Constant c) {
            return FoldResult.Success(c.Value);
        }

        return TryFold(context, node, parameterValues);
    }

    private bool IsZero(AnalysisContext context, Node node) {
        var value = GetConstantValue(context, node);
        return value.HasValue && IsZeroValue(value.Value);
    }

    private static bool IsZeroValue(object? value) => value switch {
        sbyte x => x == 0,
        byte x => x == 0,
        short x => x == 0,
        ushort x => x == 0,
        int x => x == 0,
        uint x => x == 0,
        long x => x == 0,
        ulong x => x == 0,
        float x => x == 0,
        double x => x == 0,
        decimal x => x == 0,
        _ => false
    };

    private bool IsOne(AnalysisContext context, Node node) {
        var value = GetConstantValue(context, node);
        return value.HasValue && value.Value switch {
            sbyte x => x == 1,
            byte x => x == 1,
            short x => x == 1,
            ushort x => x == 1,
            int x => x == 1,
            uint x => x == 1,
            long x => x == 1,
            ulong x => x == 1,
            float x => x == 1,
            double x => x == 1,
            decimal x => x == 1,
            _ => false
        };
    }

    /// <summary>
    /// Folds an operator over constant operands. The type provider decides: <c>GetOperator</c> states whether the
    /// operator applies and which signature it is, each operand converts to that signature's parameter type, and the
    /// operator is applied. A cell the provider states nothing for declines (fail closed), except equality of two values
    /// of one enum type, which keeps <see cref="object.Equals(object?, object?)"/>.
    /// </summary>
    private FoldResult FoldOperator(AnalysisContext context, OperatorKind kind, Node left, Node? right, IReadOnlyDictionary<NodeId, object?>? parameterValues) {
        if (!TryGetOperand(context, left, parameterValues, out var leftValue))
            return FoldResult.NotFoldable;
        object? rightValue = null;
        if (right is not null && !TryGetOperand(context, right, parameterValues, out rightValue))
            return FoldResult.NotFoldable;

        var equality = kind is OperatorKind.Equal or OperatorKind.NotEqual;
        if (leftValue is null || (right is not null && rightValue is null))
            return equality ? Equality(kind, Equals(leftValue, rightValue)) : FoldResult.NotFoldable;

        var leftType = leftValue.GetType();
        var rightType = rightValue?.GetType();
        var handled = IsFoldableCell(kind, leftType, rightType);
        // Two values of one enum type have no operator from the provider; they keep object.Equals.
        var sameTypeEquality = equality && leftType == rightType && leftType.IsEnum;
        if (!handled && !sameTypeEquality)
            return FoldResult.NotFoldable;

        var leftDefinition = context.TypeDefinitions.GetTypeDefinition(leftType);
        var rightDefinition = rightType is null ? null : context.TypeDefinitions.GetTypeDefinition(rightType);
        if (leftDefinition is null || (right is not null && rightDefinition is null))
            return FoldResult.NotFoldable;
        if (leftDefinition.GetOperator(kind, rightDefinition) is not { } resolution)
            return sameTypeEquality ? Equality(kind, Equals(leftValue, rightValue)) : FoldResult.NotFoldable;
        // A declared operator outside the handled cells is never run at analysis time.
        if (!handled)
            return FoldResult.NotFoldable;

        if (kind is OperatorKind.Divide or OperatorKind.Modulo && IsZeroValue(rightValue))
            return FoldResult.NotFoldable;

        return TryEvaluate(kind, resolution, leftValue, rightValue, out var result)
            ? FoldResult.Success(result)
            : FoldResult.NotFoldable;
    }

    private static FoldResult Equality(OperatorKind kind, bool equal) =>
        FoldResult.Success(kind is OperatorKind.Equal ? equal : !equal);

    /// <summary>
    /// An operand's constant value. A constant and a folded child are typed by their value. A parameter is typed by
    /// its stamp: a value of another type, or no stamp, declines.
    /// </summary>
    private bool TryGetOperand(AnalysisContext context, Node node, IReadOnlyDictionary<NodeId, object?>? parameterValues, out object? value) {
        var operand = GetConstantValue(context, node, parameterValues);
        value = operand.HasValue ? operand.Value : null;
        if (!operand.HasValue)
            return false;
        if (node is not Parameter || value is null)
            return true;
        return context.GetResolvedType(node) is { } stamp
            && stamp.TryGetRuntimeType(out var stamped)
            && value.GetType() == stamped;
    }

    private static readonly HashSet<Type> NumericTypes = [
        typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
        typeof(long), typeof(ulong), typeof(char), typeof(float), typeof(double), typeof(decimal)
    ];

    /// <summary>The declaring types whose declared operators may run at analysis time.</summary>
    private static readonly HashSet<Type> DeclaredOperatorTypes = [
        typeof(decimal), typeof(string), typeof(DateTime), typeof(DateOnly), typeof(TimeOnly), typeof(TimeSpan),
        typeof(DateTimeOffset), typeof(Guid)
    ];

    private static readonly HashSet<(Type, Type)> MixedArithmetic = [
        (typeof(int), typeof(long)), (typeof(long), typeof(int)), (typeof(int), typeof(double)), (typeof(double), typeof(int))
    ];

    private static readonly HashSet<Type> SameTypeArithmetic = [typeof(int), typeof(long), typeof(double), typeof(float), typeof(decimal)];

    /// <summary>
    /// The (operator, left type, right type) cells the pass evaluates. Which cells are defined and what they mean is the
    /// provider's answer; this table only bounds where folding runs: the cells that folded before the provider decided
    /// (T-20 keeps them) and the ones T-20 adds by name. A cell outside it declines, so no fold lands in a cell whose VM
    /// value is wrong (issue 832) without a test. Temporal arithmetic and unary minus are not cells here (T-10).
    /// </summary>
    private static bool IsFoldableCell(OperatorKind kind, Type left, Type? right) {
        var arithmetic = right is not null && ((left == right && SameTypeArithmetic.Contains(left)) || MixedArithmetic.Contains((left, right)));
        var decimalWithInteger = (left == typeof(decimal) && right == typeof(int)) || (left == typeof(decimal) && right == typeof(long))
            || (right == typeof(decimal) && left == typeof(int)) || (right == typeof(decimal) && left == typeof(long));
        return kind switch {
            OperatorKind.Equal or OperatorKind.NotEqual or OperatorKind.LessThan or OperatorKind.LessThanOrEqual
                or OperatorKind.GreaterThan or OperatorKind.GreaterThanOrEqual =>
                right is not null && ((NumericTypes.Contains(left) && NumericTypes.Contains(right))
                    || (left == right && (left == typeof(bool) || DeclaredOperatorTypes.Contains(left)))),
            OperatorKind.Add => arithmetic || decimalWithInteger || IsCharAdd(left, right)
                || (left == typeof(string) && right == typeof(string)),
            OperatorKind.Subtract or OperatorKind.Multiply or OperatorKind.Modulo => arithmetic || decimalWithInteger,
            OperatorKind.Divide => arithmetic || decimalWithInteger || (left == typeof(ulong) && right == typeof(ulong)),
            OperatorKind.UnaryMinus => SameTypeArithmetic.Contains(left),
            OperatorKind.BitwiseAnd or OperatorKind.BitwiseOr or OperatorKind.BitwiseXor =>
                left == right && (left == typeof(int) || left == typeof(long) || left == typeof(bool)),
            OperatorKind.ShiftLeft or OperatorKind.ShiftRight => left == typeof(int) && right == typeof(int),
            OperatorKind.BitwiseNot => left == typeof(int) || left == typeof(long),
            _ => false
        };
    }

    private static bool IsCharAdd(Type left, Type? right) =>
        (left == typeof(char) && (right == typeof(char) || right == typeof(int) || right == typeof(long) || right == typeof(double)))
        || (right == typeof(char) && (left == typeof(int) || left == typeof(long) || left == typeof(double)));

    /// <summary>
    /// Applies the chosen operator: each operand converts to its parameter's type, then the platform applies the
    /// operator or its declared method runs. An overflow, a division by zero, a failed conversion or any exception an
    /// invoked method throws (<see cref="TargetInvocationException"/>) declines. The result must be of the operator's
    /// result type.
    /// </summary>
    private static bool TryEvaluate(OperatorKind kind, OperatorResolution resolution, object left, object? right, out object? result) {
        result = null;
        var parameters = resolution.Signature.Parameters.ToArray();
        if (!resolution.Result.TryGetRuntimeType(out var resultType))
            return false;
        try {
            if (!TryConvert(left, parameters[0].ParameterTypeDefinition, out var x))
                return false;
            object? y = null;
            if (parameters.Length > 1 && !TryConvert(right!, parameters[1].ParameterTypeDefinition, out y))
                return false;
            result = resolution.PlatformApplied ? ApplyPlatformOperator(kind, x, y) : InvokeDeclaredOperator(kind, resolution.Signature, x, y);
        }
        catch (Exception ex) when (ex is OverflowException or DivideByZeroException or TargetInvocationException or InvalidCastException) {
            result = null;
            return false;
        }
        return result is not null && result.GetType() == resultType;
    }

    /// <summary>
    /// Converts an operand to a parameter type. <c>GetOperator</c> chose the operator, so the conversion is implicit.
    /// Decimal's conversions are its declared <c>op_Implicit</c> members; the rest are the platform's numeric
    /// conversions, with a char going through its code point.
    /// </summary>
    private static bool TryConvert(object value, ITypeDefinition parameter, out object converted) {
        converted = value;
        if (!parameter.TryGetRuntimeType(out var target))
            return false;
        if (value.GetType() == target)
            return true;
        if (target == typeof(decimal)) {
            var implicitConversion = typeof(decimal).GetMethod("op_Implicit", BindingFlags.Public | BindingFlags.Static, [value.GetType()]);
            if (implicitConversion is null || implicitConversion.Invoke(null, [value]) is not { } widened)
                return false;
            converted = widened;
            return true;
        }
        if (!NumericTypes.Contains(target))
            return false;
        converted = Convert.ChangeType(value is char c ? (int)c : value, target, CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>An operator the platform applies itself, over operands already of its parameter types.</summary>
    private static object? ApplyPlatformOperator(OperatorKind kind, object x, object? y) => x switch {
        bool a => kind switch {
            OperatorKind.Equal => a == (bool)y!,
            OperatorKind.NotEqual => a != (bool)y!,
            OperatorKind.BitwiseAnd => a & (bool)y!,
            OperatorKind.BitwiseOr => a | (bool)y!,
            OperatorKind.BitwiseXor => a ^ (bool)y!,
            _ => null
        },
        string a => kind is OperatorKind.Add ? string.Concat(a, (string)y!) : null,
        int a => ApplyInteger(kind, a, y),
        uint a => ApplyInteger(kind, a, y),
        long a => ApplyInteger(kind, a, y),
        ulong a => ApplyInteger(kind, a, y),
        float a => ApplyNumber(kind, a, y),
        double a => ApplyNumber(kind, a, y),
        _ => null
    };

    private static object? ApplyInteger<T>(OperatorKind kind, T a, object? y) where T : IBinaryInteger<T> => kind switch {
        OperatorKind.ShiftLeft => unchecked(a << (int)y!),
        OperatorKind.ShiftRight => unchecked(a >> (int)y!),
        OperatorKind.BitwiseAnd => a & (T)y!,
        OperatorKind.BitwiseOr => a | (T)y!,
        OperatorKind.BitwiseXor => a ^ (T)y!,
        OperatorKind.BitwiseNot => ~a,
        _ => ApplyNumber(kind, a, y)
    };

    private static object? ApplyNumber<T>(OperatorKind kind, T a, object? y) where T : INumber<T> {
        if (kind is OperatorKind.UnaryMinus)
            return unchecked(-a);
        var b = (T)y!;
        return kind switch {
            OperatorKind.Add => unchecked(a + b),
            OperatorKind.Subtract => unchecked(a - b),
            OperatorKind.Multiply => unchecked(a * b),
            OperatorKind.Divide => a / b,
            OperatorKind.Modulo => a % b,
            OperatorKind.Equal => a == b,
            OperatorKind.NotEqual => a != b,
            OperatorKind.LessThan => a < b,
            OperatorKind.LessThanOrEqual => a <= b,
            OperatorKind.GreaterThan => a > b,
            OperatorKind.GreaterThanOrEqual => a >= b,
            _ => null
        };
    }

    /// <summary>
    /// A declared operator runs only for Decimal arithmetic and unary minus, and for the comparisons of the declaring
    /// types in <see cref="DeclaredOperatorTypes"/>. Temporal arithmetic and any user-declared operator decline
    /// (T-10 owns the temporal fold fence).
    /// </summary>
    private static object? InvokeDeclaredOperator(OperatorKind kind, ITypeMethod signature, object x, object? y) {
        if (signature is not ClrMethod { MethodInfo: { DeclaringType: { } declaring } method })
            return null;
        var allowed = declaring == typeof(decimal)
                && kind is OperatorKind.Add or OperatorKind.Subtract or OperatorKind.Multiply or OperatorKind.Divide
                    or OperatorKind.Modulo or OperatorKind.UnaryMinus
            || DeclaredOperatorTypes.Contains(declaring)
                && kind is OperatorKind.Equal or OperatorKind.NotEqual or OperatorKind.LessThan or OperatorKind.LessThanOrEqual
                    or OperatorKind.GreaterThan or OperatorKind.GreaterThanOrEqual;
        if (!allowed)
            return null;
        return method.Invoke(null, y is null ? [x] : [x, y]);
    }

    private readonly struct FoldResult {
        private readonly object? _value;
        public bool HasValue { get; }
        public object? Value => HasValue ? _value : throw new InvalidOperationException("No value");

        private FoldResult(object? value, bool hasValue) {
            _value = value;
            HasValue = hasValue;
        }

        public static FoldResult Success(object? value) => new(value, true);
        public static FoldResult NotFoldable => new(null, false);
    }
}

public static class ConstantFoldingExtensions {
    extension(AnalyzerBuilder builder) {
        /// <summary>
        /// Adds constant folding optimization to the analyzer.
        /// This evaluates constant expressions at analysis time.
        /// </summary>
        public AnalyzerBuilder UseConstantFolding() {
            builder.AddAnalyzer(new ConstantFoldingPass());
            return builder;
        }
    }

    extension(INodeMetadataProvider context) {
        /// <summary>
        /// Gets the constant-folded value for a node, if available.
        /// </summary>
        public object? GetConstantValue(Node node) {
            var metadata = context.GetMetadata<ConstantValueMetadata>(node);
            return metadata?.Value;
        }

        /// <summary>
        /// Returns true if the node has been determined to be a constant.
        /// </summary>
        public bool IsConstant(Node node) {
            return context.GetMetadata<ConstantValueMetadata>(node) != null || node is Constant;
        }
    }
}