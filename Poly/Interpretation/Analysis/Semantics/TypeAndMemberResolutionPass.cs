namespace Poly.Interpretation.Analysis.Semantics;

using Poly.Ast.Nodes;

/// <summary>Resolves both types and member references for all AST nodes.
/// Merged from the former separate TypeResolver + MemberResolver passes
/// to avoid duplicate tree walks — both call the same resolvers for
/// Invoke, New, Member, and IndexAccess nodes.</summary>
internal sealed class TypeAndMemberResolver : INodeAnalyzer {
    public const string Id = "TypeAndMember";
    public string PassName => Id;
    public string[] Dependencies => [ThisReferenceContextAnalyzer.Id];
    public void Analyze(AnalysisContext context, Node node) {
        if (node is TryCatchFinally tcf) {
            AnalyzeTryCatchFinally(context, tcf);
            return;
        }

        var resolvedType = ResolveNodeType(context, node);

        if (resolvedType != null) {
            context.SetResolvedType(node, resolvedType!);
        }

        this.AnalyzeChildren(context, node);
    }

    /// <summary>Seed catch <see cref="CatchClause.VariableName"/> types before the body
    /// so <c>Member(ex, "Message")</c> resolves; then walk try/catch/finally normally.</summary>
    private void AnalyzeTryCatchFinally(AnalysisContext context, TryCatchFinally tcf) {
        Analyze(context, tcf.TryBlock);
        if (tcf.CatchClauses is not null) {
            foreach (var clause in tcf.CatchClauses) {
                ITypeDefinition? exType = null;
                if (clause.ExceptionType is not null) {
                    Analyze(context, clause.ExceptionType);
                    exType = context.GetResolvedType(clause.ExceptionType);
                }
                exType ??= context.TypeDefinitions.GetTypeDefinition(typeof(Exception));
                if (!string.IsNullOrEmpty(clause.VariableName) && exType is not null)
                    SeedCatchVariableTypes(context, clause.Body, clause.VariableName!, exType);
                Analyze(context, clause.Body);
            }
        }
        if (tcf.FinallyBlock is not null)
            Analyze(context, tcf.FinallyBlock);
        var tryType = context.GetResolvedType(tcf.TryBlock);
        if (tryType is not null)
            context.SetResolvedType(tcf, tryType);
    }

    private static void SeedCatchVariableTypes(
        AnalysisContext context, Node node, string name, ITypeDefinition type) {
        if (node is Variable v && v.Name == name)
            context.SetResolvedType(v, type);
        if (node is Block block) {
            foreach (var bv in block.Variables) {
                if (bv is Variable dv && dv.Name == name)
                    return;
            }
        }
        foreach (var child in node.Children) {
            if (child is not null)
                SeedCatchVariableTypes(context, child, name, type);
        }
    }

    private static ITypeDefinition? ResolveNodeType(AnalysisContext context, Node node) {
        return node switch {
            Constant c => context.TypeDefinitions.GetTypeDefinition(c.Value?.GetType() ?? typeof(object)),
            ThisReference @this => ResolveThisReferenceType(context, @this),
            Parameter p => ResolveParameterType(context, p),
            Variable v => context.GetResolvedType(v),
            Add add => ResolveNumericArithmeticType(context, add.LeftHandValue, add.RightHandValue),
            Subtract sub => ResolveNumericArithmeticType(context, sub.LeftHandValue, sub.RightHandValue),
            Multiply mul => ResolveNumericArithmeticType(context, mul.LeftHandValue, mul.RightHandValue),
            Divide div => ResolveNumericArithmeticType(context, div.LeftHandValue, div.RightHandValue),
            Modulo mod => ResolveNumericArithmeticType(context, mod.LeftHandValue, mod.RightHandValue),
            UnaryMinus minus => ResolveNodeType(context, minus.Operand),
            And => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),
            Or => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),
            Not => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),
            Equal => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),
            NotEqual => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),
            LessThan => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),
            LessThanOrEqual => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),
            GreaterThan => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),
            GreaterThanOrEqual => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),
            Member memberAccess => ResolveMemberAccessType(context, memberAccess),
            Invoke methodInv => ResolveMethodInvocationType(context, methodInv),
            New @new => ResolveConstructorInvocationType(context, @new),
            NewArray newArr => ResolveNewArrayType(context, newArr),
            IndexAccess indexAccess => ResolveIndexAccessType(context, indexAccess),
            TypeDefinitionReference typeDefRef => typeDefRef.TypeDefinition,
            ClrTypeReference clrTypeRef => context.TypeDefinitions.GetTypeDefinition(clrTypeRef.RuntimeType),
            NamedTypeReference named =>
                AstTypeReferenceResolver.TryResolve(named, context.TypeDefinitions),
            PrimitiveTypeReference prim => prim.PrimitiveId.GetClrType() is { } clr
                ? context.TypeDefinitions.GetTypeDefinition(clr)
                : null,
            TypeReference typeRef => context.TypeDefinitions.GetTypeDefinition(typeRef.TypeName),
            TypeCast cast => ResolveNodeType(context, cast.TargetTypeReference),
            TypeIs => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),
            TypeAs asType => ResolveAsType(context, asType.TargetTypeReference),
            Conditional cond => ResolveNodeType(context, cond.IfTrue),
            Coalesce coal => ResolveNodeType(context, coal.RightHandValue),
            Block block => ResolveBlockType(context, block),
            Assignment assign => ResolveAssignmentType(context, assign),
            ForEachLoop foreachLoop => ResolveForEachLoopType(context, foreachLoop),
            Lambda lambda => ResolveLambdaType(context, lambda),
            NullForgiving nf => ResolveNodeType(context, nf.Operand),
            IfStatement => null,
            BitwiseAnd ba => ResolveArithmeticType(context, ba.LeftHandValue, ba.RightHandValue),
            BitwiseOr bor => ResolveArithmeticType(context, bor.LeftHandValue, bor.RightHandValue),
            BitwiseXor bx => ResolveArithmeticType(context, bx.LeftHandValue, bx.RightHandValue),
            ShiftLeft sl => ResolveArithmeticType(context, sl.LeftHandValue, sl.RightHandValue),
            ShiftRight sr => ResolveArithmeticType(context, sr.LeftHandValue, sr.RightHandValue),
            BitwiseNot bn => ResolveNodeType(context, bn.Operand),
            UsingStatement => null,
            TryCatchFinally tcf => ResolveNodeType(context, tcf.TryBlock),
            SwitchStatement swt => swt.Cases.Count > 0
                ? ResolveNodeType(context, swt.Cases[0].Body)
                : swt.DefaultCase is not null ? ResolveNodeType(context, swt.DefaultCase) : null,
            Await aw => ResolveNodeType(context, aw.Operand),
            Return r => r.Value is null ? null : ResolveNodeType(context, r.Value),
            _ => null
        };
    }

    private static ITypeDefinition? ResolveAsType(AnalysisContext context, Node targetTypeReference) {
        var targetType = ResolveNodeType(context, targetTypeReference);
        if (targetType is null) {
            return null;
        }

        var runtimeType = targetType.GetRuntimeType();
        if (runtimeType is null || !runtimeType.IsValueType || Nullable.GetUnderlyingType(runtimeType) is not null) {
            return targetType;
        }

        var nullableType = typeof(Nullable<>).MakeGenericType(runtimeType);
        return context.TypeDefinitions.GetTypeDefinition(nullableType) ?? targetType;
    }

    private static ITypeDefinition? ResolveForEachLoopType(
        AnalysisContext context,
        ForEachLoop foreachLoop) {
        var collectionType = ResolveNodeType(context, foreachLoop.Collection);
        ITypeDefinition? elementType = null;
        if (foreachLoop.Collection is Member member
            && context.GetResolvedMember(member) is AstPropertyDefinition astProp) {
            elementType = astProp.TryGetCollectionElementType();
        }
        elementType ??= collectionType?.GetElementType();

        if (elementType != null) {
            context.SetResolvedType(foreachLoop.LoopVariable, elementType);
        }

        return context.TypeDefinitions.GetTypeDefinition(typeof(void));
    }

    private static ITypeDefinition? ResolveThisReferenceType(AnalysisContext context, ThisReference thisReference) =>
        context.GetResolvedType(thisReference);

    private static ITypeDefinition? ResolveNumericArithmeticType(
        AnalysisContext context,
        Node left,
        Node right) {
        var leftType = ResolveNodeType(context, left);
        var rightType = ResolveNodeType(context, right);

        if (leftType == null || rightType == null)
            return null;

        var leftRank = GetNumericRank(leftType);
        var rightRank = GetNumericRank(rightType);

        // Promote to the wider numeric type (C#-style); fall back to the
        // left operand type when either side is not a numeric primitive.
        if (leftRank is null || rightRank is null)
            return leftType;

        return leftRank >= rightRank ? leftType : rightType;
    }

    private static int? GetNumericRank(ITypeDefinition type) => type.PrimitiveType switch {
        PrimitiveType.Int8 => 1,
        PrimitiveType.Int16 => 2,
        PrimitiveType.UInt8 => 3,
        PrimitiveType.UInt16 => 4,
        PrimitiveType.Int32 => 5,
        PrimitiveType.UInt32 => 6,
        PrimitiveType.Int64 => 7,
        PrimitiveType.UInt64 => 8,
        PrimitiveType.Float32 => 9,
        PrimitiveType.Float64 => 10,
        PrimitiveType.Decimal => 11,
        _ => null
    };

    private static ITypeDefinition? ResolveArithmeticType(
        AnalysisContext context,
        Node left,
        Node right) {
        var leftType = ResolveNodeType(context, left);
        var rightType = ResolveNodeType(context, right);

        if (leftType == null || rightType == null)
            return null;

        return leftType;
    }

    private static ITypeDefinition? ResolveMemberAccessType(
        AnalysisContext context,
        Member memberAccess) {
        var instanceType = ResolveNodeType(context, memberAccess.Value);
        if (instanceType == null)
            return null;

        var member = instanceType.Members.WithName(memberAccess.MemberName).FirstOrDefault();
        if (member is null)
            context.ReportStructuralFailure(memberAccess, $"Type '{instanceType.Name}' does not contain a member named '{memberAccess.MemberName}'.");
        else
            context.SetResolvedMember(memberAccess, member);
        return member?.MemberTypeDefinition;
    }

    private static ITypeDefinition? ResolveMethodInvocationType(
        AnalysisContext context,
        Invoke invoke) {
        // Resolve the target type through the method reference so the semantic
        // resolver can look it up by name when FindMatchingMethodOverloads is called.
        if (invoke.Delegate is Member memberAccess) {
            var targetType = ResolveNodeType(context, memberAccess.Value);
            if (targetType != null) {
                context.SetResolvedType(memberAccess.Value, targetType);
            }
        }
        else if (invoke.Delegate is Lambda lambda) {
            BindLambdaArguments(context, lambda, invoke.Arguments);
            NoteProducedLambda(context, invoke, lambda.Body);
            return ResolveNodeType(context, lambda.Body);
        }
        else if (invoke.Delegate is Variable or Parameter) {
            if (context.GetMetadata<StoredLambdaMetadata>(invoke.Delegate) is { } stored) {
                BindLambdaArguments(context, stored.Lambda, invoke.Arguments);
                NoteProducedLambda(context, invoke, stored.Lambda.Body);
                return ResolveNodeType(context, stored.Lambda.Body);
            }
            var calleeType = ResolveNodeType(context, invoke.Delegate);
            if (calleeType is not null)
                return calleeType;
        }

        foreach (var argument in invoke.Arguments) {
            var argumentType = ResolveNodeType(context, argument);
            if (argumentType != null) {
                context.SetResolvedType(argument, argumentType);
            }
        }

        var method = MethodInvocationSemanticResolver.ResolveMethod(context, invoke);
        if (method is not null)
            context.SetResolvedMember(invoke, method);
        return method?.MemberTypeDefinition;
    }

    private static ITypeDefinition? ResolveConstructorInvocationType(
        AnalysisContext context,
        New @new) {
        var targetType = ResolveNodeType(context, @new.Type);
        if (targetType != null) {
            context.SetResolvedType(@new.Type, targetType);
        }

        foreach (var argument in @new.Arguments) {
            var argumentType = ResolveNodeType(context, argument);
            if (argumentType != null) {
                context.SetResolvedType(argument, argumentType);
            }
        }

        var constructor = ConstructorInvocationSemanticResolver.ResolveConstructor(context, @new);
        if (constructor is not null)
            context.SetResolvedMember(@new, constructor);
        return constructor?.MemberTypeDefinition ?? targetType;
    }

    private static ITypeDefinition? ResolveNewArrayType(AnalysisContext context, NewArray newArr) {
        var elemType = ResolveNodeType(context, newArr.ElementType);
        var lengthType = ResolveNodeType(context, newArr.Length);
        if (elemType is Introspection.CommonLanguageRuntime.ClrTypeDefinition elemClr)
            return context.TypeDefinitions.GetTypeDefinition(elemClr.RuntimeType.MakeArrayType());
        if (elemType?.FullName is { } fn)
            return context.TypeDefinitions.GetTypeDefinition(fn + "[]");
        return null;
    }

    private static ITypeDefinition? ResolveIndexAccessType(
        AnalysisContext context,
        IndexAccess indexAccess) {
        var instanceType = ResolveNodeType(context, indexAccess.Value);
        if (instanceType == null)
            return null;

        var argumentTypes = indexAccess.Arguments
            .Select(argument => ResolveNodeType(context, argument))
            .ToArray();

        // Also store the resolved indexer member (merged from MemberResolver)
        if (argumentTypes.All(static type => type is not null)) {
            var matchedIndexer = instanceType.FindMatchingIndexers(argumentTypes!).FirstOrDefault();
            if (matchedIndexer is not null)
                context.SetResolvedMember(indexAccess, matchedIndexer);
            return instanceType.GetElementType(argumentTypes!);
        }

        var fallbackIndexer = instanceType.Properties
            .FirstOrDefault(static property => property.Parameters is { } parameters && parameters.Any());
        if (fallbackIndexer is not null)
            context.SetResolvedMember(indexAccess, fallbackIndexer);
        return instanceType.GetElementType();
    }

    private static ITypeDefinition? ResolveAssignmentType(
        AnalysisContext context,
        Assignment assignment) {
        var valueType = ResolveNodeType(context, assignment.Value);

        if (assignment.Destination is Variable variable) {
            if (valueType != null)
                context.SetResolvedType(variable, valueType);
            if (assignment.Value is Lambda assignedLambda)
                context.SetMetadata(variable, new StoredLambdaMetadata(assignedLambda));
            else if (context.GetMetadata<StoredLambdaMetadata>(assignment.Value) is { } produced)
                context.SetMetadata(variable, produced);
            else
                context.Metadata.Remove<StoredLambdaMetadata>(variable);
        }

        return valueType;
    }

    private static ITypeDefinition? ResolveBlockType(
        AnalysisContext context,
        Block block) {
        // Direct indexed access over block.Nodes and Variables (per AggregateChildren/direct precedent from SideEffect for position-dependent/wide fanout like blocks; avoids LINQ allocs/enumerators).
        var nodes = block.Nodes;
        int n = nodes.Count;
        foreach (var variable in block.Variables) {
            if (variable is not Variable v) continue;
            ITypeDefinition? resolved = null;
            for (int i = 0; i < n; i++) {
                if (nodes[i] is Assignment a && ReferenceEquals(a.Destination, v)) {
                    resolved = ResolveNodeType(context, a.Value);
                    if (a.Value is Lambda assignedLambda)
                        context.SetMetadata(v, new StoredLambdaMetadata(assignedLambda));
                    else if (context.GetMetadata<StoredLambdaMetadata>(a.Value) is { } produced)
                        context.SetMetadata(v, produced);
                    break;
                }
            }
            if (resolved != null) {
                context.SetResolvedType(v, resolved);
            }
        }

        if (n == 0) return null;
        return ResolveYieldType(context, block);
    }

    private static ITypeDefinition? ResolveYieldType(AnalysisContext context, Node body) {
        if (body is not Block block)
            return ResolveNodeType(context, body);
        if (block.Nodes.Count == 0)
            return null;
        var last = block.Nodes[^1];
        var lastType = ResolveNodeType(context, last);
        if (lastType is not null && !IsVoidYieldNode(last))
            return lastType;
        return FindValuedReturnType(context, block) ?? lastType;
    }

    private static bool IsVoidYieldNode(Node node) =>
        node is Comment or IfStatement or WhileLoop or DoWhileLoop or ForLoop or ForEachLoop
            or UsingStatement or BreakStatement or ContinueStatement or GotoStatement
            or ThrowStatement
        || node is Return { Value: null };

    private static ITypeDefinition? FindValuedReturnType(AnalysisContext context, Node node) {
        if (node is Lambda)
            return null;
        if (node is IfStatement ifStmt && ifStmt.Condition is Constant { Value: false })
            return ifStmt.ElseBranch is null ? null : FindValuedReturnType(context, ifStmt.ElseBranch);
        if (node is IfStatement ifTrue && ifTrue.Condition is Constant { Value: true })
            return FindValuedReturnType(context, ifTrue.ThenBranch);
        if (node is Return { Value: not null } ret)
            return ResolveNodeType(context, ret.Value);
        foreach (var child in node.Children) {
            if (child is null) continue;
            var found = FindValuedReturnType(context, child);
            if (found is not null)
                return found;
        }
        return null;
    }

    /// <summary>A <see cref="Lambda"/> produces a closure, not the body result.
    /// Tag it as <c>Func&lt;…&gt;</c> / <c>Action&lt;…&gt;</c> from parameter types
    /// plus the body's yield type (what <c>Invoke</c> of that value returns).</summary>
    private static ITypeDefinition? ResolveLambdaType(AnalysisContext context, Lambda lambda) {
        var paramClr = new Type[lambda.Parameters.Count];
        for (int i = 0; i < paramClr.Length; i++) {
            paramClr[i] = RuntimeTypeOf(context, lambda.Parameters[i]) ?? typeof(object);
        }
        var yieldType = ResolveYieldType(context, lambda.Body);
        var yieldClr = yieldType?.GetRuntimeType();
        Type delType;
        try {
            if (yieldClr is null || yieldClr == typeof(void)) {
                delType = paramClr.Length == 0
                    ? typeof(Action)
                    : System.Linq.Expressions.Expression.GetActionType(paramClr);
            }
            else {
                var funcArgs = new Type[paramClr.Length + 1];
                paramClr.CopyTo(funcArgs, 0);
                funcArgs[^1] = yieldClr;
                delType = System.Linq.Expressions.Expression.GetFuncType(funcArgs);
            }
        }
        catch (ArgumentException) {
            return context.TypeDefinitions.GetTypeDefinition(typeof(object));
        }
        return context.TypeDefinitions.GetTypeDefinition(delType);
    }

    private static Type? RuntimeTypeOf(AnalysisContext context, Node node) =>
        ResolveNodeType(context, node)?.GetRuntimeType()
        ?? context.GetResolvedType(node)?.GetRuntimeType();

    private static ITypeDefinition? ResolveParameterType(AnalysisContext context, Parameter parameter) {
        if (parameter.TypeReference is not null) {
            return ResolveNodeType(context, parameter.TypeReference);
        }

        return null;
    }

    private static void BindLambdaArguments(
        AnalysisContext context, Lambda lambda, IReadOnlyList<Node> arguments) {
        int n = Math.Min(lambda.Parameters.Count, arguments.Count);
        for (int i = 0; i < n; i++) {
            var arg = arguments[i];
            var param = lambda.Parameters[i];
            if (arg is Lambda argLambda)
                context.SetMetadata(param, new StoredLambdaMetadata(argLambda));
            else if (context.GetMetadata<StoredLambdaMetadata>(arg) is { } produced)
                context.SetMetadata(param, produced);
        }
    }

    private static void NoteProducedLambda(AnalysisContext context, Node invoke, Node body) {
        var value = YieldNode(body);
        if (value is Lambda lambda)
            context.SetMetadata(invoke, new StoredLambdaMetadata(lambda));
        else if (context.GetMetadata<StoredLambdaMetadata>(value) is { } produced)
            context.SetMetadata(invoke, produced);
    }

    internal static Node YieldNode(Node body) {
        if (body is not Block block || block.Nodes.Count == 0)
            return body;
        var last = block.Nodes[^1];
        if (!IsVoidYieldNode(last))
            return last;
        var fromReturn = FindReturnValueNode(block);
        return fromReturn ?? last;
    }

    private static Node? FindReturnValueNode(Node node) {
        if (node is Lambda)
            return null;
        if (node is IfStatement ifStmt && ifStmt.Condition is Constant { Value: false })
            return ifStmt.ElseBranch is null ? null : FindReturnValueNode(ifStmt.ElseBranch);
        if (node is IfStatement ifTrue && ifTrue.Condition is Constant { Value: true })
            return FindReturnValueNode(ifTrue.ThenBranch);
        if (node is Return { Value: not null } ret)
            return ret.Value;
        foreach (var child in node.Children) {
            if (child is null) continue;
            var found = FindReturnValueNode(child);
            if (found is not null)
                return found;
        }
        return null;
    }
}

/// <summary>The node holds or produces a stored <see cref="Lambda"/> (closure handle).
/// Invoke uses the lambda body type; the binding itself is a heap ref.</summary>
internal sealed record StoredLambdaMetadata(Lambda Lambda) : IAnalysisMetadata;

public static class TypeResolutionMetadataExtensions {
    private sealed record TypeResolutionMetadata : IAnalysisMetadata {
        public ITypeDefinition? ResolvedTypeDefinition { get; set; }
    };

    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseTypeAndMemberResolver() {
            builder.AddAnalyzer(new TypeAndMemberResolver());
            return builder;
        }
    }

    extension(AnalysisContext context) {
        public void SetResolvedType(Node node, ITypeDefinition type) {
            var metadata = context.GetOrAddMetadata(node, static () => new TypeResolutionMetadata());
            metadata.ResolvedTypeDefinition = type;
        }
    }

    extension(INodeMetadataProvider typedMetadataProvider) {
        public ITypeDefinition? GetResolvedType(Node node) {
            return typedMetadataProvider.GetMetadata<TypeResolutionMetadata>(node)?.ResolvedTypeDefinition;
        }
    }
}

// ── Shared resolvers (used by TypeAndMemberResolver) ──

internal static class MethodInvocationSemanticResolver {
    public static ITypeMethod? ResolveMethod(AnalysisContext context, Invoke methodInv) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(methodInv);

        if (methodInv.Delegate is not Member memberAccess)
            return null;

        var targetType = context.GetResolvedType(memberAccess.Value);
        if (targetType == null)
            return null;

        var argumentTypes = new ITypeDefinition[methodInv.Arguments.Length];
        for (var i = 0; i < methodInv.Arguments.Length; i++) {
            var argumentType = context.GetResolvedType(methodInv.Arguments[i]);
            if (argumentType == null)
                return null;

            argumentTypes[i] = argumentType;
        }

        return targetType
            .FindMatchingMethodOverloads(memberAccess.MemberName, argumentTypes)
            .FirstOrDefault();
    }
}

internal static class ConstructorInvocationSemanticResolver {
    public static ITypeConstructor? ResolveConstructor(AnalysisContext context, New @new) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(@new);

        var targetType = context.GetResolvedType(@new.Type);
        if (targetType == null)
            return null;

        var argumentTypes = new ITypeDefinition[@new.Arguments.Length];
        for (var i = 0; i < @new.Arguments.Length; i++) {
            var argumentType = context.GetResolvedType(@new.Arguments[i]);
            if (argumentType == null)
                return null;

            argumentTypes[i] = argumentType;
        }

        return targetType
            .FindMatchingConstructors(argumentTypes)
            .FirstOrDefault();
    }
}

// ── Member resolution metadata (populated by TypeAndMemberResolver) ──

public static class MemberResolutionMetadataExtensions {
    private sealed class MemberResolutionMetadata : IAnalysisMetadata {
        public ITypeMember? ResolvedMember { get; set; }
    };

    extension(AnalysisContext context) {
        public void SetResolvedMember(Node node, ITypeMember member) {
            ArgumentNullException.ThrowIfNull(member);

            var metadata = context.GetOrAddMetadata(node, static () => new MemberResolutionMetadata());
            metadata.ResolvedMember = member;

            context.SetResolvedType(node, member.MemberTypeDefinition);
        }
    }

    extension(INodeMetadataProvider typedMetadataProvider) {
        public ITypeMember? GetResolvedMember(Node node) {
            return typedMetadataProvider.GetMetadata<MemberResolutionMetadata>(node)?.ResolvedMember;
        }
    }
}