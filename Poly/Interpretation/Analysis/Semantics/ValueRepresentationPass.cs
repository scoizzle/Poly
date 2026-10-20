using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Vm;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation.Analysis.Semantics;

/// <summary>
/// Describes how a value is represented on the VM evaluation stack.
/// </summary>
public enum ValueRepresentationKind {
    /// <summary>Node produces no value (statement/void).</summary>
    Void,

    /// <summary>Inline word in a stack slot: a number, a boolean (0 = false,
    /// non-zero = true), or an IEEE float/double bit pattern. Boolean-ness is the
    /// CLR type (<c>typeof(bool)</c>), decoded at the boundary, not a slot kind.</summary>
    StackScalar,

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
/// stack word, a heap handle, or nothing (void). Boolean-ness is the CLR type.
///
/// Post-order traversal: children are classified first, then the parent
/// node's representation is derived from its children's types.
///
/// Placement: after <c>ControlFlowAnalysis</c> (standard pipeline:
/// ConstantFolding → ControlFlow → ValueRepresentation).
/// </summary>
internal sealed class ValueRepresentationAnalyzer : INodeAnalyzer {
    public const string Id = "ValueRepresentation";
    public string PassName => Id;
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
            InterpolatedString => (ValueRepresentationKind.HeapRef, typeof(string)),
            Parameter p => ClassifyFromResolvedType(context, p),
            Variable v => ClassifyFromResolvedType(context, v),
            ThisReference tr => ClassifyThisReference(context, tr),
            Block b => ClassifyBlock(context, b),
            IfStatement => (ValueRepresentationKind.Void, null),
            WhileLoop => (ValueRepresentationKind.Void, null),
            DoWhileLoop => (ValueRepresentationKind.Void, null),
            ForLoop => (ValueRepresentationKind.Void, null),
            ForEachLoop => (ValueRepresentationKind.Void, null),
            Return r => r.Value is null
                ? (ValueRepresentationKind.Void, null)
                : PropagateChild(context, r.Value),
            ThrowStatement => (ValueRepresentationKind.Void, null),
            BreakStatement => (ValueRepresentationKind.Void, null),
            ContinueStatement => (ValueRepresentationKind.Void, null),
            GotoStatement => (ValueRepresentationKind.Void, null),
            UsingStatement => (ValueRepresentationKind.Void, null),
            Assignment a => PropagateChild(context, a.Value),
            SwitchStatement sw => ClassifySwitch(sw, context),
            TryCatchFinally tcf => PropagateChild(context, tcf.TryBlock),
            Equal => (ValueRepresentationKind.StackScalar, typeof(bool)),
            NotEqual => (ValueRepresentationKind.StackScalar, typeof(bool)),
            LessThan => (ValueRepresentationKind.StackScalar, typeof(bool)),
            LessThanOrEqual => (ValueRepresentationKind.StackScalar, typeof(bool)),
            GreaterThan => (ValueRepresentationKind.StackScalar, typeof(bool)),
            GreaterThanOrEqual => (ValueRepresentationKind.StackScalar, typeof(bool)),
            And => (ValueRepresentationKind.StackScalar, typeof(bool)),
            Or => (ValueRepresentationKind.StackScalar, typeof(bool)),
            Not => (ValueRepresentationKind.StackScalar, typeof(bool)),
            Add n => ClassifyArithmetic(context, n, n.LeftHandValue, n.RightHandValue, isAdd: true),
            Subtract n => ClassifyArithmetic(context, n, n.LeftHandValue, n.RightHandValue, isAdd: false),
            Multiply n => ClassifyArithmetic(context, n, n.LeftHandValue, n.RightHandValue, isAdd: false),
            Divide n => ClassifyArithmetic(context, n, n.LeftHandValue, n.RightHandValue, isAdd: false),
            Modulo n => ClassifyArithmetic(context, n, n.LeftHandValue, n.RightHandValue, isAdd: false),
            UnaryMinus => ClassifyWithResolvedType(context, node, ValueRepresentationKind.StackScalar),
            BitwiseAnd => ClassifyWithResolvedType(context, node, ValueRepresentationKind.StackScalar),
            BitwiseOr => ClassifyWithResolvedType(context, node, ValueRepresentationKind.StackScalar),
            BitwiseXor => ClassifyWithResolvedType(context, node, ValueRepresentationKind.StackScalar),
            ShiftLeft => ClassifyWithResolvedType(context, node, ValueRepresentationKind.StackScalar),
            ShiftRight => ClassifyWithResolvedType(context, node, ValueRepresentationKind.StackScalar),
            New => (ValueRepresentationKind.HeapRef, null),
            NewArray => (ValueRepresentationKind.HeapRef, null),
            Lambda l => ClassifyLambda(context, l),
            TypeAs => (ValueRepresentationKind.HeapRef, null),
            TypeCast tc => ClassifyTypeCast(context, tc),
            TypeOf => (ValueRepresentationKind.HeapRef, typeof(Type)),
            Comment => (ValueRepresentationKind.Void, null),
            ThrowExpression => (ValueRepresentationKind.Void, null),
            Default d => ClassifyDefault(context, d),
            TypeIs => (ValueRepresentationKind.StackScalar, typeof(bool)),
            IndexAccess ia => ClassifyIndexAccess(context, ia),
            Member ma => ClassifyMember(context, ma),
            Invoke inv => ClassifyInvoke(context, inv),
            Coalesce coalesce => ClassifyCoalesce(context, coalesce),
            Conditional cond => ClassifyConditional(context, cond),
            NullForgiving nf => PropagateChild(context, nf.Operand),
            Await aw => ClassifyAwait(context, aw),
            _ => (ValueRepresentationKind.Unknown, null),
        };
    }

    /// <summary>
    /// Classify a node using its resolved type for ClrType, defaulting to
    /// <paramref name="defaultKind"/> when the kind is already known (e.g. StackScalar).
    /// </summary>
    private static (ValueRepresentationKind Kind, Type? ClrType) ClassifyArithmetic(
        AnalysisContext context, Node op, Node left, Node right, bool isAdd) {
        var l = context.GetMetadata<ValueRepresentationMetadata>(left);
        var r = context.GetMetadata<ValueRepresentationMetadata>(right);
        if (isAdd && (l?.ClrType == typeof(string) || r?.ClrType == typeof(string)
            || left is Constant { Value: string } || right is Constant { Value: string }))
            return (ValueRepresentationKind.HeapRef, typeof(string));
        if (l?.ClrType == typeof(decimal) || r?.ClrType == typeof(decimal)
            || left is Constant { Value: decimal } || right is Constant { Value: decimal })
            return (ValueRepresentationKind.HeapRef, typeof(decimal));
        if (l?.ClrType == typeof(double) || r?.ClrType == typeof(double)
            || left is Constant { Value: double } || right is Constant { Value: double })
            return (ValueRepresentationKind.StackScalar, typeof(double));
        if (l?.ClrType == typeof(float) || r?.ClrType == typeof(float)
            || left is Constant { Value: float } || right is Constant { Value: float })
            return (ValueRepresentationKind.StackScalar, typeof(float));
        return ClassifyWithResolvedType(context, op, ValueRepresentationKind.StackScalar);
    }

    private static (ValueRepresentationKind Kind, Type? ClrType) ClassifyWithResolvedType(
        AnalysisContext context, Node node, ValueRepresentationKind defaultKind) {
        var typeDef = context.GetResolvedType(node);
        if (typeDef is IClrTypeDefinition clrType)
            return (defaultKind, clrType.RuntimeType);
        return (defaultKind, null);
    }

    /// <summary>An <c>await</c> is represented as its result type: the awaiter's
    /// <c>GetResult()</c> (<c>T</c> for <c>Task&lt;T&gt;</c>, void for <c>Task</c>).</summary>
    private static (ValueRepresentationKind Kind, Type? ClrType) ClassifyAwait(
        AnalysisContext context, Await await) {
        var typeDef = context.GetResolvedType(await);
        if (typeDef is IClrTypeDefinition { RuntimeType: var rt } && rt == typeof(void))
            return (ValueRepresentationKind.Void, null);
        if (typeDef is not null)
            return ClassifyTypeDefinition(typeDef);
        return (ValueRepresentationKind.Unknown, null);
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

        // Fallback: propagate from the true branch. A `throw` arm is Void and
        // carries no value, so classify from the other arm instead.
        var trueMeta = context.GetMetadata<ValueRepresentationMetadata>(node.IfTrue);
        if (trueMeta is not null && trueMeta.Kind != ValueRepresentationKind.Void)
            return (trueMeta.Kind, trueMeta.ClrType);
        var falseMeta = context.GetMetadata<ValueRepresentationMetadata>(node.IfFalse);
        if (falseMeta is not null)
            return (falseMeta.Kind, falseMeta.ClrType);

        return (ValueRepresentationKind.Unknown, null);
    }

    private static (ValueRepresentationKind Kind, Type? ClrType) ClassifyTypeCast(
        AnalysisContext context, TypeCast tc) {
        var fromResolved = ClassifyFromResolvedType(context, tc);
        if (fromResolved.Kind != ValueRepresentationKind.Unknown)
            return fromResolved;
        if (tc.TargetTypeReference is ClrTypeReference ctr)
            return ClassifyClr(ctr.RuntimeType);
        return (ValueRepresentationKind.Unknown, null);
    }

    private static (ValueRepresentationKind Kind, Type? ClrType) ClassifyDefault(
        AnalysisContext context, Default d) {
        // The resolved type of the default covers every type-reference spelling the
        // reader folds (a primitive `default(int)` resolves to Int32); fall back to the
        // CLR reference form and the bare `default` for producers that emit one.
        var fromResolved = ClassifyFromResolvedType(context, d);
        if (fromResolved.Kind != ValueRepresentationKind.Unknown)
            return fromResolved;
        if (d.TargetType is ClrTypeReference ctr)
            return ClassifyClr(ctr.RuntimeType);
        if (d.TargetType is null)
            return (ValueRepresentationKind.StackScalar, typeof(long));
        return (ValueRepresentationKind.Unknown, null);
    }

    private static (ValueRepresentationKind Kind, Type? ClrType) ClassifyClr(Type t) {
        if (Nullable.GetUnderlyingType(t) is not null)
            return (ValueRepresentationKind.HeapRef, t);
        if (t == typeof(bool))
            return (ValueRepresentationKind.StackScalar, typeof(bool));
        if (t.IsValueType && RingCodec.IsInline(t))
            return (ValueRepresentationKind.StackScalar, t);
        if (t == typeof(float) || t == typeof(double))
            return (ValueRepresentationKind.StackScalar, t);
        return (ValueRepresentationKind.HeapRef, t);
    }

    private static (ValueRepresentationKind Kind, Type? ClrType) ClassifyConstant(Constant c) {
        if (c.Value is null)
            return (ValueRepresentationKind.HeapRef, null);
        if (c.Value is bool)
            return (ValueRepresentationKind.StackScalar, typeof(bool));
        if (c.Value is string)
            return (ValueRepresentationKind.HeapRef, typeof(string));
        if (c.Value.GetType().IsValueType) {
            var vt = c.Value.GetType();
            // Non-long value types (DateTime, DateOnly, Guid, ...) are heap handles.
            return RingCodec.IsInline(vt)
                ? (ValueRepresentationKind.StackScalar, vt)
                : (ValueRepresentationKind.HeapRef, vt);
        }

        // All other reference types (arrays, objects) go on the heap
        return (ValueRepresentationKind.HeapRef, c.Value.GetType());
    }

    private static (ValueRepresentationKind Kind, Type? ClrType) ClassifyLambda(
        AnalysisContext context, Lambda lambda) {
        var resolved = ClassifyFromResolvedType(context, lambda);
        return resolved.Kind == ValueRepresentationKind.Unknown
            ? (ValueRepresentationKind.HeapRef, null)
            : resolved;
    }

    private static (ValueRepresentationKind Kind, Type? ClrType) ClassifyInvoke(
        AnalysisContext context, Invoke invoke) {
        var resolved = ClassifyFromResolvedType(context, invoke);
        if (resolved.Kind != ValueRepresentationKind.Unknown)
            return resolved;
        if (invoke.Delegate is Lambda lambda)
            return PropagateChild(context, lambda.Body);
        if (context.GetMetadata<StoredLambdaMetadata>(invoke.Delegate) is { } stored)
            return PropagateChild(context, stored.Lambda.Body);
        if (invoke.Delegate is Member member
            && context.GetResolvedType(member.Value) is IClrTypeDefinition receiver) {
            var returns = receiver.Methods
                .Where(m => string.Equals(m.Name, member.MemberName, StringComparison.Ordinal)
                    && m.Parameters.Count() == invoke.Arguments.Length)
                .Select(m => m.MemberTypeDefinition)
                .Distinct()
                .ToList();
            if (returns.Count == 1) {
                var (kind, clrType) = ClassifyTypeDefinition(returns[0]);
                if (kind == ValueRepresentationKind.HeapRef
                    && clrType is { IsValueType: true }
                    && !RingCodec.IsInline(clrType))
                    return (kind, clrType);
            }
        }
        return resolved;
    }

    private static (ValueRepresentationKind Kind, Type? ClrType) ClassifySwitch(
        SwitchStatement sw, AnalysisContext context) {
        if (sw.Cases.Count > 0)
            return PropagateChild(context, sw.Cases[0].Body);
        if (sw.DefaultCase is not null)
            return PropagateChild(context, sw.DefaultCase);
        return (ValueRepresentationKind.StackScalar, typeof(long));
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
            if (Nullable.GetUnderlyingType(rt) is not null)
                return (ValueRepresentationKind.HeapRef, rt);
            if (rt == typeof(bool))
                return (ValueRepresentationKind.StackScalar, rt);
            if (rt.IsValueType || rt.IsPrimitive) {
                // Non-numeric value types (DateTime, DateOnly, Guid, TimeSpan, ...)
                // cannot be inlined into the long-based VM ring — their values live
                // on the heap as boxed handles (RingCodec.IsInline).
                return RingCodec.IsInline(rt)
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
        var last = PropagateChild(context, block.Nodes[^1]);
        var fromReturn = FindValuedReturnKind(context, block);
        if (fromReturn is null)
            return last;
        if (last.Kind == ValueRepresentationKind.Void)
            return fromReturn.Value;
        if (HasDominatingValuedReturn(block))
            return fromReturn.Value;
        return last;
    }

    /// <summary>Valued <see cref="Return"/> that can yield from this node.
    /// Nested lambdas are skipped. <c>If(false, …)</c> then-branches are skipped;
    /// <c>If(true, …)</c> uses the then-branch only.</summary>
    private static (ValueRepresentationKind Kind, Type? ClrType)? FindValuedReturnKind(
        AnalysisContext context, Node node) {
        if (node is Lambda)
            return null;
        if (node is IfStatement ifStmt) {
            if (ifStmt.Condition is Constant { Value: false })
                return ifStmt.ElseBranch is null ? null : FindValuedReturnKind(context, ifStmt.ElseBranch);
            if (ifStmt.Condition is Constant { Value: true })
                return FindValuedReturnKind(context, ifStmt.ThenBranch);
        }
        if (node is Return { Value: not null } ret)
            return PropagateChild(context, ret.Value);
        foreach (var child in node.Children) {
            if (child is null) continue;
            var found = FindValuedReturnKind(context, child);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static bool HasDominatingValuedReturn(Block block) {
        foreach (var stmt in block.Nodes) {
            if (stmt is Return { Value: not null })
                return true;
            if (stmt is IfStatement { Condition: Constant { Value: true } } ifTrue
                && FindValuedReturnKindCheap(ifTrue.ThenBranch))
                return true;
        }
        return false;
    }

    private static bool FindValuedReturnKindCheap(Node node) {
        if (node is Lambda)
            return false;
        if (node is Return { Value: not null })
            return true;
        if (node is IfStatement ifStmt) {
            if (ifStmt.Condition is Constant { Value: false })
                return ifStmt.ElseBranch is not null && FindValuedReturnKindCheap(ifStmt.ElseBranch);
            if (ifStmt.Condition is Constant { Value: true })
                return FindValuedReturnKindCheap(ifStmt.ThenBranch);
        }
        foreach (var child in node.Children) {
            if (child is not null && FindValuedReturnKindCheap(child))
                return true;
        }
        return false;
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