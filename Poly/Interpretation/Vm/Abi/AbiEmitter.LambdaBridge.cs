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
            throw ShapeRefusal($"the open generic delegate '{delegateType}' is not supported.");

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
        if (lambda.Parameters.Count != delegateParameters.Length)
            throw ShapeRefusal($"it has {lambda.Parameters.Count} parameter(s) but delegate '{delegateType.Name}' expects {delegateParameters.Length}.");
        for (int i = 0; i < delegateParameters.Length; i++) {
            if (delegateParameters[i].ParameterType.IsByRef)
                throw ShapeRefusal($"delegate parameter '{delegateParameters[i].Name}' is by-ref; the bridge supports value parameters only.");
            var declared = ParameterClrType(lambda.Parameters[i], ctx);
            if (declared is not null && declared != delegateParameters[i].ParameterType)
                throw ShapeRefusal($"lambda parameter '{lambda.Parameters[i].Name}' is '{declared.Name}' but delegate '{delegateType.Name}' expects '{delegateParameters[i].ParameterType.Name}'.");
        }
    }

    /// <summary>
    /// The bridge's refusal of a lambda's shape before it builds the argument: its message and its mark, so a caller never
    /// counts it as the main emitter's.
    /// </summary>
    private static InvalidOperationException ShapeRefusal(string reason) {
        var refusal = new InvalidOperationException($"VM compile rejected: lambda bridge refuses the lambda: {reason}");
        refusal.Data[LambdaBridgeExceptionKey] = true;
        return refusal;
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
        try {
            return QuoteExpressionArgument(lambda, delegateType, invokeMethod, ctx, out prelude, out callArgument);
        }
        catch (Exception ex) when (MarkAsLambdaBridge(ex)) {
            throw;
        }
    }

    /// <summary>
    /// The <see cref="Exception.Data"/> key set on every exception raised while the bridge checks or quotes a lambda, so a
    /// caller can tell the bridge's refusal from the main emitter's. A lambda passed directly at an <c>Expression&lt;D&gt;</c>
    /// boundary has no function-table entry (<see cref="CollectQuoteOnlyLambdas"/>); any other lambda the main emitter still
    /// compiles there, before the call quotes it.
    /// </summary>
    internal const string LambdaBridgeExceptionKey = "Poly.Interpretation.Vm.Abi.LambdaBridge";

    /// <summary>Marks the exception and declines to catch it, so it propagates with its stack unchanged.</summary>
    private static bool MarkAsLambdaBridge(Exception ex) {
        ex.Data[LambdaBridgeExceptionKey] = true;
        return false;
    }

    private static bool QuoteExpressionArgument(
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

        Expression body;
        try {
            body = CompilePureLambdaBody(lambda.Body, pure);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
            && !ex.Message.StartsWith("VM compile rejected: lambda bridge", StringComparison.Ordinal)) {
            // The Expression API's own refusal, raw: the bridge's positioned one, naming the body.
            throw new InvalidOperationException(
                $"VM compile rejected: lambda bridge cannot quote the body '{lambda.Body.GetType().Name}' of a lambda for '{delegateType.Name}': {ex.Message}", ex);
        }
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
            body = CoercePure(body, returnType, "lambda return", pure);
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


    private static Type? ParameterClrType(Parameter parameter, Context ctx) => ParameterClrType(parameter, ctx.Analysis);

    private static Type? ParameterClrType(Parameter parameter, AnalysisResult? analysis) =>
        ClrTypeResolution.Resolve(
            parameter.TypeReference, parameter, node => analysis?.GetResolvedType(node));

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
            Add add => PureAdd(add, ctx),
            Subtract sub => PureBinary(OperatorKind.Subtract, sub, sub.LeftHandValue, sub.RightHandValue, Subtract, ctx),
            Multiply mul => PureBinary(OperatorKind.Multiply, mul, mul.LeftHandValue, mul.RightHandValue, Multiply, ctx),
            Divide div => PureBinary(OperatorKind.Divide, div, div.LeftHandValue, div.RightHandValue, Divide, ctx),
            Modulo mod => PureBinary(OperatorKind.Modulo, mod, mod.LeftHandValue, mod.RightHandValue, Modulo, ctx),
            BitwiseAnd bitAnd => PureBinary(OperatorKind.BitwiseAnd, bitAnd, bitAnd.LeftHandValue, bitAnd.RightHandValue, And, ctx),
            BitwiseOr bitOr => PureBinary(OperatorKind.BitwiseOr, bitOr, bitOr.LeftHandValue, bitOr.RightHandValue, Or, ctx),
            BitwiseXor bitXor => PureBinary(OperatorKind.BitwiseXor, bitXor, bitXor.LeftHandValue, bitXor.RightHandValue, ExclusiveOr, ctx),
            ShiftLeft left => PureBinary(OperatorKind.ShiftLeft, left, left.LeftHandValue, left.RightHandValue, LeftShift, ctx),
            ShiftRight right => PureBinary(OperatorKind.ShiftRight, right, right.LeftHandValue, right.RightHandValue, RightShift, ctx),
            Equal equal => PureBinary(OperatorKind.Equal, equal, equal.LeftHandValue, equal.RightHandValue, Equal, ctx),
            NotEqual notEqual => PureBinary(OperatorKind.NotEqual, notEqual, notEqual.LeftHandValue, notEqual.RightHandValue, NotEqual, ctx),
            LessThan less => PureBinary(OperatorKind.LessThan, less, less.LeftHandValue, less.RightHandValue, LessThan, ctx),
            LessThanOrEqual lessEq => PureBinary(OperatorKind.LessThanOrEqual, lessEq, lessEq.LeftHandValue, lessEq.RightHandValue, LessThanOrEqual, ctx),
            GreaterThan greater => PureBinary(OperatorKind.GreaterThan, greater, greater.LeftHandValue, greater.RightHandValue, GreaterThan, ctx),
            GreaterThanOrEqual greaterEq => PureBinary(OperatorKind.GreaterThanOrEqual, greaterEq, greaterEq.LeftHandValue, greaterEq.RightHandValue, GreaterThanOrEqual, ctx),
            And and => AndAlso(CompilePureCondition(and.LeftHandValue, ctx), CompilePureCondition(and.RightHandValue, ctx)),
            Or or => OrElse(CompilePureCondition(or.LeftHandValue, ctx), CompilePureCondition(or.RightHandValue, ctx)),
            Not not => Not(CompilePureCondition(not.Value, ctx)),
            UnaryMinus minus => PureUnary(OperatorKind.UnaryMinus, minus, minus.Operand, Negate, ctx),
            BitwiseNot bitNot => PureUnary(OperatorKind.BitwiseNot, bitNot, bitNot.Operand, Not, ctx),
            Conditional conditional => CompilePureConditional(conditional, ctx),
            Coalesce coalesce => CompilePureCoalesce(coalesce, ctx),
            TypeCast cast => ConvertOperand(cast, CompilePureNode(cast.Operand, ctx), TargetClrType(cast.TargetTypeReference, cast, ctx)),
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
                $"argument '{parameters[i].Name}' of '{method.Name}'",
                ctx);
        }
        return Call(method.IsStatic ? null : instance, method, arguments);
    }

    /// <summary>
    /// A binary operator. Over a governed operand (<see cref="IsGoverned"/>) the type provider decides it
    /// (<see cref="OperandsFor"/>); otherwise the Expression API's own rules apply (identity, a typed null, reference
    /// widening, a user-defined operator).
    /// </summary>
    private static Expression PureBinary(
        OperatorKind kind, Node node, Node leftNode, Node rightNode,
        Func<Expression, Expression, BinaryExpression> factory,
        PureLambdaContext ctx) =>
        PureBinary(kind, node, leftNode, rightNode, CompilePureNode(leftNode, ctx), CompilePureNode(rightNode, ctx), factory, ctx);

    private static Expression PureBinary(
        OperatorKind kind, Node node, Node leftNode, Node rightNode, Expression left, Expression right,
        Func<Expression, Expression, BinaryExpression> factory,
        PureLambdaContext ctx) {
        if (IsGovernedPair(left, right)) {
            var operands = OperandsFor(kind, node, leftNode, rightNode, left, right, ctx);
            var result = Combine(node, () => factory(operands.Left, operands.Right));
            if (result.Type != operands.Result)
                throw RejectOperator(node, $"the {kind} operator over '{TypeName(operands.Left.Type)}' and '{TypeName(operands.Right.Type)}' is '{TypeName(result.Type)}' in the expression tree, but the type provider states '{TypeName(operands.Result)}'");
            return result;
        }
        var (promotedLeft, promotedRight) = Promote(left, right);
        return Combine(node, () => factory(promotedLeft, promotedRight));
    }

    private static Expression PureAdd(Add add, PureLambdaContext ctx) {
        var left = CompilePureNode(add.LeftHandValue, ctx);
        var right = CompilePureNode(add.RightHandValue, ctx);
        if (left.Type == typeof(string) && right.Type == typeof(string))
            return Call(null, StringConcatInfo, left, right);
        return PureBinary(OperatorKind.Add, add, add.LeftHandValue, add.RightHandValue, left, right, Add, ctx);
    }

    /// <summary>A unary operator over a governed operand: the type provider's operator, as for a binary one.</summary>
    private static Expression PureUnary(
        OperatorKind kind, Node node, Node operandNode,
        Func<Expression, UnaryExpression> factory,
        PureLambdaContext ctx) {
        var operand = CompilePureNode(operandNode, ctx);
        if (!IsGoverned(operand.Type))
            return Combine(node, () => factory(operand));
        var definition = OperandDefinition(operand.Type, node, ctx);
        if (definition.GetOperator(kind, null) is not { } resolution)
            throw RejectOperator(node, $"the type provider defines no {kind} operator for '{TypeName(operand.Type)}'");
        var lifted = Nullable.GetUnderlyingType(operand.Type) is not null;
        var parameter = Lift(RuntimeTypeOf(resolution.Signature.Parameters.Single().ParameterTypeDefinition, node), lifted);
        var expected = Lift(RuntimeTypeOf(resolution.Result, node), lifted);
        var converted = ConvertOperand(node, operand, parameter);
        var result = Combine(node, () => factory(converted));
        if (result.Type != expected)
            throw RejectOperator(node, $"the {kind} operator over '{TypeName(parameter)}' is '{TypeName(result.Type)}' in the expression tree, but the type provider states '{TypeName(expected)}'");
        return result;
    }

    /// <summary>
    /// The operands of a governed binary operator converted to the parameter types of the operator the type provider
    /// states, and the result it states. A nullable operand is lifted: the provider is asked about the underlying types and
    /// both parameters become <c>Nullable&lt;P&gt;</c>; a lifted comparison stays <c>bool</c>, the rest are
    /// <c>Nullable&lt;R&gt;</c> (C# 12.4.8). Before the provider, T-18's shared <see cref="ConstantOperandRule"/> judges a
    /// non-negative signed constant beside a <c>ulong</c> as UInt64 (owner ruling #679 6842272727; removed by T-26); the
    /// bridge only converts the operands. Undefined: a positioned refusal.
    /// </summary>
    private static (Expression Left, Expression Right, Type Result) OperandsFor(
        OperatorKind kind, Node node, Node leftNode, Node rightNode, Expression left, Expression right, PureLambdaContext ctx) {
        var analysis = ctx.Abi.Analysis
            ?? throw RejectOperator(node, "the lambda bridge has no analysis, so no type provider to state the operator");
        var lifted = Nullable.GetUnderlyingType(left.Type) is not null || Nullable.GetUnderlyingType(right.Type) is not null;
        var comparison = kind is OperatorKind.Equal or OperatorKind.NotEqual or OperatorKind.LessThan
            or OperatorKind.LessThanOrEqual or OperatorKind.GreaterThan or OperatorKind.GreaterThanOrEqual;
        if (ConstantOperandRule.Applies(kind, analysis.GetEffectiveNode(leftNode), left.Type, analysis.GetEffectiveNode(rightNode), right.Type)) {
            var uint64 = Lift(typeof(ulong), lifted);
            return (ConvertOperand(node, left, uint64), ConvertOperand(node, right, uint64), comparison ? typeof(bool) : uint64);
        }
        var leftDefinition = OperandDefinition(left.Type, node, ctx);
        var rightDefinition = OperandDefinition(right.Type, node, ctx);
        if (leftDefinition.GetOperator(kind, rightDefinition) is not { } resolution)
            throw RejectOperator(node, $"the type provider defines no {kind} operator for '{TypeName(left.Type)}' and '{TypeName(right.Type)}'");
        var parameters = resolution.Signature.Parameters
            .Select(parameter => Lift(RuntimeTypeOf(parameter.ParameterTypeDefinition, node), lifted)).ToArray();
        var result = RuntimeTypeOf(resolution.Result, node);
        return (ConvertOperand(node, left, parameters[0]), ConvertOperand(node, right, parameters[1]), comparison ? result : Lift(result, lifted));
    }

    /// <summary>The type provider's definition of an operand's type, its nullable unwrapped; a missing definition is a refusal.</summary>
    private static ITypeDefinition OperandDefinition(Type type, Node node, PureLambdaContext ctx) {
        var analysis = ctx.Abi.Analysis
            ?? throw RejectOperator(node, "the lambda bridge has no analysis, so no type provider to state the operator");
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return analysis.TypeDefinitions.GetTypeDefinition(underlying)
            ?? throw RejectOperator(node, $"the type provider has no definition for '{underlying.Name}'");
    }

    private static Type RuntimeTypeOf(ITypeDefinition definition, Node node) =>
        definition.TryGetRuntimeType(out var type)
            ? type
            : throw RejectOperator(node, $"the type provider's '{definition.Name}' has no CLR type");

    private static Type Lift(Type type, bool lifted) =>
        lifted && type.IsValueType && Nullable.GetUnderlyingType(type) is null ? typeof(Nullable<>).MakeGenericType(type) : type;

    /// <summary>
    /// Governed: a CLR primitive (numeric, <c>char</c>, <c>bool</c>, the native integers) or <c>decimal</c>, or
    /// <c>Nullable&lt;&gt;</c> of one. The type provider decides every operator and conversion over a governed operand;
    /// other types keep the Expression API's rules (enum: T-25; temporal, Guid, string, reference and null: T-23).
    /// </summary>
    private static bool IsGoverned(Type type) =>
        (Nullable.GetUnderlyingType(type) ?? type) is var underlying && (underlying.IsPrimitive || underlying == typeof(decimal));

    /// <summary>A governed operand beside anything but a null literal; a null literal keeps the Expression API's typing.</summary>
    private static bool IsGovernedPair(Expression left, Expression right) =>
        (IsGoverned(left.Type) || IsGoverned(right.Type)) && !IsNullLiteral(left) && !IsNullLiteral(right);

    private static bool IsNullLiteral(Expression expression) => expression is ConstantExpression { Value: null };

    private static string TypeName(Type type) =>
        Nullable.GetUnderlyingType(type) is { } underlying ? underlying.Name + "?" : type.Name;

    /// <summary>Builds a node with the Expression API, turning its own refusal into the bridge's positioned one.</summary>
    private static Expression Combine(Node node, Func<Expression> build) {
        try {
            return build();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) {
            throw RejectOperator(node, ex.Message);
        }
    }

    /// <summary>
    /// A conversion the provider stated, built by the Expression API, which may still have no conversion for the pair
    /// (<c>IntPtr</c> to <c>double</c>): then the bridge's positioned refusal.
    /// </summary>
    private static Expression ConvertOperand(Node node, Expression value, Type type) =>
        value.Type == type ? value : Combine(node, () => Convert(value, type));

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
        if (IsGovernedPair(whenTrue, whenFalse)) {
            var common = ConditionalType(conditional, whenTrue.Type, whenFalse.Type, ctx);
            return Condition(condition, ConvertOperand(conditional, whenTrue, common), ConvertOperand(conditional, whenFalse, common));
        }
        var (promotedTrue, promotedFalse) = Promote(whenTrue, whenFalse);
        return Combine(conditional, () => Condition(condition, promotedTrue, promotedFalse));
    }

    /// <summary>
    /// The type of a conditional over governed arms (C# 12.18): the arm type the other arm converts to implicitly, from the
    /// type provider's <c>GetCommonType</c> over the underlying types. Two nullable arms lift the common type; a nullable
    /// arm beside a non-nullable one is the nullable arm's type when its underlying type is the common type (the other arm
    /// converts to it, lifted), and no type otherwise.
    /// </summary>
    private static Type ConditionalType(Conditional conditional, Type whenTrue, Type whenFalse, PureLambdaContext ctx) {
        if (whenTrue == whenFalse)
            return whenTrue;
        var (trueDefinition, falseDefinition) = (OperandDefinition(whenTrue, conditional, ctx), OperandDefinition(whenFalse, conditional, ctx));
        var common = new[] { trueDefinition, falseDefinition }.GetCommonType() is { } found
            ? RuntimeTypeOf(found, conditional)
            : throw RejectOperator(conditional, $"the type provider states no common type for the arms '{TypeName(whenTrue)}' and '{TypeName(whenFalse)}'");
        var (trueNullable, falseNullable) = (Nullable.GetUnderlyingType(whenTrue) is not null, Nullable.GetUnderlyingType(whenFalse) is not null);
        if (trueNullable == falseNullable)
            return Lift(common, trueNullable);
        var nullableArm = trueNullable ? whenTrue : whenFalse;
        return Nullable.GetUnderlyingType(nullableArm) == common
            ? nullableArm
            : throw RejectOperator(conditional, $"the arms '{TypeName(whenTrue)}' and '{TypeName(whenFalse)}' have no common type: neither converts implicitly to the other");
    }

    /// <summary>
    /// <c>a ?? b</c> typed by C# 12.15, with A the left's type and A0 its underlying type when it is nullable: A0 when b
    /// converts implicitly to A0; else A when b converts to A; else B when A0 converts to B (a unwrapped and converted);
    /// else B when a converts to B; otherwise a positioned refusal. The conversions are <see cref="TryCoercePure"/>'s.
    /// </summary>
    private static Expression CompilePureCoalesce(Coalesce coalesce, PureLambdaContext ctx) {
        var left = CompilePureNode(coalesce.LeftHandValue, ctx);
        var right = CompilePureNode(coalesce.RightHandValue, ctx);
        if (left.Type.IsValueType && Nullable.GetUnderlyingType(left.Type) is null)
            throw RejectPure(coalesce, "the left operand is a non-nullable value type");
        var underlying = Nullable.GetUnderlyingType(left.Type);
        if (underlying is not null && TryCoercePure(right, underlying, ctx, out var toUnderlying, out _))
            return Combine(coalesce, () => Coalesce(left, toUnderlying));
        if (TryCoercePure(right, left.Type, ctx, out var toLeft, out _))
            return Combine(coalesce, () => Coalesce(left, toLeft));
        if (!IsNullLiteral(right)) {
            if (underlying is not null && TryCoercePure(Default(underlying), right.Type, ctx, out _, out _)) {
                var lifted = ConvertOperand(coalesce, left, Lift(right.Type, true));
                return Combine(coalesce, () => Coalesce(lifted, right));
            }
            if (TryCoercePure(left, right.Type, ctx, out var leftToRight, out _))
                return Combine(coalesce, () => Coalesce(leftToRight, right));
        }
        throw RejectOperator(coalesce, $"C# states no type for '{TypeName(left.Type)} ?? {TypeName(right.Type)}': neither operand converts implicitly to the other");
    }

    private static Type TargetClrType(Node typeReference, Node owner, PureLambdaContext ctx) =>
        ClrTypeResolution.Resolve(
            typeReference, owner, node => ctx.Abi.Analysis?.GetResolvedType(node))
        ?? throw RejectPure(owner, "its target type is unresolved");

    /// <summary>
    /// The Expression API's typing for an operation over non-governed operands: identity, a null literal typed as the
    /// other operand when that can be null (a reference or a <c>Nullable&lt;&gt;</c>), and reference widening. Fails closed rather than asking <see cref="Expression"/> to invent a
    /// conversion the language does not have.
    /// </summary>
    private static (Expression Left, Expression Right) Promote(Expression left, Expression right) {
        if (left.Type == right.Type)
            return (left, right);
        if (IsNullLiteral(left) && CanBeNull(right.Type))
            return (Constant(null, right.Type), right);
        if (IsNullLiteral(right) && CanBeNull(left.Type))
            return (left, Constant(null, left.Type));

        if (left.Type.IsAssignableFrom(right.Type))
            return (left, Convert(right, left.Type));
        if (right.Type.IsAssignableFrom(left.Type))
            return (Convert(left, right.Type), right);

        throw new InvalidOperationException(
            $"VM compile rejected: lambda bridge cannot combine '{left.Type.Name}' and '{right.Type.Name}'.");
    }

    /// <summary>
    /// The conversion of a value to a CLR target (an argument, the lambda's return). Between governed types it is the type
    /// provider's implicit <c>ResolveConversionFrom</c> over the underlying types, lifted to a nullable target (C# 10.6.1);
    /// a nullable value never converts implicitly to a non-nullable target. A null literal converts to a reference or a
    /// <c>Nullable&lt;&gt;</c> target (C# 10.2.7). Otherwise: identity, assignability.
    /// </summary>
    private static Expression CoercePure(Expression value, Type target, string context, PureLambdaContext ctx) =>
        TryCoercePure(value, target, ctx, out var converted, out var reason)
            ? converted
            : throw new InvalidOperationException(
                $"VM compile rejected: lambda bridge cannot convert '{TypeName(value.Type)}' to '{TypeName(target)}' for {context}{reason}.");

    private static bool TryCoercePure(Expression value, Type target, PureLambdaContext ctx, out Expression converted, out string reason) {
        converted = value;
        reason = "";
        if (value.Type == target)
            return true;
        if (IsNullLiteral(value)) {
            if (!CanBeNull(target))
                return false;
            converted = Constant(null, target);
            return true;
        }
        if (IsGoverned(value.Type) && IsGoverned(target)) {
            if (!ImplicitlyConverts(value.Type, target, ctx)) {
                reason = ": the type provider states no implicit conversion";
                return false;
            }
            return TryConvert(value, target, out converted, out reason);
        }
        var underlying = Nullable.GetUnderlyingType(target);
        if (target.IsAssignableFrom(value.Type) || (underlying is not null && (underlying == value.Type || underlying.IsAssignableFrom(value.Type))))
            return TryConvert(value, target, out converted, out reason);
        return false;
    }

    private static bool TryConvert(Expression value, Type target, out Expression converted, out string reason) {
        try {
            converted = Convert(value, target);
            reason = "";
            return true;
        }
        catch (InvalidOperationException ex) {
            converted = value;
            reason = $": {ex.Message}";
            return false;
        }
    }

    private static bool CanBeNull(Type type) => !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

    private static bool ImplicitlyConverts(Type source, Type target, PureLambdaContext ctx) {
        var (sourceUnderlying, targetUnderlying) = (Nullable.GetUnderlyingType(source), Nullable.GetUnderlyingType(target));
        if (sourceUnderlying is not null && targetUnderlying is null)
            return false;
        var (from, to) = (sourceUnderlying ?? source, targetUnderlying ?? target);
        if (from == to)
            return true;
        var definitions = (ctx.Abi.Analysis ?? throw new InvalidOperationException(
            "VM compile rejected: lambda bridge has no analysis, so no type provider to state the conversion.")).TypeDefinitions;
        return definitions.GetTypeDefinition(to) is { } toDefinition
            && definitions.GetTypeDefinition(from) is { } fromDefinition
            && toDefinition.ResolveConversionFrom(fromDefinition) is { Kind: ConversionOperatorKind.Implicit };
    }

    private static InvalidOperationException RejectOperator(Node node, string reason) =>
        new($"VM compile rejected: lambda bridge cannot state body node '{node.GetType().Name}': {reason}.");

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