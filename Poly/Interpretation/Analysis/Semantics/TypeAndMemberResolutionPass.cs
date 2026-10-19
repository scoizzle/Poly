namespace Poly.Interpretation.Analysis.Semantics;

using Poly.Ast.Nodes;
using Poly.Introspection.CommonLanguageRuntime;

/// <summary>Resolves both types and member references for all AST nodes.
/// Merged from the former separate TypeResolver + MemberResolver passes
/// to avoid duplicate tree walks — both call the same resolvers for
/// Invoke, New, Member, and IndexAccess nodes.</summary>
internal sealed class TypeAndMemberResolver : INodeAnalyzer {
    public const string Id = "TypeAndMember";
    public string PassName => Id;
    public void Analyze(AnalysisContext context, Node node) {
        if (node is TryCatchFinally tcf) {
            AnalyzeTryCatchFinally(context, tcf);
            return;
        }

        // Seed parameter uses before the body resolves so a typed lambda
        // parameter supports member access (e.g. ((string s) => s.Length)).
        if (node is Lambda lambda)
            SeedLambdaParameterTypes(context, lambda);

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
                    SeedVariableType(context, clause.Body, clause.VariableName!, exType);
                Analyze(context, clause.Body);
            }
        }
        if (tcf.FinallyBlock is not null)
            Analyze(context, tcf.FinallyBlock);
        var tryType = context.GetResolvedType(tcf.TryBlock);
        if (tryType is not null)
            context.SetResolvedType(tcf, tryType);
    }

    /// <summary>Seeds <paramref name="type"/> onto every <see cref="Variable"/> use of
    /// <paramref name="name"/> in <paramref name="node"/>'s subtree, stopping at a nested
    /// block that declares the name (shadowing). A local's type lives on its declaration
    /// node, while each use is a distinct node — and member resolution needs the *use's*
    /// type — so the declaring scope seeds its uses before the body is walked.</summary>
    private static void SeedVariableType(
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
                SeedVariableType(context, child, name, type);
        }
    }

    /// <summary>Seed the resolved types of a lambda's parameter uses before its
    /// body is analyzed, so member access on a typed parameter resolves
    /// (<c>((string s) =&gt; s.Length)</c>). The reader emits a fresh
    /// <see cref="Variable"/> per identifier, so the name is the only link.
    /// A block that declares the same name shadows the parameter and is left
    /// alone; nested lambdas seed their own parameters.</summary>
    private static void SeedLambdaParameterTypes(AnalysisContext context, Lambda lambda) {
        if (lambda.Parameters.Count == 0)
            return;
        var byName = new Dictionary<string, ITypeDefinition>(StringComparer.Ordinal);
        foreach (var parameter in lambda.Parameters) {
            if (parameter.TypeReference is null)
                continue;
            if (ResolveNodeType(context, parameter.TypeReference) is { } type)
                byName[parameter.Name] = type;
        }
        if (byName.Count == 0)
            return;
        SeedParameterVariableTypes(context, lambda.Body, byName);
    }

    private static void SeedParameterVariableTypes(
        AnalysisContext context, Node node, IReadOnlyDictionary<string, ITypeDefinition> byName) {
        if (node is Lambda)
            return;
        if (node is Variable v && byName.TryGetValue(v.Name, out var type))
            context.SetResolvedType(v, type);
        if (node is Block block) {
            foreach (var bv in block.Variables) {
                if (bv is Variable dv && byName.ContainsKey(dv.Name))
                    return;
            }
        }
        foreach (var child in node.Children) {
            if (child is not null)
                SeedParameterVariableTypes(context, child, byName);
        }
    }

    private static ITypeDefinition? ResolveNodeType(AnalysisContext context, Node node) {
        // A node may have been rewritten to a type reference (a dotted CLR type name folded
        // as a member chain); resolve through the replacement, as the emitter does.
        if (context.GetNodeReplacement(node) is { } replaced && replaced != node)
            return ResolveNodeType(context, replaced);
        return node switch {
            Constant c => context.TypeDefinitions.GetTypeDefinition(c.Value?.GetType() ?? typeof(object)),
            InterpolatedString => context.TypeDefinitions.GetTypeDefinition(typeof(string)),
            ThisReference @this => ResolveThisReferenceType(context, @this),
            Parameter p => ResolveParameterType(context, p),
            Variable v => context.GetResolvedType(v),
            Add add => ResolveNumericArithmeticType(context, add.LeftHandValue, add.RightHandValue),
            Subtract sub => ResolveNumericArithmeticType(context, sub.LeftHandValue, sub.RightHandValue),
            Multiply mul => ResolveNumericArithmeticType(context, mul.LeftHandValue, mul.RightHandValue),
            Divide div => ResolveNumericArithmeticType(context, div.LeftHandValue, div.RightHandValue),
            Modulo mod => ResolveNumericArithmeticType(context, mod.LeftHandValue, mod.RightHandValue),
            UnaryMinus minus => UnwrapOptionalScalar(context, ResolveNodeType(context, minus.Operand)),
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
            OptionalTypeReference optional => ResolveNodeType(context, optional.InnerType),
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
            BitwiseNot bn => UnwrapOptionalScalar(context, ResolveNodeType(context, bn.Operand)),
            UsingStatement => null,
            TryCatchFinally tcf => ResolveNodeType(context, tcf.TryBlock),
            SwitchStatement swt => swt.Cases.Count > 0
                ? ResolveNodeType(context, swt.Cases[0].Body)
                : swt.DefaultCase is not null ? ResolveNodeType(context, swt.DefaultCase) : null,
            Await aw => ResolveAwaitType(context, aw),
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

    /// <summary>The type of an <c>await</c> is the awaiter's <c>GetResult()</c>.
    /// A non-awaitable operand, including one whose awaiter has no <c>IsCompleted</c>
    /// and no completion method, resolves to null.</summary>
    private static ITypeDefinition? ResolveAwaitType(AnalysisContext context, Await await) {
        var operandType = ResolveNodeType(context, await.Operand);
        if (operandType is null)
            return null;

        var binding = AwaitPattern.TryBind(operandType);
        if (binding is null)
            return null;

        var result = binding.GetResult.ReturnType;
        if (result == typeof(void))
            return context.TypeDefinitions.GetTypeDefinition(typeof(void));
        return context.TypeDefinitions.GetTypeDefinition(result);
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

        // Publish the element type so the compatibility check and the emitter agree on it.
        context.SetMetadata(foreachLoop, new ForEachElementTypeMetadata(elementType));

        // A declared loop type wins over the element type (`foreach (double d in ints)`).
        ITypeDefinition? declaredType = null;
        if (foreachLoop.DeclaredType is not null) {
            declaredType = ResolveNodeType(context, foreachLoop.DeclaredType);
            if (declaredType is null)
                return context.TypeDefinitions.GetTypeDefinition(typeof(void));
            context.SetResolvedType(foreachLoop.DeclaredType, declaredType);
        }

        var loopType = declaredType ?? elementType;
        if (loopType != null) {
            context.SetResolvedType(foreachLoop.LoopVariable, loopType);
            SeedVariableType(context, foreachLoop.Body, foreachLoop.LoopVariable.Name, loopType);
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
            return UnwrapOptionalScalar(context, leftType);

        var promoted = leftRank >= rightRank ? leftType : rightType;
        // An optional scalar operand is unboxed by the operation, so the result is the
        // underlying scalar slot, not a nullable handle.
        promoted = UnwrapOptionalScalar(context, promoted) ?? promoted;
        // C# promotes char arithmetic to int (`'a' + 'b'` is 195, and `var x = 'a' + 1`
        // must accept a later int write), so a char result becomes int.
        return promoted.GetRuntimeType() == typeof(char)
            ? context.TypeDefinitions.GetTypeDefinition(typeof(int)) ?? promoted
            : promoted;
    }

    /// <summary>An optional scalar (`long?`/`bool?`) is unboxed by an operation, so a numeric
    /// result is its underlying scalar slot, not a nullable handle.</summary>
    private static ITypeDefinition? UnwrapOptionalScalar(AnalysisContext context, ITypeDefinition? type) {
        var runtime = type?.GetRuntimeType();
        var underlying = runtime is null ? null : Nullable.GetUnderlyingType(runtime);
        if (underlying is null || (underlying != typeof(long) && underlying != typeof(bool)))
            return type;
        return context.TypeDefinitions.GetTypeDefinition(underlying) ?? type;
    }

    private static int? GetNumericRank(ITypeDefinition type) => type.PrimitiveType switch {
        PrimitiveType.Char => 1,
        PrimitiveType.Int8 => 2,
        PrimitiveType.Int16 => 3,
        PrimitiveType.UInt8 => 4,
        PrimitiveType.UInt16 => 5,
        PrimitiveType.Int32 => 6,
        PrimitiveType.UInt32 => 7,
        PrimitiveType.Int64 => 8,
        PrimitiveType.UInt64 => 9,
        PrimitiveType.Float32 => 10,
        PrimitiveType.Float64 => 11,
        PrimitiveType.Decimal => 12,
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

        return UnwrapOptionalScalar(context, leftType);
    }

    private static ITypeDefinition? ResolveMemberAccessType(
        AnalysisContext context,
        Member memberAccess) {
        var instanceType = ResolveNodeType(context, memberAccess.Value);
        if (instanceType == null) {
            // A dotted CLR type name (`System.String`, `System.Threading.Tasks.Task`) is
            // folded as a member chain over an identifier. Resolve the dotted name; on a
            // CLR type hit, rewrite this node to the type so the access above it is a
            // static member access.
            if (DottedNameSpine(memberAccess) is { } dotted
                && context.TypeDefinitions.GetTypeDefinition(dotted) is ClrTypeDefinition clr) {
                // Rewrite this member node to the type; the access above it becomes a
                // static member access (the emitter follows the replacement).
                var typeRef = new ClrTypeReference(clr.RuntimeType);
                context.SetNodeReplacement(memberAccess, typeRef);
                context.SetResolvedType(memberAccess, clr);
                return clr;
            }
            return null;
        }

        var member = instanceType.Members.WithName(memberAccess.MemberName).FirstOrDefault();
        if (member is null)
            context.ReportError(memberAccess, $"Type '{instanceType.Name}' does not contain a member named '{memberAccess.MemberName}'.");
        else
            context.SetResolvedMember(memberAccess, member);
        return member?.MemberTypeDefinition;
    }

    /// <summary>Flatten a member chain over an identifier (`A.B.C`) to the dotted name it
    /// spells; null when the chain is not a pure identifier spine.</summary>
    private static string? DottedNameSpine(Node node) {
        var names = new List<string>();
        var current = node;
        while (current is Member member) {
            names.Insert(0, member.MemberName);
            current = member.Value;
        }
        if (current is not Variable root)
            return null;
        names.Insert(0, root.Name);
        return string.Join(".", names);
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

    /// <summary>
    /// An assignment from <c>Method</c> whose declared return type is
    /// <c>DomainResult&lt;T&gt;</c> remembers <c>T</c> on the result variable.
    /// </summary>
    private static void NoteDomainResultValue(AnalysisContext context, Assignment assignment) {
        if (assignment.Destination is not Variable variable)
            return;
        if (assignment.Value is not Invoke invoke)
            return;
        if (context.GetResolvedMember(invoke) is not AstMethodDefinition method)
            return;
        if (method.DefinitionNode.ReturnType is not NamedTypeReference {
            TypeName: "DomainResult",
            TypeArguments: [var typeArgument]
        })
            return;
        var closed = ResolveNodeType(context, typeArgument);
        if (closed is not null)
            context.SetMetadata(variable, new DomainResultValueMetadata(closed));
    }

    /// <summary>
    /// <c>result.Value</c> after a <c>DomainResult&lt;T&gt;</c> call is <c>T</c>,
    /// so members of <c>T</c> resolve. The CLR property stays <c>object</c>.
    /// </summary>
    private static ITypeDefinition? DomainResultValueType(AnalysisContext context, Node value) {
        if (value is not Member { MemberName: "Value", Value: Variable result })
            return null;
        return context.GetMetadata<DomainResultValueMetadata>(result)?.Type;
    }

    private static ITypeDefinition? ResolveAssignmentType(
        AnalysisContext context,
        Assignment assignment) {
        var valueType = ResolveNodeType(context, assignment.Value);
        NoteDomainResultValue(context, assignment);
        valueType = DomainResultValueType(context, assignment.Value) ?? valueType;

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
                    NoteDomainResultValue(context, a);
                    resolved = DomainResultValueType(context, a.Value) ?? resolved;
                    if (a.Value is Lambda assignedLambda)
                        context.SetMetadata(v, new StoredLambdaMetadata(assignedLambda));
                    else if (context.GetMetadata<StoredLambdaMetadata>(a.Value) is { } produced)
                        context.SetMetadata(v, produced);
                    break;
                }
            }
            if (resolved != null) {
                context.SetResolvedType(v, resolved);
                // Member access on a local (`s.Length`, `s?.Length`) resolves against the
                // use's type, and a use is a distinct node from this declaration, so seed
                // the declared type over the block's statements. Seed each statement, not
                // the block itself: the seeder stops at a block that declares the name,
                // which would be this one.
                foreach (var statement in nodes) {
                    if (statement is not null)
                        SeedVariableType(context, statement, v.Name, resolved);
                }
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

/// <summary>
/// <c>DomainResult&lt;T&gt;</c> declared on an AST method. Closing that name
/// hits the non-generic CLR result, whose <c>Value</c> is object. The
/// variable that received the call still carries <c>T</c>.
/// </summary>
internal sealed record DomainResultValueMetadata(ITypeDefinition Type) : IAnalysisMetadata;

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

        var matches = targetType
            .FindMatchingMethodOverloads(memberAccess.MemberName, argumentTypes)
            .ToList();
        // A type-name receiver (`Person.Create(name)`) binds the static factory.
        // An instance host job of the same arity stays for `this.Create(...)`.
        // When no static overload matches, the instance method remains so the
        // VM still rejects an instance call on a type name.
        if (matches.Count > 1 && IsTypeName(context.GetNodeReplacement(memberAccess.Value) ?? memberAccess.Value)) {
            var staticMatch = matches.FirstOrDefault(static method => method.IsStatic);
            if (staticMatch is not null)
                return staticMatch;
        }
        return matches.FirstOrDefault();
    }

    private static bool IsTypeName(Node node) => node is
        NamedTypeReference or TypeReference or PrimitiveTypeReference or TypeDefinitionReference;
}

internal static class ConstructorInvocationSemanticResolver {
    public static ITypeConstructor? ResolveConstructor(AnalysisContext context, New @new) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(@new);

        var targetType = context.GetResolvedType(@new.Type);
        if (targetType == null)
            return null;

        var argumentTypes = new ITypeDefinition?[@new.Arguments.Length];
        var incomplete = false;
        for (var i = 0; i < @new.Arguments.Length; i++) {
            // A null literal's resolved type is object, which rejects string? / entity?.
            if (@new.Arguments[i] is Constant { Value: null }) {
                argumentTypes[i] = null;
                incomplete = true;
                continue;
            }

            argumentTypes[i] = context.GetResolvedType(@new.Arguments[i]);
            if (argumentTypes[i] is null)
                incomplete = true;
        }

        if (!incomplete) {
            var complete = new ITypeDefinition[argumentTypes.Length];
            for (var i = 0; i < argumentTypes.Length; i++)
                complete[i] = argumentTypes[i]!;
            return targetType.FindMatchingConstructors(complete).FirstOrDefault();
        }

        return MatchIncompleteConstructor(targetType, @new, argumentTypes);
    }

    /// <summary>
    /// Member invoke already treats a null argument type as incomplete and skips the error.
    /// Constructors still have to bind: an exact-arity ctor whose known args are assignable,
    /// and whose null literals fit a reference or <c>Nullable&lt;T&gt;</c> parameter.
    /// </summary>
    private static ITypeConstructor? MatchIncompleteConstructor(
        ITypeDefinition targetType, New @new, ITypeDefinition?[] argumentTypes) {
        ITypeConstructor? best = null;
        var bestScore = int.MaxValue;
        var ties = 0;
        foreach (var constructor in targetType.Constructors) {
            if (!TryScoreConstructor(constructor, @new, argumentTypes, out var score))
                continue;
            if (score < bestScore) {
                bestScore = score;
                best = constructor;
                ties = 1;
            }
            else if (score == bestScore) {
                ties++;
            }
        }

        return ties == 1 ? best : null;
    }

    private static bool TryScoreConstructor(
        ITypeConstructor constructor, New @new, ITypeDefinition?[] argumentTypes, out int score) {
        score = 0;
        var parameters = constructor.Parameters?.ToArray() ?? [];
        if (!SyntaxTypeCompatibilityAnalyzer.CallFits(parameters, argumentTypes.Length))
            return false;

        if (parameters.Length != argumentTypes.Length)
            score++;

        for (var i = 0; i < argumentTypes.Length; i++) {
            var paramType = parameters[i].ParameterTypeDefinition;
            if (@new.Arguments[i] is Constant { Value: null }) {
                if (!SyntaxTypeCompatibilityAnalyzer.AcceptsNull(paramType))
                    return false;
                score++;
                continue;
            }

            var argType = argumentTypes[i];
            // A non-literal argument with no resolved type is incomplete, same as
            // a member invoke. It does not reject the constructor and it does not
            // change the score. Two constructors that both accept the known args
            // still tie and bind nothing.
            if (argType is null)
                continue;
            if (paramType == argType)
                continue;
            if (paramType.IsAssignableFrom(argType)) {
                score++;
                continue;
            }

            return false;
        }

        return true;
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

/// <summary>The collection element type resolved for a <see cref="ForEachLoop"/>, published
/// so the compatibility check and the emitter use one derivation.</summary>
internal sealed record ForEachElementTypeMetadata(ITypeDefinition? ElementType) : IAnalysisMetadata;