using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation.Analysis.Semantics;

/// <summary>
/// Describes how a value is represented on the VM evaluation stack.
/// </summary>
public enum ValueRepresentationKind {
    /// <summary>Node produces no value (statement/void).</summary>
    Void,

    /// <summary>Numeric scalar stored directly in a stack slot (int, long, float, etc.).</summary>
    StackScalar,

    /// <summary>Boolean value (true=1, false=0).</summary>
    Bool,

    /// <summary>Heap-allocated reference; the stack slot holds a heap handle.</summary>
    HeapRef,

    /// <summary>Representation could not be determined statically.</summary>
    Unknown,
}

/// <summary>
/// Metadata indicating the value representation kind and optional CLR type for a node.
/// </summary>
public sealed record ValueRepresentationMetadata(
    ValueRepresentationKind Kind,
    Type? ClrType
) : IAnalysisMetadata;

/// <summary>
/// Analysis pass that classifies every expression/statement node by its
/// <see cref="ValueRepresentationKind"/> — whether the node produces a
/// stack scalar, a heap handle, a boolean, or nothing (void).
///
/// Post-order traversal: children are classified first, then the parent
/// node's representation is derived from its children's types.
///
/// Placement: after <c>ControlFlowAnalysis</c>, before <c>ConstantFolding</c>.
/// </summary>
internal sealed class ValueRepresentationAnalyzer : INodeAnalyzer {
    public const string Id = "ValueRepresentation";
    public string PassName => Id;
    public string[] Dependencies => [TypeAndMemberResolver.Id, ControlFlowAnalysisPass.Id];
    public void Analyze(AnalysisContext context, Node node) {
        // Post-order: classify children first
        this.AnalyzeChildren(context, node);

        var (kind, clrType) = Classify(context, node);

        context.SetMetadata(node, new ValueRepresentationMetadata(kind, clrType));
    }

    private static (ValueRepresentationKind Kind, Type? ClrType) Classify(
        AnalysisContext context, Node node) {
        return node switch {
            Constant c => ClassifyConstant(c),
            Parameter p => ClassifyFromResolvedType(context, p),
            Variable v => ClassifyFromResolvedType(context, v),
            ThisReference tr => ClassifyThisReference(context, tr),
            Block b => ClassifyBlock(context, b),
            IfStatement => (ValueRepresentationKind.Void, null),
            WhileLoop => (ValueRepresentationKind.Void, null),
            ForLoop => (ValueRepresentationKind.Void, null),
            ForEachLoop => (ValueRepresentationKind.Void, null),
            Return => (ValueRepresentationKind.Void, null),
            ThrowStatement => (ValueRepresentationKind.Void, null),
            Assignment => (ValueRepresentationKind.Void, null),
            CallExternal => (ValueRepresentationKind.Void, null),
            Equal => (ValueRepresentationKind.Bool, typeof(bool)),
            NotEqual => (ValueRepresentationKind.Bool, typeof(bool)),
            LessThan => (ValueRepresentationKind.Bool, typeof(bool)),
            LessThanOrEqual => (ValueRepresentationKind.Bool, typeof(bool)),
            GreaterThan => (ValueRepresentationKind.Bool, typeof(bool)),
            GreaterThanOrEqual => (ValueRepresentationKind.Bool, typeof(bool)),
            And => (ValueRepresentationKind.Bool, typeof(bool)),
            Or => (ValueRepresentationKind.Bool, typeof(bool)),
            Not => (ValueRepresentationKind.Bool, typeof(bool)),
            Add => ClassifyWithResolvedType(context, node, ValueRepresentationKind.StackScalar),
            Subtract => ClassifyWithResolvedType(context, node, ValueRepresentationKind.StackScalar),
            Multiply => ClassifyWithResolvedType(context, node, ValueRepresentationKind.StackScalar),
            Divide => ClassifyWithResolvedType(context, node, ValueRepresentationKind.StackScalar),
            Modulo => ClassifyWithResolvedType(context, node, ValueRepresentationKind.StackScalar),
            UnaryMinus => ClassifyWithResolvedType(context, node, ValueRepresentationKind.StackScalar),
            BitwiseAnd => ClassifyWithResolvedType(context, node, ValueRepresentationKind.StackScalar),
            BitwiseOr => ClassifyWithResolvedType(context, node, ValueRepresentationKind.StackScalar),
            BitwiseXor => ClassifyWithResolvedType(context, node, ValueRepresentationKind.StackScalar),
            ShiftLeft => ClassifyWithResolvedType(context, node, ValueRepresentationKind.StackScalar),
            ShiftRight => ClassifyWithResolvedType(context, node, ValueRepresentationKind.StackScalar),
            New => (ValueRepresentationKind.HeapRef, null),
            NewArray => (ValueRepresentationKind.HeapRef, null),
            Lambda => (ValueRepresentationKind.HeapRef, null),
            TypeAs => (ValueRepresentationKind.HeapRef, null),
            TypeIs => (ValueRepresentationKind.Bool, typeof(bool)),
            IndexAccess ia => ClassifyIndexAccess(context, ia),
            Member ma => ClassifyMember(context, ma),
            Invoke inv => ClassifyFromResolvedType(context, inv),
            Coalesce coalesce => ClassifyCoalesce(context, coalesce),
            Conditional cond => ClassifyConditional(context, cond),
            NullForgiving nf => PropagateChild(context, nf.Operand),
            _ => (ValueRepresentationKind.Unknown, null),
        };
    }

    /// <summary>
    /// Classify a node using its resolved type for ClrType, defaulting to
    /// <paramref name="defaultKind"/> when the kind is already known (e.g. StackScalar).
    /// </summary>
    private static (ValueRepresentationKind Kind, Type? ClrType) ClassifyWithResolvedType(
        AnalysisContext context, Node node, ValueRepresentationKind defaultKind) {
        var typeDef = context.GetResolvedType(node);
        if (typeDef is IClrTypeDefinition clrType)
            return (defaultKind, clrType.RuntimeType);
        return (defaultKind, null);
    }

    private static (ValueRepresentationKind Kind, Type? ClrType) ClassifyCoalesce(
        AnalysisContext context, Coalesce node) {
        // Coalesce result type depends on the left-hand side's resolved type.
        var typeDef = context.GetResolvedType(node);
        if (typeDef is not null)
            return ClassifyTypeDefinition(typeDef);

        // Fallback: propagate from left operand
        var leftMeta = context.GetMetadata<ValueRepresentationMetadata>(node.LeftHandValue);
        if (leftMeta is not null)
            return (leftMeta.Kind, leftMeta.ClrType);

        return (ValueRepresentationKind.Unknown, null);
    }

    private static (ValueRepresentationKind Kind, Type? ClrType) ClassifyConditional(
        AnalysisContext context, Conditional node) {
        // Conditional propagates the type of its true/false branches.
        var typeDef = context.GetResolvedType(node);
        if (typeDef is not null)
            return ClassifyTypeDefinition(typeDef);

        // Fallback: propagate from true branch
        var trueMeta = context.GetMetadata<ValueRepresentationMetadata>(node.IfTrue);
        if (trueMeta is not null)
            return (trueMeta.Kind, trueMeta.ClrType);

        return (ValueRepresentationKind.Unknown, null);
    }

    private static (ValueRepresentationKind Kind, Type? ClrType) ClassifyConstant(Constant c) {
        if (c.Value is null)
            // Null is represented as the 0L sentinel on the VM evaluation stack.
            // Classify as StackScalar to prevent interpretResult from attempting
            // a heap handle dereference on the 0L value.
            return (ValueRepresentationKind.StackScalar, null);
        if (c.Value is bool)
            return (ValueRepresentationKind.Bool, typeof(bool));
        if (c.Value is string)
            return (ValueRepresentationKind.HeapRef, typeof(string));
        if (c.Value.GetType().IsValueType) {
            var vt = c.Value.GetType();
            // Non-long value types (DateTime, DateOnly, Guid, ...) are heap handles.
            return AbiValueTypes.IsLongRepresentable(vt)
                ? (ValueRepresentationKind.StackScalar, vt)
                : (ValueRepresentationKind.HeapRef, vt);
        }

        // All other reference types (arrays, objects) go on the heap
        return (ValueRepresentationKind.HeapRef, c.Value.GetType());
    }

    private static (ValueRepresentationKind Kind, Type? ClrType) ClassifyFromResolvedType(
        AnalysisContext context, Node node) {
        var typeDef = context.GetResolvedType(node);
        if (typeDef is null)
            return (ValueRepresentationKind.Unknown, null);

        return ClassifyTypeDefinition(typeDef);
    }

    private static (ValueRepresentationKind Kind, Type? ClrType) ClassifyThisReference(
        AnalysisContext context, ThisReference node) {
        // 'this' in a value type is a stack scalar; in a reference type it's a heap handle
        var typeDef = context.GetResolvedType(node);
        if (typeDef is null)
            return (ValueRepresentationKind.Unknown, null);

        return ClassifyTypeDefinition(typeDef);
    }

    private static (ValueRepresentationKind Kind, Type? ClrType) ClassifyMember(
        AnalysisContext context, Member node) {
        var member = context.GetResolvedMember(node);
        if (member is not null) {
            var memberType = member.MemberTypeDefinition;
            return ClassifyTypeDefinition(memberType);
        }

        // Fallback: check resolved type on the member node itself
        var typeDef = context.GetResolvedType(node);
        if (typeDef is not null)
            return ClassifyTypeDefinition(typeDef);

        return (ValueRepresentationKind.Unknown, null);
    }

    private static (ValueRepresentationKind Kind, Type? ClrType) ClassifyIndexAccess(
        AnalysisContext context, IndexAccess node) {
        // Index access returns the element type
        var typeDef = context.GetResolvedType(node);
        if (typeDef is not null)
            return ClassifyTypeDefinition(typeDef);

        return (ValueRepresentationKind.Unknown, null);
    }

    private static (ValueRepresentationKind Kind, Type? ClrType) ClassifyTypeDefinition(
        ITypeDefinition typeDef) {
        // Check if it's a CLR type — if so, use IsValueType to distinguish
        if (typeDef is IClrTypeDefinition clrType) {
            var rt = clrType.RuntimeType;
            if (rt == typeof(bool))
                return (ValueRepresentationKind.Bool, rt);
            if (rt.IsValueType || rt.IsPrimitive) {
                // Non-numeric value types (DateTime, DateOnly, Guid, TimeSpan, ...)
                // cannot be inlined into the long-based VM ring — their values live
                // on the heap as boxed handles (AbiValueTypes.IsLongRepresentable).
                return AbiValueTypes.IsLongRepresentable(rt)
                    ? (ValueRepresentationKind.StackScalar, rt)
                    : (ValueRepresentationKind.HeapRef, rt);
            }
            return (ValueRepresentationKind.HeapRef, rt);
        }

        // Non-CLR type: assume heap-ref by default (conservative)
        return (ValueRepresentationKind.HeapRef, null);
    }

    private static (ValueRepresentationKind Kind, Type? ClrType) ClassifyBlock(
        AnalysisContext context, Block block) {
        if (block.Nodes.Count == 0)
            return (ValueRepresentationKind.Void, null);
        var last = block.Nodes[^1];
        return PropagateChild(context, last);
    }

    private static (ValueRepresentationKind Kind, Type? ClrType) PropagateChild(
        AnalysisContext context, Node child) {
        var meta = context.GetMetadata<ValueRepresentationMetadata>(child);
        return meta is not null
            ? (meta.Kind, meta.ClrType)
            : (ValueRepresentationKind.Unknown, null);
    }
}

public static class ValueRepresentationExtensions {
    extension(AnalyzerBuilder builder) {
        /// <summary>
        /// Adds the <see cref="ValueRepresentationAnalyzer"/> to the pipeline.
        /// This pass classifies every node by its value representation kind
        /// (stack scalar, bool, heap ref, void, or unknown).
        /// </summary>
        public AnalyzerBuilder UseValueRepresentationAnalysis() {
            builder.AddAnalyzer(new ValueRepresentationAnalyzer());
            return builder;
        }
    }

    extension(INodeMetadataProvider provider) {
        /// <summary>
        /// Gets the <see cref="ValueRepresentationMetadata"/> for a node, if available.
        /// </summary>
        public ValueRepresentationKind GetValueRepresentation(Node node) {
            return provider.GetMetadata<ValueRepresentationMetadata>(node)?.Kind
                ?? ValueRepresentationKind.Unknown;
        }
    }
}