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
        if (node is ForLoop loop)
            ApplyForLoopStoredLambdas(context, loop);

        var resolvedType = ResolveNodeType(context, node);

        if (resolvedType != null) {
            context.SetResolvedType(node, resolvedType!);
        }

        this.AnalyzeChildren(context, node);
    }

    /// <summary>Stamp the catch variable's synthetic declaration from the resolved
    /// exception type before the body is analyzed; uses project through the binding.</summary>
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
                if (exType is not null
                    && context.GetMetadata<CatchBindingMetadata>(clause.Body) is { } binding)
                    context.SetResolvedType(binding.Declaration, exType);
                Analyze(context, clause.Body);
            }
        }
        if (tcf.FinallyBlock is not null)
            Analyze(context, tcf.FinallyBlock);
        var tryType = context.GetResolvedType(tcf.TryBlock);
        if (tryType is not null)
            context.SetResolvedType(tcf, tryType);
    }

    /// <summary>True when <paramref name="variable"/> is a use bound to a different
    /// declaration, not the declaration node itself.</summary>
    private static bool IsBoundUse(AnalysisContext context, Variable variable) {
        if (context.GetMetadata<VariableAnalysisMetadata>(variable) is not { } metadata)
            return false;
        return (metadata.VariableReferences.TryGetValue(variable, out var declaration)
                    && declaration is not null
                    && !ReferenceEquals(declaration, variable))
            || metadata.ParameterReferences.ContainsKey(variable);
    }

    /// <summary>A use's type is its declaration's type, found through the binding.
    /// The analyzer stamps the use from this result.</summary>
    private static ITypeDefinition? ResolveVariableType(AnalysisContext context, Variable variable) {
        if (context.GetResolvedType(variable) is { } existing)
            return existing;
        if (context.GetMetadata<VariableAnalysisMetadata>(variable) is not { } metadata)
            return null;
        if (metadata.VariableReferences.TryGetValue(variable, out var declaration)
            && declaration is not null
            && !ReferenceEquals(declaration, variable))
            return ResolveNodeType(context, declaration);
        if (metadata.ParameterReferences.TryGetValue(variable, out var parameter))
            return ResolveNodeType(context, parameter);
        return null;
    }

    /// <summary>True when a write to <paramref name="destination"/> reaches
    /// <paramref name="declaration"/> through the bind pass. The declaration node is
    /// its own binding; every other write is a use the bind pass linked to it.</summary>
    private static bool BindsTo(AnalysisContext context, Variable destination, Variable declaration) {
        if (ReferenceEquals(destination, declaration))
            return true;
        return context.GetMetadata<VariableAnalysisMetadata>(destination) is { } metadata
            && metadata.VariableReferences.TryGetValue(destination, out var binding)
            && ReferenceEquals(binding, declaration);
    }

    /// <summary>Attach a stored lambda's shape to its declaring binding and decide
    /// whether the AST at that binding is the lambda itself. Only a single
    /// unconditional syntactic lambda assignment is quotable; a nested, conditional,
    /// produced, or copied closure keeps the shape for the delegate adapter but is
    /// marked unquotable.</summary>
    private static void ApplyStoredLambdaBinding(
        AnalysisContext context, Node declaringScope, Variable declaration) {
        var (assignments, singleLambda, direct, singleValueIsLambda) =
            CountStoredLambdaAssignments(context, declaringScope, declaration);
        if (singleLambda is null) {
            context.Metadata.Remove<StoredLambdaMetadata>(declaration);
            return;
        }
        context.SetMetadata(declaration, new StoredLambdaMetadata(singleLambda));
        if (assignments != 1 || !direct || !singleValueIsLambda)
            context.SetMetadata(declaration, new UnquotableStoredLambdaMetadata());
    }

    /// <summary>Count the writes that reach <paramref name="declaration"/> across its
    /// declaring scope. <c>Direct</c> is true only for a statement of that scope (a
    /// for-init write counts as direct for the variable the loop declares);
    /// <c>SingleValueIsLambda</c> is true only when the value is syntactically a lambda,
    /// so a produced or copied closure is never quoted.</summary>
    private static (int Assignments, Lambda? SingleLambda, bool Direct, bool SingleValueIsLambda)
        CountStoredLambdaAssignments(
            AnalysisContext context, Node declaringScope, Variable declaration) {
        int assignments = 0;
        Lambda? singleLambda = null;
        bool direct = false;
        bool singleValueIsLambda = false;

        void Visit(Node node, bool isDirectStatement) {
            if (node is ForLoop loop) {
                bool declares = loop.DeclaredVariables?.Contains(declaration) == true;
                foreach (var child in loop.Children) {
                    if (child is not null)
                        Visit(child, isDirectStatement && declares
                            && ReferenceEquals(child, loop.Initializer));
                }
                return;
            }
            if (node is Assignment assignment
                && assignment.Destination is Variable destination
                && BindsTo(context, destination, declaration)) {
                assignments++;
                direct |= isDirectStatement;
                if (assignment.Value is Lambda assigned) {
                    singleLambda = assigned;
                    singleValueIsLambda = true;
                }
                else {
                    if (StoredLambdaBinding.TryGet(context, assignment.Value, out var produced)
                        && produced is not null)
                        singleLambda = produced.Lambda;
                    singleValueIsLambda = false;
                }
            }
            foreach (var child in node.Children) {
                if (child is not null)
                    Visit(child, false);
            }
        }

        if (declaringScope is Block block) {
            foreach (var statement in block.Nodes) {
                if (statement is not null)
                    Visit(statement, true);
            }
        }
        else if (declaringScope is ForLoop forLoop) {
            // Only the initializer is a single unconditional write; the condition and
            // increment run repeatedly, so a lambda written there is not quotable.
            if (forLoop.Initializer is { } initializer)
                Visit(initializer, true);
            if (forLoop.Condition is { } condition)
                Visit(condition, false);
            if (forLoop.Body is { } body)
                Visit(body, false);
            if (forLoop.Increment is { } increment)
                Visit(increment, false);
        }
        else {
            Visit(declaringScope, true);
        }
        return (assignments, singleLambda, direct, singleValueIsLambda);
    }

    /// <summary>A for-init declaration is not a block variable, so its stored lambda's
    /// shape and quotability are attached from its own initializer write.</summary>
    private static void ApplyForLoopStoredLambdas(AnalysisContext context, ForLoop loop) {
        if (loop.DeclaredVariables is not { } declared)
            return;
        foreach (var declaration in declared) {
            if (loop.Initializer is Assignment initializer
                && ReferenceEquals(initializer.Destination, declaration))
                ResolveNodeType(context, initializer.Value);
            ApplyStoredLambdaBinding(context, loop, declaration);
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
            Variable v => ResolveVariableType(context, v),
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
            OptionalTypeReference optional => ResolveNodeType(context, optional.InnerType),
            PrimitiveTypeReference prim => prim.PrimitiveId.GetClrType() is { } clr
                ? context.TypeDefinitions.GetTypeDefinition(clr)
                : null,
            TypeReference typeRef => context.TypeDefinitions.GetTypeDefinition(typeRef.TypeName),
            TypeCast cast => ResolveNodeType(context, cast.TargetTypeReference),
            Default d => d.TargetType is null ? null : ResolveNodeType(context, d.TargetType),
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
        if (loopType != null)
            context.SetResolvedType(foreachLoop.LoopVariable, loopType);

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

        var promoted = leftRank >= rightRank ? leftType : rightType;
        // C# promotes char arithmetic to int (`'a' + 'b'` is 195, and `var x = 'a' + 1`
        // must accept a later int write), so a char result becomes int.
        return promoted.GetRuntimeType() == typeof(char)
            ? context.TypeDefinitions.GetTypeDefinition(typeof(int)) ?? promoted
            : promoted;
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

        return leftType;
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
            // A bound root wins even when its projected type is null (an untyped
            // parameter): do not steal the name for a CLR type. Only an unbound
            // spine is a type name.
            if (DottedTypeNames.TryGetSpine(memberAccess, out _, out var rootVariable)
                && context.GetMetadata<VariableAnalysisMetadata>(rootVariable) is { } bindingMetadata
                && (bindingMetadata.VariableReferences.ContainsKey(rootVariable)
                    || bindingMetadata.ParameterReferences.ContainsKey(rootVariable)))
                return null;
            if (DottedTypeNames.TryResolveType(context, memberAccess, out var clr)) {
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
            if (StoredLambdaBinding.TryGet(context, invoke.Delegate, out var stored)
                && stored is not null) {
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
            // A write to a bound local takes the declaration's type; the use projects
            // through the binding. Only the declaration's own initializer stamps the
            // declared shape.
            if (valueType != null && !IsBoundUse(context, variable))
                context.SetResolvedType(variable, valueType);
            // The shape and its quotability live on the binding, never on this write's
            // destination node. A closure produced or copied by the value keeps the
            // shape for the delegate adapter but is not the AST at this position.
            var binding = StoredLambdaBinding.BindingOf(context, variable);
            if (assignment.Value is Lambda assignedLambda) {
                context.SetMetadata(binding, new StoredLambdaMetadata(assignedLambda));
            }
            else if (StoredLambdaBinding.TryGet(context, assignment.Value, out var produced)
                && produced is not null) {
                context.SetMetadata(binding, produced);
                context.SetMetadata(binding, new UnquotableStoredLambdaMetadata());
            }
            else if (context.GetMetadata<UnquotableStoredLambdaMetadata>(binding) is null) {
                context.Metadata.Remove<StoredLambdaMetadata>(binding);
            }
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
                    break;
                }
            }
            if (resolved != null)
                context.SetResolvedType(v, resolved);
            // A stored lambda's shape and its quotability both live on the
            // declaration; each use projects through the binding.
            ApplyStoredLambdaBinding(context, block, v);
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
            else if (StoredLambdaBinding.TryGet(context, arg, out var produced)
                && produced is not null)
                context.SetMetadata(param, produced);
        }
    }

    private static void NoteProducedLambda(AnalysisContext context, Node invoke, Node body) {
        var value = YieldNode(body);
        if (value is Lambda lambda)
            context.SetMetadata(invoke, new StoredLambdaMetadata(lambda));
        else if (StoredLambdaBinding.TryGet(context, value, out var produced)
            && produced is not null)
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

/// <summary>Marks a binding whose lambda is reassigned or conditionally assigned. The
/// closure handle is still the value at a use, so delegate wrapping is sound, but the
/// AST is not that value, so an <c>Expression&lt;D&gt;</c> quote must fail closed.</summary>
internal sealed record UnquotableStoredLambdaMetadata : IAnalysisMetadata;

/// <summary>The one rule for reading a stored lambda off a node: metadata lives on the
/// binding the bind pass linked a use to, never on the use itself. A parameter binding
/// is never quotable — it has no write history to prove the AST is its value.</summary>
internal static class StoredLambdaBinding {
    /// <summary>The declaration a node is bound to: a use projects through the bind
    /// pass; a declaration, parameter, or any other node is its own binding.</summary>
    public static Node BindingOf(INodeMetadataProvider provider, Node node) {
        if (node is not Variable use)
            return node;
        if (provider.GetMetadata<VariableAnalysisMetadata>(use) is not { } metadata)
            return use;
        if (metadata.VariableReferences.TryGetValue(use, out var declaration)
            && declaration is not null
            && !ReferenceEquals(declaration, use))
            return declaration;
        if (metadata.ParameterReferences.TryGetValue(use, out var parameter))
            return parameter;
        return use;
    }

    /// <summary>True when the node holds a stored module lambda. Quotable only when the
    /// binding is a local declaration whose AST really is that lambda.</summary>
    public static bool TryGet(
        INodeMetadataProvider provider, Node node, out StoredLambdaMetadata? stored) {
        var binding = BindingOf(provider, node);
        stored = provider.GetMetadata<StoredLambdaMetadata>(binding);
        return stored is not null;
    }

    /// <summary>True when the node's stored lambda is the AST value at that position,
    /// so an <c>Expression&lt;D&gt;</c> can quote it. Only a local declaration qualifies:
    /// a parameter has no write history, and a produced closure is a call result.</summary>
    public static bool IsQuotable(INodeMetadataProvider provider, Node node) {
        var binding = BindingOf(provider, node);
        return binding is Variable
            && provider.GetMetadata<StoredLambdaMetadata>(binding) is not null
            && provider.GetMetadata<UnquotableStoredLambdaMetadata>(binding) is null;
    }
}

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
        // A lambda argument's natural type is its Func/Action shape, not the
        // delegate or Expression<D> the parameter declares. Ordinary matching may
        // find nothing, or may bind the lambda to a wider non-delegate parameter
        // (object). The bridge's rule wins whenever the ordinary winner cannot take
        // the lambda as a delegate whose parameter list agrees.
        bool hasLambdaArgument = methodInv.Arguments.Any(argument =>
            argument is Lambda || StoredLambdaBinding.TryGet(context, argument, out _));
        if (hasLambdaArgument
            && (matches.Count == 0
                || !LambdaArgumentsAreDelegates(context, matches[0], methodInv.Arguments))) {
            var bridged = FindLambdaDelegateOverloads(
                context, targetType, memberAccess.MemberName, methodInv.Arguments).ToList();
            if (bridged.Count > 0)
                matches = bridged;
        }
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

    /// <summary>True when every lambda-shaped argument of <paramref name="method"/> is
    /// accepted by its parameter as a delegate or <c>Expression&lt;D&gt;</c>.</summary>
    private static bool LambdaArgumentsAreDelegates(
        AnalysisContext context, ITypeMethod method, IReadOnlyList<Node> arguments) {
        var parameters = method.Parameters?.ToArray() ?? [];
        for (int i = 0; i < arguments.Count && i < parameters.Length; i++) {
            var shape = arguments[i] as Lambda
                ?? (StoredLambdaBinding.TryGet(context, arguments[i], out var stored)
                    ? stored!.Lambda
                    : null);
            if (shape is null)
                continue;
            if (!AcceptsLambdaArgument(context, shape, parameters[i].ParameterTypeDefinition))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Best-scoring overload when one or more arguments are lambdas bound to a
    /// delegate or <c>Expression&lt;D&gt;</c> parameter. Parameter count and
    /// parameter types must agree; the return type is checked by the emitter so a
    /// value body against a void delegate fails with the bridge's own diagnostic.
    /// </summary>
    private static IEnumerable<ITypeMethod> FindLambdaDelegateOverloads(
        AnalysisContext context,
        ITypeDefinition targetType,
        string name,
        IReadOnlyList<Node> arguments) {
        int? bestScore = null;
        var best = new List<ITypeMethod>();
        foreach (var candidate in targetType.Methods.WithName(name)) {
            int? score = LambdaOverloadScore(context, candidate, arguments);
            if (score is null)
                continue;
            if (bestScore is null || score < bestScore) {
                bestScore = score;
                best = [candidate];
            }
            else if (score == bestScore) {
                best.Add(candidate);
            }
        }
        return best;
    }

    private static int? LambdaOverloadScore(
        AnalysisContext context, ITypeMethod candidate, IReadOnlyList<Node> arguments) {
        var parameters = candidate.Parameters?.ToArray() ?? [];
        if (arguments.Count > parameters.Length)
            return null;
        for (int i = arguments.Count; i < parameters.Length; i++) {
            if (!parameters[i].IsOptional)
                return null;
        }

        var score = 0;
        for (int i = 0; i < arguments.Count; i++) {
            var argument = arguments[i];
            var parameterType = parameters[i].ParameterTypeDefinition;
            var shape = argument as Lambda
                ?? (StoredLambdaBinding.TryGet(context, argument, out var argumentStored)
                    ? argumentStored!.Lambda
                    : null);
            if (shape is not null) {
                // A lambda's Func/Action type is not its meaning: only a delegate or
                // Expression<D> parameter receives it, never a wider object parameter.
                if (!AcceptsLambdaArgument(context, shape, parameterType))
                    return null;
                score += 2;
                continue;
            }
            var argumentType = context.GetResolvedType(argument);
            if (argumentType is not null
                && (ReferenceEquals(parameterType, argumentType)
                    || parameterType.IsAssignableFrom(argumentType))) {
                if (!ReferenceEquals(parameterType, argumentType))
                    score++;
                continue;
            }
            else
                return null;
        }
        return score;
    }

    /// <summary>
    /// True when <paramref name="parameterType"/> is a delegate <c>D</c> or
    /// <c>Expression&lt;D&gt;</c> whose Invoke parameter list matches the lambda's
    /// declared parameter list. Untyped lambda parameters take the delegate's types.
    /// </summary>
    internal static bool AcceptsLambdaArgument(
        AnalysisContext context, Lambda lambda, ITypeDefinition parameterType) {
        var parameterClr = parameterType.GetRuntimeType();
        if (parameterClr is null)
            return false;
        if (!DelegateShape.TryGet(parameterClr, out var delegateType, out _))
            return false;
        var invoke = delegateType.GetMethod(nameof(Action.Invoke));
        if (invoke is null)
            return false;
        var delegateParameters = invoke.GetParameters();
        if (delegateParameters.Length != lambda.Parameters.Count)
            return false;
        for (int i = 0; i < delegateParameters.Length; i++) {
            var declared = DeclaredParameterType(context, lambda.Parameters[i]);
            if (declared is not null && declared != delegateParameters[i].ParameterType)
                return false;
        }
        return true;
    }

    private static Type? DeclaredParameterType(AnalysisContext context, Parameter parameter) =>
        ClrTypeResolution.Resolve(
            parameter.TypeReference, parameter, context.GetResolvedType);
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