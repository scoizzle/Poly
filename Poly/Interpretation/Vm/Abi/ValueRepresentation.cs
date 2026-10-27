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
    /// <summary>Classify a CLR type. A primitive of at most 64 bits, and an enum,
    /// is an inline word. That includes <c>bool</c>, <c>char</c>, the integers,
    /// <c>float</c>, <c>double</c>, <c>nint</c>, and <c>nuint</c>. A nullable is a
    /// heap handle. Everything else is a handle.</summary>
    public static ValueRepresentationKind KindFor(Type type) =>
        FrameRepresentation.For(type).Kind;

    public static (ValueRepresentationKind Kind, Type? ClrType) Classify(
        INodeMetadataProvider? provider, Node? node) {
        var (form, clr) = FrameRepresentation.Of(provider, node);
        return (form?.Kind ?? ValueRepresentationKind.Unknown, clr);
    }

    public static ValueRepresentationKind KindOf(INodeMetadataProvider? provider, Node? node) =>
        Classify(provider, node).Kind;

    public static Type? ClrTypeOf(INodeMetadataProvider? provider, Node? node) =>
        Classify(provider, node).ClrType;
}