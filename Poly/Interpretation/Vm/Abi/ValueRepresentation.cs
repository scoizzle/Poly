using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Interpretation.Vm.Abi;

/// <summary>Describes how a value is represented in the VM's long ring slot.
/// Boolean-ness is the CLR type; it is not a slot kind.</summary>
public enum ValueRepresentationKind {
    /// <summary>Node produces no value (statement/void).</summary>
    Void,

    /// <summary>Inline word: a number, a boolean (0 = false, non-zero = true),
    /// or an IEEE float/double bit pattern.</summary>
    StackScalar,

    /// <summary>Heap-allocated reference; the slot holds a heap handle.</summary>
    HeapRef,

    /// <summary>Representation could not be determined from the type.</summary>
    Unknown,
}

/// <summary>
/// The emit-time representation rule: a node's slot kind is its effective node's
/// resolved CLR type. Analysis replaces a node when the runtime action differs from
/// the default semantics (<c>int + string</c> becomes <c>String.Concat</c>,
/// <c>date + 1</c> becomes <c>AddDays</c>), and the replacement carries the real
/// action's type, so emit never needs a per-node representation bag.
/// </summary>
internal static class ValueRepresentationClassifier {
    public static (ValueRepresentationKind Kind, Type? ClrType) Classify(
        INodeMetadataProvider? provider, Node? node) {
        if (node is null)
            return (ValueRepresentationKind.Unknown, null);

        var effective = Effective(provider, node);

        // Statements have no CLR type; they produce no value. A valued return and a
        // try/catch yield what their value-bearing part yields.
        if (effective is IfStatement or WhileLoop or DoWhileLoop or ForLoop or ForEachLoop
            or UsingStatement or BreakStatement or ContinueStatement or GotoStatement
            or ThrowStatement or Comment or ThrowExpression or StridedSetBits)
            return (ValueRepresentationKind.Void, null);
        if (effective is Return { Value: { } returned })
            return Classify(provider, returned);
        if (effective is Return)
            return (ValueRepresentationKind.Void, null);
        if (effective is TryCatchFinally tryCatch)
            return Classify(provider, tryCatch.TryBlock);

        // A constant carries its value, not a declared type (folding runs after the
        // type pass). Its slot is whatever the constant encoder produces: an inline
        // word, or a boxed heap handle (an enum is boxed today); null is the sentinel.
        if (effective is Constant constant) {
            if (constant.Value is null)
                return (ValueRepresentationKind.HeapRef, null);
            var valueType = constant.Value.GetType();
            // Classify the slot the writer actually produces: TryInlineConstant is the
            // constant encoder, and an enum constant is not inline there (it is boxed).
            return (RingCodec.TryInlineConstant(constant.Value, out _)
                ? ValueRepresentationKind.StackScalar
                : ValueRepresentationKind.HeapRef, valueType);
        }

        var type = provider?.GetResolvedType(effective)?.GetRuntimeType()
            ?? provider?.GetResolvedMember(effective)?.MemberTypeDefinition.GetRuntimeType()
            ?? InferUnresolvedClrReturn(provider, effective);

        return type is null
            ? (ValueRepresentationKind.Unknown, null)
            : (KindFor(type), type);
    }

    public static ValueRepresentationKind KindOf(INodeMetadataProvider? provider, Node? node) =>
        Classify(provider, node).Kind;

    public static Type? ClrTypeOf(INodeMetadataProvider? provider, Node? node) =>
        Classify(provider, node).ClrType;

    /// <summary>Type inference for an emit-time unresolved CLR invoke whose only
    /// same-name/arity overloads agree on their return type (for example
    /// <c>DateTime.AddMinutes(long)</c> widening to the <c>double</c> overload). The
    /// resolved member wins whenever analysis found it; this never guesses between
    /// differing return types.
    /// <para>An AST type definition is excluded: its runtime type is the VM's own storage
    /// (a dictionary), not the API the program calls, so reflecting over it can only
    /// invent a signature the type does not have.</para></summary>
    private static Type? InferUnresolvedClrReturn(INodeMetadataProvider? provider, Node node) {
        if (provider is null)
            return null;
        if (node is not Invoke { Delegate: Member member } invoke)
            return null;
        var receiverDefinition = provider.GetResolvedType(member.Value);
        if (receiverDefinition is null or AstTypeDefinition)
            return null;
        var receiver = receiverDefinition.GetRuntimeType();
        if (receiver is null)
            return null;
        var returns = receiver
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Static)
            .Where(method => method.Name == member.MemberName
                && method.GetParameters().Length == invoke.Arguments.Length)
            .Select(method => method.ReturnType)
            .Distinct()
            .ToArray();
        return returns.Length == 1 ? returns[0] : null;
    }

    /// <summary>The node a backend compiles: replacements are the real runtime
    /// action, so their type is the one that classifies the slot.</summary>
    public static Node Effective(INodeMetadataProvider? provider, Node node) {
        var current = node;
        while (provider?.GetNodeReplacement(current) is { } replacement
            && !ReferenceEquals(replacement, current)) {
            current = replacement;
        }
        return current;
    }

    /// <summary>Classify a CLR type. A nullable is a heap handle (its slot holds a
    /// boxed value, not the underlying inline word); enums and the integral/char/
    /// bool/IEEE types are inline words; everything else is a handle.</summary>
    public static ValueRepresentationKind KindFor(Type type) {
        if (type == typeof(void))
            return ValueRepresentationKind.Void;
        if (Nullable.GetUnderlyingType(type) is not null)
            return ValueRepresentationKind.HeapRef;
        if (type.IsEnum || type == typeof(bool) || type == typeof(char)
            || type == typeof(long) || type == typeof(int) || type == typeof(short)
            || type == typeof(byte) || type == typeof(sbyte) || type == typeof(ushort)
            || type == typeof(uint) || type == typeof(ulong)
            || type == typeof(float) || type == typeof(double))
            return ValueRepresentationKind.StackScalar;
        return ValueRepresentationKind.HeapRef;
    }
}