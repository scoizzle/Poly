using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;

using Poly.Ast.Nodes;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Abi;

public static partial class AbiEmitter {
    /// <summary>
    /// A module <see cref="Lambda"/> passed where a CLR parameter expects a delegate is
    /// not a VM closure value. Emit a real nested <see cref="LambdaExpression"/> in the
    /// call argument: a delegate-typed parameter receives the compiled delegate (the
    /// outer <c>Compile()</c> compiles it), an <c>Expression&lt;D&gt;</c>-typed parameter
    /// receives the tree. Captured ring/heap values are spilled to typed locals before
    /// the tree, so the tree is pure and a query provider sees closure fields.
    /// </summary>
    /// <remarks>
    /// The delegate closes over the live <see cref="VmState"/>, so it is valid while
    /// that state is alive. The thread that invokes it is not policed; the supported
    /// off-thread pattern is the parked call (the VM thread is blocked inside the CLR
    /// call that owns the delegate), and concurrent use of one state is the caller's
    /// responsibility. A host delegate forwarded through a module parameter is invoked
    /// directly rather than re-entered.
    /// </remarks>
    private static bool TryBuildLambdaBridgeArgument(
        Node argument,
        Type? parameterType,
        Context ctx,
        out Expression prelude,
        out Expression callArgument) {
        prelude = Empty();
        callArgument = null!;

        if (!DelegateShape.TryGet(parameterType, out var delegateType, out _))
            return false;
        if (delegateType.ContainsGenericParameters)
            throw new InvalidOperationException(
                $"VM compile rejected: open generic delegate '{delegateType}' is not supported by the lambda bridge.");

        var invokeMethod = delegateType.GetMethod(nameof(Action.Invoke))
            ?? throw new InvalidOperationException(
                $"VM compile rejected: delegate '{delegateType}' has no Invoke method.");
        bool isExpression = DelegateShape.IsExpressionOfDelegate(parameterType, out var expressionDelegate)
            && expressionDelegate == delegateType;

        if (argument is Lambda literal) {
            CheckDelegateShape(literal, delegateType, invokeMethod, ctx);
            return isExpression
                ? BuildExpressionArgument(literal, delegateType, invokeMethod, ctx, out prelude, out callArgument)
                : BuildDelegateArgument(literal, null, delegateType, invokeMethod, ctx, out prelude, out callArgument);
        }

        // A non-literal argument: a stored module lambda (metadata on the binding),
        // a forwarded module parameter (a runtime closure handle), or a real CLR
        // delegate/Expression object supplied by the host.
        bool hasStored = TryResolveStoredLambda(
            argument, ctx, out var stored, out bool quotable, out var handle);

        if (isExpression) {
            // A forwarded Expression<D> parameter or a host Expression object is the
            // value the parameter wants; stale binding metadata must not quote over it.
            if (ArgumentAlreadyExpression(argument, parameterType!, ctx))
                return false;
            if (stored is not null) {
                if (!quotable) {
                    bool forwarded = ctx.Analysis is { } bindingAnalysis
                        && StoredLambdaBinding.BindingOf(bindingAnalysis, argument) is Parameter;
                    throw new InvalidOperationException(forwarded
                        ? $"VM compile rejected: a lambda forwarded through a parameter cannot be quoted for '{parameterType!.Name}'; pass the lambda at the Expression<> boundary or declare the forwarding parameter as '{parameterType.Name}'."
                        : $"VM compile rejected: lambda bound to a reassigned or conditional variable cannot be quoted for '{parameterType!.Name}'; pass the lambda at the Expression<> boundary.");
                }
                if (!CapturesResolve(stored.Lambda, ctx)) {
                    throw new InvalidOperationException(
                        $"VM compile rejected: stored lambda '{stored.Lambda}' captures bindings that are not in scope here; pass the lambda at the '{parameterType!.Name}' boundary.");
                }
                return BuildExpressionArgument(stored.Lambda, delegateType, invokeMethod, ctx, out prelude, out callArgument);
            }
            if (hasStored) {
                throw new InvalidOperationException(
                    $"VM compile rejected: a lambda forwarded through a delegate parameter cannot be quoted for '{parameterType!.Name}'; pass the lambda at the Expression<> boundary or declare the forwarding parameter as '{parameterType.Name}'.");
            }
            // A closure produced by a call is not the AST at this position either.
            if (ctx.Analysis is { } producedAnalysis
                && StoredLambdaBinding.TryGet(producedAnalysis, argument, out _)) {
                throw new InvalidOperationException(
                    $"VM compile rejected: a closure produced by an expression cannot be quoted for '{parameterType!.Name}'; pass the lambda at the Expression<> boundary.");
            }
            // A real host Expression<D> object — a method or member result, or null —
            // is already the CLR value the parameter wants.
            return false;
        }

        if (hasStored) {
            if (stored is not null)
                CheckDelegateShape(stored.Lambda, delegateType, invokeMethod, ctx);
            else
                CheckForwardedDelegateShape(invokeMethod);
            return BuildDelegateArgument(stored?.Lambda, handle, delegateType, invokeMethod, ctx, out prelude, out callArgument);
        }

        // A real delegate object: the normal ToClr path derefs and passes it.
        return false;
    }

    /// <summary>Arity, by-ref, and declared-parameter checks for a lambda against a
    /// delegate's Invoke signature.</summary>
    private static void CheckDelegateShape(
        Lambda lambda, Type delegateType, MethodInfo invokeMethod, Context ctx) {
        var delegateParameters = invokeMethod.GetParameters();
        if (lambda.Parameters.Count != delegateParameters.Length) {
            throw new InvalidOperationException(
                $"VM compile rejected: lambda has {lambda.Parameters.Count} parameter(s) but delegate '{delegateType.Name}' expects {delegateParameters.Length}.");
        }
        for (int i = 0; i < delegateParameters.Length; i++) {
            if (delegateParameters[i].ParameterType.IsByRef)
                throw new InvalidOperationException(
                    $"VM compile rejected: delegate parameter '{delegateParameters[i].Name}' is by-ref; the lambda bridge supports value parameters only.");
            var declared = ParameterClrType(lambda.Parameters[i], ctx);
            if (declared is not null && declared != delegateParameters[i].ParameterType) {
                throw new InvalidOperationException(
                    $"VM compile rejected: lambda parameter '{lambda.Parameters[i].Name}' is '{declared.Name}' but delegate '{delegateType.Name}' expects '{delegateParameters[i].ParameterType.Name}'.");
            }
        }
    }

    /// <summary>A forwarded delegate has no lambda AST to check, but a by-ref Invoke
    /// signature still cannot be expressed, so reject it before building the parameter.</summary>
    private static void CheckForwardedDelegateShape(MethodInfo invokeMethod) {
        foreach (var parameter in invokeMethod.GetParameters()) {
            if (parameter.ParameterType.IsByRef) {
                throw new InvalidOperationException(
                    $"VM compile rejected: delegate parameter '{parameter.Name}' is by-ref; the lambda bridge supports value parameters only.");
            }
        }
    }

    private static bool IsExpressionBridgeParameter(Type? type) =>
        DelegateShape.IsExpressionOfDelegate(type, out _);

    /// <summary>
    /// Resolve a non-literal argument to the value that holds the module lambda:
    /// metadata on the binding for a stored lambda, or the frame/inline slot for a
    /// forwarded module parameter. Returns false when the argument is not a module
    /// closure (a real host delegate/Expression object).
    /// </summary>
    private static bool TryResolveStoredLambda(
        Node argument, Context ctx, out StoredLambdaMetadata? stored, out bool quotable, out Expression handle) {
        stored = null;
        quotable = true;
        handle = null!;

        var analysis = ctx.Analysis;
        Node? binding = analysis is null ? null : StoredLambdaBinding.BindingOf(analysis, argument);

        // A closure produced by a call is not a binding: its value is a fresh handle,
        // so the normal compile path must evaluate the call.
        if (binding is not (Poly.Ast.Nodes.Variable or Poly.Ast.Nodes.Parameter))
            return false;

        if (analysis is not null
            && StoredLambdaBinding.TryGet(analysis, argument, out var bindingMetadata)
            && bindingMetadata is not null) {
            stored = bindingMetadata;
            quotable = StoredLambdaBinding.IsQuotable(analysis, argument);
            handle = ReadBindingValue(binding, ctx);
            return true;
        }

        // A reader emits a Variable for a module parameter use. The bind pass links
        // that use to the parameter; an unbound use is not a stored lambda.
        if (argument is Variable use) {
            if (ctx.BoundParameter(use) is { } boundParameter) {
                if (ctx.TryGetCapture(use, out int captureIndex)) {
                    handle = EmitCaptureCellValue(ctx, captureIndex);
                    return true;
                }
                if (TryReadParameter(boundParameter, ctx, out handle))
                    return true;
                throw new InvalidOperationException(
                    $"VM compile rejected: bound parameter '{boundParameter.Name}' is not in any scope here.");
            }
        }
        // A directly built Parameter use resolves through the emitter's parameter
        // maps; the runtime dispatch accepts a host delegate or a VM closure.
        if (argument is Parameter parameterUse
            && (ctx.TryGetParameterSlot(parameterUse, out _)
                || ctx.TryGetInlineParameter(parameterUse, out _))) {
            handle = ParameterValue(parameterUse, ctx);
            return true;
        }
        return false;
    }

    /// <summary>Read a bound parameter's live slot: the inline ring slot when it is
    /// passed inline, otherwise the frame parameter.</summary>
    private static bool TryReadParameter(Parameter parameter, Context ctx, out Expression handle) {
        if (ctx.TryGetInlineParameter(parameter, out int ringSlot)) {
            handle = ctx.RingVar(ringSlot);
            return true;
        }
        if (ctx.TryGetParameterSlot(parameter, out int paramIndex)) {
            handle = ctx.ParameterRead(paramIndex);
            return true;
        }
        handle = null!;
        return false;
    }

    /// <summary>A reader emits a Variable for a module parameter use; the bind pass
    /// links it to the Parameter, and the emitter can read that parameter's live slot
    /// or capture cell by identity.</summary>
    private static bool ParameterCaptureResolves(Variable use, Context ctx) =>
        ctx.BoundParameter(use) is { } parameter
        && (ctx.TryGetCapture(use, out _)
            || ctx.TryGetInlineParameter(parameter, out _)
            || ctx.TryGetParameterSlot(parameter, out _));

    /// <summary>Read a stored binding's current handle: its capture cell when this
    /// frame captured it, otherwise the variable/parameter slot.</summary>
    private static Expression ReadBindingValue(Node binding, Context ctx) => binding switch {
        Variable v => ctx.TryGetCapture(v, out int capIndex)
            ? EmitCaptureCellValue(ctx, capIndex)
            : ctx.VariableRead(v),
        Parameter p => ctx.TryGetCapture(p, out int capIndex)
            ? EmitCaptureCellValue(ctx, capIndex)
            : ParameterValue(p, ctx),
        _ => throw new InvalidOperationException(
            "VM compile rejected: lambda bridge binding is not a variable or parameter."),
    };

    /// <summary>True when a stored lambda's captures are all resolvable in this frame
    /// (required before quoting its body at the use site).</summary>
    private static bool CapturesResolve(Lambda lambda, Context ctx) {
        foreach (var capture in FindBodyCaptures(lambda, ctx.Analysis)) {
            if (capture.Variable is { } variable) {
                if (!ctx.IsDeclared(variable) && !ctx.TryGetCapture(variable, out _)
                    && !ParameterCaptureResolves(variable, ctx))
                    return false;
            }
            else if (capture.Parameter is { } parameter) {
                if (!ctx.TryGetParameterSlot(parameter, out _)
                    && !ctx.TryGetCapture(parameter, out _)
                    )
                    return false;
            }
        }
        return true;
    }

    /// <summary>True when the argument already carries the CLR value the parameter
    /// wants (`Expression&lt;D&gt;`), as a forwarded parameter or a host object.</summary>
    private static bool ArgumentAlreadyExpression(Node argument, Type parameterType, Context ctx) {
        // A module closure's declared type is its delegate shape, not the Expression<D>
        // the parameter wants; it must take the stored/forwarded path, never pass through.
        if (ctx.Analysis is { } analysis && StoredLambdaBinding.TryGet(analysis, argument, out _))
            return false;
        var resolved = argument switch {
            Variable v => ctx.Analysis?.GetResolvedType(v)?.GetRuntimeType(),
            Parameter p => ctx.Analysis?.GetResolvedType(p)?.GetRuntimeType(),
            _ => null,
        };
        return resolved is not null && parameterType.IsAssignableFrom(resolved);
    }

    /// <summary>
    /// `Expression&lt;D&gt;` position: a pure quoted tree. Captures are spilled to
    /// typed locals so the tree is translatable by a query provider.
    /// </summary>
    private static bool BuildExpressionArgument(
        Lambda lambda,
        Type delegateType,
        MethodInfo invokeMethod,
        Context ctx,
        out Expression prelude,
        out Expression callArgument) {
        var pure = new PureLambdaContext(lambda, delegateType, invokeMethod, ctx);
        var spills = new List<Expression>();
        foreach (var capture in FindBodyCaptures(lambda, ctx.Analysis)) {
            if (!TryCaptureClrType(capture, ctx, out var captureType))
                throw new InvalidOperationException(
                    $"VM compile rejected: captured '{CaptureName(capture)}' has no CLR type for the lambda bridge.");
            var local = ctx.DeclareBridgeLocal(captureType, CaptureName(capture));
            var binding = capture.Variable is { } variable ? (Node)variable : capture.Parameter!;
            var read = ReadBindingValue(binding, ctx);
            spills.Add(Assign(local, RingCodec.ToClr(
                read, captureType, binding, ctx.Analysis, ctx.HeapLocal)));
            pure.AddCapture(capture, local);
        }

        var body = CompilePureLambdaBody(lambda.Body, pure);
        var returnType = invokeMethod.ReturnType;
        if (returnType == typeof(void)) {
            if (body.Type != typeof(void)) {
                throw new InvalidOperationException(
                    $"VM compile rejected: a value-form lambda body cannot convert to the void delegate '{delegateType.Name}'.");
            }
        }
        else {
            if (body.Type == typeof(void))
                throw new InvalidOperationException(
                    $"VM compile rejected: lambda body is a statement; delegate '{delegateType.Name}' returns '{returnType.Name}'.");
            body = CoercePure(body, returnType, "lambda return");
        }

        var lambdaExpression = Lambda(delegateType, body, pure.ParameterExpressions);
        callArgument = Quote(lambdaExpression);
        EnsurePureTree(callArgument, pure.AllowedExpressions);
        prelude = spills.Count == 0 ? Empty() : Block(spills);
        return true;
    }

    /// <summary>
    /// Delegate `D` position: a VM re-entry adapter. The delegate carries the
    /// existing function-table body and closure handle, so captures are live
    /// (reference semantics) and the body supports the full module language.
    /// Valid while the owning <see cref="VmState"/> is alive; the thread is not
    /// policed.
    /// </summary>
    private static bool BuildDelegateArgument(
        Lambda? shape,
        Expression? closureSource,
        Type delegateType,
        MethodInfo invokeMethod,
        Context ctx,
        out Expression prelude,
        out Expression callArgument) {
        var returnType = invokeMethod.ReturnType;
        if (returnType == typeof(Task)
            || (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>)))
            throw new InvalidOperationException(
                "VM compile rejected: async lambda bridge delegates are not supported.");

        if (shape is not null) {
            var bodyType = ctx.Analysis?.GetResolvedType(shape.Body)?.GetRuntimeType();
            if (returnType == typeof(void)) {
                // A block body is statement-form; its trailing value is discarded.
                if (shape.Body is not Poly.Ast.Nodes.Block && bodyType is not null && bodyType != typeof(void))
                    throw new InvalidOperationException(
                        $"VM compile rejected: a value-form lambda body cannot convert to the void delegate '{delegateType.Name}'.");
            }
            else if (bodyType == typeof(void)) {
                throw new InvalidOperationException(
                    $"VM compile rejected: lambda body is a statement; delegate '{delegateType.Name}' returns '{returnType.Name}'.");
            }
        }

        var closureLocal = ctx.DeclareBridgeLocal(typeof(long), "closure");
        Expression closureExpr;
        if (closureSource is not null) {
            closureExpr = closureSource;
        }
        else {
            // EmitLambda leaves the closure handle on the ring; the typed local keeps
            // it and the transient slot is dead, so the region closes here.
            using (ctx.OpenRingScope()) {
                closureExpr = EmitLambda(shape!, ctx);
            }
        }
        var statements = new List<Expression> { Assign(closureLocal, closureExpr) };
        var targetLocal = ctx.DeclareBridgeLocal(typeof(object), "target");
        statements.Add(Assign(targetLocal,
            Call(ctx.HeapLocal, HeapUnsafeGet, Convert(closureLocal, typeof(int)))));
        var table = ctx.FunctionTableExpr
            ?? throw new InvalidOperationException(
                "VM compile rejected: lambda bridge has no function table.");

        var invokeParameters = invokeMethod.GetParameters();
        var parameters = new ParameterExpression[invokeParameters.Length];
        for (int i = 0; i < invokeParameters.Length; i++)
            parameters[i] = Parameter(invokeParameters[i].ParameterType, invokeParameters[i].Name);
        var argsArray = NewArrayInit(typeof(long),
            parameters.Select(p => (Expression)Call(null, BoxToAbiInfo, ctx.HeapLocal, Convert(p, typeof(object)))));

        // A forwarded parameter may hold a host delegate rather than a VM closure.
        // Dispatch on the target: only the host arm materializes the object array,
        // so a VM closure pays nothing for a path it never takes.
        var hostArgsArray = NewArrayInit(typeof(object),
            parameters.Select(p => (Expression)Convert(p, typeof(object))));
        Expression raw = Condition(
            TypeIs(targetLocal, typeof(object[])),
            Call(null, InvokeHeapClosureInfo, ctx.State, closureLocal, table, argsArray),
            Call(null, InvokeHostDelegateInfo, targetLocal, hostArgsArray, ctx.HeapLocal));
        Expression body;
        if (returnType == typeof(void)) {
            body = Block(raw, Empty());
        }
        else if (shape is null && (returnType == typeof(double) || returnType == typeof(float))) {
            // A forwarded parameter has no body node to classify; the delegate's
            // declared return type carries the IEEE shape.
            body = Convert(RingCodec.FromIeee(raw), returnType);
        }
        else {
            body = RingCodec.ToClr(raw, returnType, shape?.Body, ctx.Analysis, ctx.HeapLocal);
        }
        callArgument = Lambda(delegateType, body, parameters);
        prelude = Block(statements);
        return true;
    }


    private static Type? ParameterClrType(Parameter parameter, Context ctx) =>
        ClrTypeResolution.Resolve(
            parameter.TypeReference, parameter, node => ctx.Analysis?.GetResolvedType(node));

    private static string CaptureName(Capture capture) =>
        capture.Variable?.Name ?? capture.Parameter!.Name;

    private static bool TryCaptureClrType(Capture capture, Context ctx, out Type type) {
        type = null!;
        if (capture.Variable is { } variable) {
            type = ctx.Analysis?.GetResolvedType(variable)?.GetRuntimeType()
                ?? ctx.ClrTypeOf(variable)!;
            return type is not null;
        }
        if (capture.Parameter is { } parameter) {
            type = ParameterClrType(parameter, ctx)!;
            return type is not null;
        }
        return false;
    }

    private static Expression CompilePureLambdaBody(Node body, PureLambdaContext ctx) {
        Node? replacement = ctx.Abi.Analysis?.GetNodeReplacement(body);
        if (replacement is not null && replacement != body)
            return CompilePureLambdaBody(replacement, ctx);

        if (body is Block block) {
            if (block.Variables.Count > 0)
                throw RejectPure(block, "a lambda-bridge body cannot declare locals");
            if (block.Nodes.Count == 0)
                return Empty();
            var expressions = new List<Expression>(block.Nodes.Count);
            for (int i = 0; i < block.Nodes.Count; i++) {
                var expression = CompilePureLambdaBody(block.Nodes[i], ctx);
                if (i < block.Nodes.Count - 1 && expression.Type != typeof(void))
                    throw RejectPure(block.Nodes[i], "a non-final statement in a lambda-bridge body produces a value");
                expressions.Add(expression);
            }
            return expressions.Count == 1 ? expressions[0] : Block(expressions);
        }

        if (body is Return { Value: { } value })
            return CompilePureNode(value, ctx);
        if (body is Return)
            return Empty();
        return CompilePureNode(body, ctx);
    }

    private static Expression CompilePureNode(Node node, PureLambdaContext ctx) {
        Node? replacement = ctx.Abi.Analysis?.GetNodeReplacement(node);
        if (replacement is not null && replacement != node)
            return CompilePureNode(replacement, ctx);

        return node switch {
            Constant constant => PureConstant(constant),
            Parameter parameter => PureParameter(parameter, ctx),
            Variable variable => PureVariable(variable, ctx),
            Member member => CompilePureMember(member, ctx),
            Invoke invoke => CompilePureInvoke(invoke, ctx),
            Add add => PureAdd(add.LeftHandValue, add.RightHandValue, ctx),
            Subtract sub => PureArithmetic(sub.LeftHandValue, sub.RightHandValue, Subtract, ctx),
            Multiply mul => PureArithmetic(mul.LeftHandValue, mul.RightHandValue, Multiply, ctx),
            Divide div => PureArithmetic(div.LeftHandValue, div.RightHandValue, Divide, ctx),
            Modulo mod => PureArithmetic(mod.LeftHandValue, mod.RightHandValue, Modulo, ctx),
            BitwiseAnd bitAnd => PureBitwise(bitAnd.LeftHandValue, bitAnd.RightHandValue, And, ctx),
            BitwiseOr bitOr => PureBitwise(bitOr.LeftHandValue, bitOr.RightHandValue, Or, ctx),
            BitwiseXor bitXor => PureBitwise(bitXor.LeftHandValue, bitXor.RightHandValue, ExclusiveOr, ctx),
            ShiftLeft left => PureShift(left.LeftHandValue, left.RightHandValue, LeftShift, ctx),
            ShiftRight right => PureShift(right.LeftHandValue, right.RightHandValue, RightShift, ctx),
            Equal equal => PureComparison(equal.LeftHandValue, equal.RightHandValue, Equal, ctx),
            NotEqual notEqual => PureComparison(notEqual.LeftHandValue, notEqual.RightHandValue, NotEqual, ctx),
            LessThan less => PureComparison(less.LeftHandValue, less.RightHandValue, LessThan, ctx),
            LessThanOrEqual lessEq => PureComparison(lessEq.LeftHandValue, lessEq.RightHandValue, LessThanOrEqual, ctx),
            GreaterThan greater => PureComparison(greater.LeftHandValue, greater.RightHandValue, GreaterThan, ctx),
            GreaterThanOrEqual greaterEq => PureComparison(greaterEq.LeftHandValue, greaterEq.RightHandValue, GreaterThanOrEqual, ctx),
            And and => AndAlso(CompilePureCondition(and.LeftHandValue, ctx), CompilePureCondition(and.RightHandValue, ctx)),
            Or or => OrElse(CompilePureCondition(or.LeftHandValue, ctx), CompilePureCondition(or.RightHandValue, ctx)),
            Not not => Not(CompilePureCondition(not.Value, ctx)),
            UnaryMinus minus => Negate(CompilePureNode(minus.Operand, ctx)),
            BitwiseNot bitNot => Not(CompilePureNode(bitNot.Operand, ctx)),
            Conditional conditional => CompilePureConditional(conditional, ctx),
            Coalesce coalesce => CompilePureCoalesce(coalesce, ctx),
            TypeCast cast => Convert(CompilePureNode(cast.Operand, ctx), TargetClrType(cast.TargetTypeReference, cast, ctx)),
            TypeAs typeAs => TypeAs(CompilePureNode(typeAs.Operand, ctx), TargetClrType(typeAs.TargetTypeReference, typeAs, ctx)),
            TypeIs typeIs => TypeIs(CompilePureNode(typeIs.Operand, ctx), TargetClrType(typeIs.TargetTypeReference, typeIs, ctx)),
            TypeOf typeOf => Constant(TargetClrType(typeOf.Type, typeOf, ctx), typeof(Type)),
            Default @default => Default(@default.TargetType is null
                ? ctx.Abi.Analysis?.GetResolvedType(@default)?.GetRuntimeType()
                    ?? throw RejectPure(@default, "its target type is unresolved")
                : TargetClrType(@default.TargetType, @default, ctx)),
            NullForgiving forgiving => CompilePureNode(forgiving.Operand, ctx),
            Comment => Empty(),
            ThisReference => throw RejectPure(node, "'this' is not a pure CLR expression"),
            _ => throw RejectPure(node, "the node is not on the lambda-bridge allow-list"),
        };
    }

    private static Expression PureConstant(Constant constant) =>
        constant.Value is null
            ? Constant(null, typeof(object))
            : Constant(constant.Value);

    private static Expression PureParameter(Parameter parameter, PureLambdaContext ctx) {
        if (ctx.ParametersByNode.TryGetValue(parameter, out var byNode))
            return byNode;
        if (ctx.Abi.BoundDeclaredParameter(parameter) is { } declaration
            && !ReferenceEquals(declaration, parameter)
            && ctx.ParametersByNode.TryGetValue(declaration, out byNode))
            return byNode;
        if (ctx.CapturesByBinding.TryGetValue(parameter, out var capture))
            return capture;
        if (ctx.Abi.BoundDeclaredParameter(parameter) is { } declared
            && !ReferenceEquals(declared, parameter)
            && ctx.CapturesByBinding.TryGetValue(declared, out capture))
            return capture;
        throw RejectPure(parameter, $"parameter '{parameter.Name}' is neither a lambda parameter nor a captured value");
    }

    private static Expression PureVariable(Variable variable, PureLambdaContext ctx) {
        // A lambda parameter shadows an outer binding of the same name. The bind
        // pass records that edge on the use; the name is not a second key.
        if (ctx.Abi.BoundParameter(variable) is { } parameter
            && ctx.ParametersByNode.TryGetValue(parameter, out var parameterExpression))
            return parameterExpression;
        if (ctx.CapturesByBinding.TryGetValue(variable, out var byUse))
            return byUse;
        if (ctx.Abi.DeclaredVariable(variable) is { } declaration
            && ctx.CapturesByBinding.TryGetValue(declaration, out var byDeclaration))
            return byDeclaration;
        throw RejectPure(variable, $"variable '{variable.Name}' is not a lambda parameter or a captured value");
    }

    private static Expression CompilePureMember(Member member, PureLambdaContext ctx) {
        var resolved = ctx.Abi.Analysis?.GetResolvedMember(member)
            ?? throw RejectPure(member, $"member '{member.MemberName}' is unresolved");

        Expression? instance = null;
        if (IsTypeNameReceiver(member.Value, ctx.Abi)) {
            if (resolved.LifetimeModifier != LifetimeModifier.Static)
                throw RejectPure(member, $"member '{member.MemberName}' on a type name is not static");
        }
        else if (resolved.LifetimeModifier == LifetimeModifier.Static) {
            throw RejectPure(member, $"static member '{member.MemberName}' is accessed through a value");
        }
        else {
            instance = CompilePureNode(member.Value, ctx);
        }

        if (resolved.EmitRead(instance) is { } read)
            return read;
        if (resolved is ClrMethod { MethodInfo: { } method }
            && method.GetParameters().Length == 0
            && !method.ContainsGenericParameters)
            return Call(method.IsStatic ? null : instance, method);
        throw RejectPure(member, $"member '{member.MemberName}' is not a readable CLR member");
    }

    private static Expression CompilePureInvoke(Invoke invoke, PureLambdaContext ctx) {
        if (invoke.Delegate is not Member member)
            throw RejectPure(invoke, "invoking a stored lambda or delegate is not a pure CLR expression");
        var resolved = ctx.Abi.Analysis?.GetResolvedMember(invoke)
            ?? ctx.Abi.Analysis?.GetResolvedMember(member)
            ?? throw RejectPure(invoke, $"method '{member.MemberName}' is unresolved");
        if (resolved is not ClrMethod { MethodInfo: { } method })
            throw RejectPure(invoke, $"method '{member.MemberName}' is not a CLR method");
        if (method.ContainsGenericParameters)
            throw RejectPure(invoke, $"generic method '{method.Name}' is not supported in a bridged lambda");

        Expression? instance = null;
        if (method.IsStatic) {
            if (!IsTypeNameReceiver(member.Value, ctx.Abi))
                throw RejectPure(invoke, $"static method '{method.Name}' is invoked through a value");
        }
        else {
            if (IsTypeNameReceiver(member.Value, ctx.Abi))
                throw RejectPure(invoke, $"instance method '{method.Name}' is invoked on a type name");
            instance = CompilePureNode(member.Value, ctx);
        }

        var parameters = method.GetParameters();
        if (invoke.Arguments.Length > parameters.Length)
            throw RejectPure(invoke, $"method '{method.Name}' has {parameters.Length} parameter(s) but the call has {invoke.Arguments.Length} argument(s)");
        var arguments = new Expression[parameters.Length];
        for (int i = 0; i < parameters.Length; i++) {
            if (i >= invoke.Arguments.Length) {
                if (!parameters[i].HasDefaultValue)
                    throw RejectPure(invoke, $"argument '{parameters[i].Name}' has no value");
                arguments[i] = Constant(parameters[i].DefaultValue, parameters[i].ParameterType);
                continue;
            }
            arguments[i] = CoercePure(
                CompilePureNode(invoke.Arguments[i], ctx),
                parameters[i].ParameterType,
                $"argument '{parameters[i].Name}' of '{method.Name}'");
        }
        return Call(method.IsStatic ? null : instance, method, arguments);
    }

    private static Expression PureArithmetic(
        Node leftNode, Node rightNode,
        Func<Expression, Expression, BinaryExpression> factory,
        PureLambdaContext ctx) {
        var (left, right) = Promote(CompilePureNode(leftNode, ctx), CompilePureNode(rightNode, ctx));
        return factory(left, right);
    }

    private static Expression PureAdd(Node leftNode, Node rightNode, PureLambdaContext ctx) {
        var left = CompilePureNode(leftNode, ctx);
        var right = CompilePureNode(rightNode, ctx);
        if (left.Type == typeof(string) && right.Type == typeof(string))
            return Call(null, StringConcatInfo, left, right);
        var (promotedLeft, promotedRight) = Promote(left, right);
        return Add(promotedLeft, promotedRight);
    }

    private static Expression PureBitwise(
        Node leftNode, Node rightNode,
        Func<Expression, Expression, BinaryExpression> factory,
        PureLambdaContext ctx) {
        var (left, right) = Promote(CompilePureNode(leftNode, ctx), CompilePureNode(rightNode, ctx));
        return factory(left, right);
    }

    private static Expression PureShift(
        Node leftNode, Node rightNode,
        Func<Expression, Expression, BinaryExpression> factory,
        PureLambdaContext ctx) {
        var left = CompilePureNode(leftNode, ctx);
        var shift = CompilePureNode(rightNode, ctx);
        return factory(PromoteShiftLeft(left), Convert(shift, typeof(int)));
    }

    private static Expression PromoteShiftLeft(Expression value) =>
        value.Type == typeof(int) || value.Type == typeof(long) || value.Type == typeof(uint) || value.Type == typeof(ulong)
            ? value
            : Convert(value, typeof(int));

    private static Expression PureComparison(
        Node leftNode, Node rightNode,
        Func<Expression, Expression, BinaryExpression> factory,
        PureLambdaContext ctx) {
        var (left, right) = Promote(CompilePureNode(leftNode, ctx), CompilePureNode(rightNode, ctx));
        return factory(left, right);
    }

    private static Expression CompilePureCondition(Node node, PureLambdaContext ctx) {
        var value = CompilePureNode(node, ctx);
        if (value.Type == typeof(bool))
            return value;
        throw RejectPure(node, $"condition type '{value.Type.Name}' is not Boolean");
    }

    private static Expression CompilePureConditional(Conditional conditional, PureLambdaContext ctx) {
        var condition = CompilePureCondition(conditional.Condition, ctx);
        var whenTrue = CompilePureNode(conditional.IfTrue, ctx);
        var whenFalse = CompilePureNode(conditional.IfFalse, ctx);
        if (whenTrue.Type == typeof(void) || whenFalse.Type == typeof(void))
            throw RejectPure(conditional, "a conditional arm produces no value");
        var (promotedTrue, promotedFalse) = Promote(whenTrue, whenFalse);
        return Condition(condition, promotedTrue, promotedFalse);
    }

    private static Expression CompilePureCoalesce(Coalesce coalesce, PureLambdaContext ctx) {
        var left = CompilePureNode(coalesce.LeftHandValue, ctx);
        var right = CompilePureNode(coalesce.RightHandValue, ctx);
        if (left.Type.IsValueType && Nullable.GetUnderlyingType(left.Type) is null)
            throw RejectPure(coalesce, "the left operand is a non-nullable value type");
        var rightCoerced = CoercePure(right, left.Type, "the coalesce right operand");
        return Coalesce(left, rightCoerced);
    }

    private static Type TargetClrType(Node typeReference, Node owner, PureLambdaContext ctx) =>
        ClrTypeResolution.Resolve(
            typeReference, owner, node => ctx.Abi.Analysis?.GetResolvedType(node))
        ?? throw RejectPure(owner, "its target type is unresolved");

    /// <summary>
    /// C# numeric promotion and reference widening for a binary operation. Fails
    /// closed rather than asking <see cref="Expression"/> to invent a conversion the
    /// language does not have.
    /// </summary>
    private static (Expression Left, Expression Right) Promote(Expression left, Expression right) {
        if (left.Type == right.Type)
            return (left, right);
        if (left is ConstantExpression { Value: null } && !right.Type.IsValueType)
            return (Constant(null, right.Type), right);
        if (right is ConstantExpression { Value: null } && !left.Type.IsValueType)
            return (left, Constant(null, left.Type));

        var promoted = PromoteNumeric(left.Type, right.Type);
        if (promoted is not null)
            return (Convert(left, promoted), Convert(right, promoted));

        if (left.Type.IsAssignableFrom(right.Type))
            return (left, Convert(right, left.Type));
        if (right.Type.IsAssignableFrom(left.Type))
            return (Convert(left, right.Type), right);

        throw new InvalidOperationException(
            $"VM compile rejected: lambda bridge cannot combine '{left.Type.Name}' and '{right.Type.Name}'.");
    }

    private static Type? PromoteNumeric(Type left, Type right) {
        if (left == typeof(decimal) || right == typeof(decimal)) return typeof(decimal);
        if (left == typeof(double) || right == typeof(double)) return typeof(double);
        if (left == typeof(float) || right == typeof(float)) return typeof(float);
        if (left == typeof(ulong) || right == typeof(ulong)) return typeof(ulong);
        if (left == typeof(long) || right == typeof(long)) return typeof(long);
        if (left == typeof(uint) || right == typeof(uint)) return typeof(uint);
        if (IsSmallInteger(left) || IsSmallInteger(right)) return typeof(int);
        return null;
    }

    private static bool IsSmallInteger(Type type) =>
        type == typeof(char) || type == typeof(short) || type == typeof(byte)
        || type == typeof(sbyte) || type == typeof(ushort) || type == typeof(int);

    private static Expression CoercePure(Expression value, Type target, string context) {
        if (value.Type == target)
            return value;
        if (value is ConstantExpression { Value: null } && !target.IsValueType)
            return Constant(null, target);
        if (target.IsAssignableFrom(value.Type))
            return Convert(value, target);
        var underlying = Nullable.GetUnderlyingType(target);
        if (underlying is not null && (underlying == value.Type || underlying.IsAssignableFrom(value.Type)))
            return Convert(value, target);
        var promoted = PromoteNumeric(value.Type, target);
        if (promoted == target)
            return Convert(value, target);
        throw new InvalidOperationException(
            $"VM compile rejected: lambda bridge cannot convert '{value.Type.Name}' to '{target.Name}' for {context}.");
    }

    private static InvalidOperationException RejectPure(Node node, string reason) =>
        new($"VM compile rejected: lambda bridge body node '{node.GetType().Name}' is not a pure CLR expression: {reason}.");

    /// <summary>
    /// Defensive purity check: the emitted tree may only mention the lambda's own
    /// parameters and the typed spill locals. Any VM ring/heap reference would make
    /// an <c>Expression&lt;D&gt;</c> untranslatable.
    /// </summary>
    private static void EnsurePureTree(Expression expression, HashSet<ParameterExpression> allowed) {
        new PurityVisitor(allowed).Visit(expression);
    }


    private sealed class PurityVisitor(HashSet<ParameterExpression> allowed) : ExpressionVisitor {
        protected override Expression VisitParameter(ParameterExpression node) {
            if (!allowed.Contains(node))
                throw new InvalidOperationException(
                    $"VM compile rejected: lambda bridge tree references '{node.Name}', which is not a lambda parameter or typed spill local.");
            return base.VisitParameter(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node) {
            RejectVmMember(node.Method.DeclaringType);
            return base.VisitMethodCall(node);
        }

        protected override Expression VisitMember(MemberExpression node) {
            RejectVmMember(node.Member.DeclaringType);
            return base.VisitMember(node);
        }

        private static void RejectVmMember(Type? declaringType) {
            if (declaringType == typeof(VmManagedObjectHeap) || declaringType == typeof(VmState)
                || declaringType == typeof(RingCodec) || declaringType == typeof(VmCall))
                throw new InvalidOperationException(
                    $"VM compile rejected: lambda bridge tree references VM state via '{declaringType.Name}'.");
        }
    }

    private sealed class PureLambdaContext {
        public PureLambdaContext(Lambda lambda, Type delegateType, MethodInfo invokeMethod, Context abi) {
            Lambda = lambda;
            DelegateType = delegateType;
            InvokeMethod = invokeMethod;
            Abi = abi;
            var parameters = invokeMethod.GetParameters();
            ParameterExpressions = new List<ParameterExpression>(parameters.Length);
            for (int i = 0; i < parameters.Length; i++) {
                var expression = Parameter(parameters[i].ParameterType, parameters[i].Name);
                ParameterExpressions.Add(expression);
                ParametersByNode[lambda.Parameters[i]] = expression;
                AllowedExpressions.Add(expression);
            }
        }

        public Lambda Lambda { get; }
        public Type DelegateType { get; }
        public MethodInfo InvokeMethod { get; }
        public Context Abi { get; }
        public List<ParameterExpression> ParameterExpressions { get; }
        public Dictionary<Parameter, ParameterExpression> ParametersByNode { get; } =
            new(ReferenceEqualityComparer.Instance);
        public Dictionary<object, ParameterExpression> CapturesByBinding { get; } =
            new(ReferenceEqualityComparer.Instance);
        public HashSet<ParameterExpression> AllowedExpressions { get; } =
            new(ReferenceEqualityComparer.Instance);

        public void AddCapture(Capture capture, ParameterExpression local) {
            CapturesByBinding[capture.Binding] = local;
            if (capture.Variable is { } variable && Abi.DeclaredVariable(variable) is { } declaration)
                CapturesByBinding[declaration] = local;
            AllowedExpressions.Add(local);
        }
    }

    /// <summary>
    /// Invoke a host delegate reached through a bridged parameter: a forwarded
    /// parameter may hold a real CLR delegate rather than a VM closure.
    /// </summary>
    private static long InvokeHostDelegate(object? target, object?[] args, VmManagedObjectHeap heap) {
        if (target is null)
            throw new InvalidOperationException("VM: invoke of null.");
        if (target is not Delegate hostDelegate)
            throw new InvalidOperationException(
                "VM: lambda bridge target is neither a VM closure nor a delegate.");
        object? result;
        try {
            result = hostDelegate.DynamicInvoke(args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null) {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
        return RingCodec.ToRing(result, heap);
    }

    private static readonly MethodInfo InvokeHostDelegateInfo =
        typeof(AbiEmitter).GetMethod(nameof(InvokeHostDelegate),
            BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly MethodInfo StringConcatInfo =
        typeof(string).GetMethod(nameof(string.Concat), [typeof(string), typeof(string)])!;
}