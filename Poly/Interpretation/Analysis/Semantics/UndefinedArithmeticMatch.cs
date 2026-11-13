namespace Poly.Interpretation.Analysis.Semantics;

/// <summary>
/// Interim rule (T-22 removes it when it reports the undefined operator at the node). Arithmetic over operands the type
/// provider defines no operator for (<c>object + 1</c>, an entity or struct operand, a null constant, <c>"a" + unresolved</c>,
/// the nullable shift and bitwise cells <c>Int32? &lt;&lt; Int64</c> and <c>UInt64? | Int32</c>) has no stamp, but before the
/// provider typed arithmetic the private rule stamped such a node with its left operand's type (a string when an operand
/// was exactly <c>string</c>), and every consumer that reads an argument, an arm or a value type judged it by that type. This
/// answers that type for <b>matching only</b>: it never stamps the node (so the ledger and the VM root type do not move)
/// and nothing else reads it. A node master accepted still executes; a call, constructor, arm, coalesce or assignment that
/// master rejected is rejected on the same message, because the consumer sees the same type. The nullable bitwise and shift
/// cells took the operand the other converts to (master's wider-operand promotion), which the provider answers through
/// <c>ResolveConversionFrom</c>, so no rank table lives here.
/// </summary>
internal static class UndefinedArithmeticMatch {
    /// <summary>True for the five arithmetic kinds and the bitwise and shift kinds.</summary>
    public static bool TryGetKind(Node node, out OperatorKind kind, out Node left, out Node right) {
        (kind, left, right) = node switch {
            Add n => (OperatorKind.Add, n.LeftHandValue, n.RightHandValue),
            Subtract n => (OperatorKind.Subtract, n.LeftHandValue, n.RightHandValue),
            Multiply n => (OperatorKind.Multiply, n.LeftHandValue, n.RightHandValue),
            Divide n => (OperatorKind.Divide, n.LeftHandValue, n.RightHandValue),
            Modulo n => (OperatorKind.Modulo, n.LeftHandValue, n.RightHandValue),
            BitwiseAnd n => (OperatorKind.BitwiseAnd, n.LeftHandValue, n.RightHandValue),
            BitwiseOr n => (OperatorKind.BitwiseOr, n.LeftHandValue, n.RightHandValue),
            BitwiseXor n => (OperatorKind.BitwiseXor, n.LeftHandValue, n.RightHandValue),
            ShiftLeft n => (OperatorKind.ShiftLeft, n.LeftHandValue, n.RightHandValue),
            ShiftRight n => (OperatorKind.ShiftRight, n.LeftHandValue, n.RightHandValue),
            _ => (default, null!, null!)
        };
        return left is not null;
    }

    /// <summary>The type a node had when the stamp was the left operand's type, from the operands' own matching types.</summary>
    public static ITypeDefinition? Of(AnalysisContext context, OperatorKind kind, ITypeDefinition? left, ITypeDefinition? right) {
        if (kind is OperatorKind.Add
            && (left?.GetRuntimeType() == typeof(string) || right?.GetRuntimeType() == typeof(string)))
            return context.TypeDefinitions.GetTypeDefinition(typeof(string));
        if (left is null || right is null)
            return null;

        var lift = IsNullable(left) || IsNullable(right);
        var promoted = Peel(context, left);
        var other = Peel(context, right);
        if (lift && kind is OperatorKind.BitwiseAnd or OperatorKind.BitwiseOr or OperatorKind.BitwiseXor
                or OperatorKind.ShiftLeft or OperatorKind.ShiftRight
            && !TypeDefinitionExtensions.IsSameType(promoted, other)
            && other.ResolveConversionFrom(promoted) is { Kind: ConversionOperatorKind.Implicit })
            promoted = other;

        if (!lift || promoted.GetRuntimeType() is not { IsValueType: true } runtime
            || Nullable.GetUnderlyingType(runtime) is not null)
            return promoted;
        return context.TypeDefinitions.GetTypeDefinition(typeof(Nullable<>).MakeGenericType(runtime)) ?? promoted;
    }

    /// <summary>
    /// The matching type of a node once the resolver has stamped the tree: its stamp, or for an unstamped arithmetic node
    /// the type <see cref="Of"/> gives from its operands' matching types.
    /// </summary>
    public static ITypeDefinition? ForNode(AnalysisContext context, Node node) {
        var effective = context.GetEffectiveNode(node);
        if (context.GetResolvedType(effective) is { } stamped)
            return stamped;
        return TryGetKind(effective, out var kind, out var left, out var right)
            ? Of(context, kind, ForNode(context, left), ForNode(context, right))
            : null;
    }

    private static bool IsNullable(ITypeDefinition type) =>
        type.GetRuntimeType() is { } runtime && Nullable.GetUnderlyingType(runtime) is not null;

    private static ITypeDefinition Peel(AnalysisContext context, ITypeDefinition type) =>
        type.GetRuntimeType() is { } runtime && Nullable.GetUnderlyingType(runtime) is { } underlying
            ? context.TypeDefinitions.GetTypeDefinition(underlying) ?? type
            : type;
}
