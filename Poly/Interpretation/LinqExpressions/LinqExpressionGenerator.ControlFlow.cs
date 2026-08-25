using System.Linq.Expressions;

using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation.LinqExpressions;

public sealed partial class LinqExpressionGenerator {
    /// <summary>
    /// Compiles a cast such as <c>(T)value</c> or a checked cast by emitting the operand and then
    /// wrapping it in either <see cref="Expression.Convert(Expression, Type)"/> or
    /// <see cref="Expression.ConvertChecked(Expression, Type)"/>.
    /// </summary>
    private Expression CompileTypeCast(TypeCast typeCast, CompilationContext context) {
        var operand = CompileNode(typeCast.Operand, context);
        var type = GetClrType(typeCast);
        return typeCast.IsChecked
            ? Expression.ConvertChecked(operand, type)
            : Expression.Convert(operand, type);
    }

    /// <summary>
    /// Compiles a type test such as <c>value is T</c> into <see cref="Expression.TypeIs(Expression, Type)"/>.
    /// </summary>
    private Expression CompileTypeIs(TypeIs typeIs, CompilationContext context) {
        var operand = CompileNode(typeIs.Operand, context);
        var type = GetClrType(typeIs.TargetTypeReference);
        return Expression.TypeIs(operand, type);
    }

    /// <summary>
    /// Compiles a safe cast such as <c>value as T</c> into <see cref="Expression.TypeAs(Expression, Type)"/>
    /// for reference/nullable targets and a nullable convert for non-nullable value type targets.
    /// </summary>
    private Expression CompileTypeAs(TypeAs typeAs, CompilationContext context) {
        var operand = CompileNode(typeAs.Operand, context);
        var type = GetClrType(typeAs.TargetTypeReference);

        if (!type.IsValueType || Nullable.GetUnderlyingType(type) is not null) {
            return Expression.TypeAs(operand, type);
        }

        var nullableType = typeof(Nullable<>).MakeGenericType(type);
        return Expression.TypeAs(operand, nullableType);
    }

    /// <summary>
    /// Compiles an <c>if</c> statement into <see cref="Expression.IfThen(Expression, Expression)"/> or
    /// <see cref="Expression.IfThenElse(Expression, Expression, Expression)"/>.
    /// The condition and branches are emitted in place, with branch typing normalized when possible.
    /// </summary>
    private Expression CompileIfStatement(IfStatement ifStmt, CompilationContext context) {
        var condition = CompileNode(ifStmt.Condition, context);
        Expression thenBranch;
        if (_analysisResult != null && _analysisResult.CanElide(ifStmt.ThenBranch)) {
            thenBranch = Expression.Empty();
        }
        else {
            thenBranch = CompileNode(ifStmt.ThenBranch, context);
        }

        if (ifStmt.ElseBranch != null) {
            Expression elseBranch;
            if (_analysisResult != null && _analysisResult.CanElide(ifStmt.ElseBranch)) {
                elseBranch = Expression.Empty();
            }
            else {
                elseBranch = CompileNode(ifStmt.ElseBranch, context);
            }
            if (thenBranch.Type == elseBranch.Type) {
                return Expression.IfThenElse(condition, thenBranch, elseBranch);
            }
            if (thenBranch.Type == typeof(void))
                return Expression.IfThenElse(condition, thenBranch, elseBranch);
            else if (elseBranch.Type == typeof(void))
                return Expression.IfThenElse(condition, thenBranch, elseBranch);
        }

        return Expression.IfThen(condition, thenBranch);
    }

    /// <summary>
    /// Compiles a switch statement by emitting the switch value, compiling each case pattern/body
    /// pair into a <see cref="SwitchCase"/>, and then producing a single
    /// <see cref="Expression.Switch(Type, Expression, Expression, System.Reflection.MethodInfo, IEnumerable{System.Linq.Expressions.SwitchCase})"/>.
    /// </summary>
    private Expression CompileSwitchStatement(SwitchStatement switchStmt, CompilationContext context) {
        var switchValue = CompileNode(switchStmt.Value, context);
        var switchType = switchValue.Type;

        var cases = switchStmt.Cases.Select(caseNode => {
            var pattern = CompileNode(caseNode.Pattern, context);
            var body = CompileNode(caseNode.Body, context);
            // SwitchCase expects Expression array for test values
            return Expression.SwitchCase(body, pattern);
        }).ToArray();

        var defaultCase = switchStmt.DefaultCase != null ? CompileNode(switchStmt.DefaultCase, context) : null;

        return Expression.Switch(switchType, switchValue, defaultCase, null, cases);
    }

    /// <summary>
    /// Compiles a <c>while (condition) { body }</c> loop into an infinite LINQ loop with explicit
    /// break and continue labels.
    /// The condition is checked at the top of each iteration and breaks the loop when false, while
    /// the body executes inside a child loop scope so unlabeled <c>break</c> and <c>continue</c>
    /// bind to this loop instance.
    /// </summary>
    private Expression CompileWhileLoop(WhileLoop whileLoop, CompilationContext context) {
        var breakLabel = Expression.Label("break");
        var continueLabel = Expression.Label("continue");
        var loopContext = context.CreateLoopScope(breakLabel, continueLabel);

        var condition = CompileNode(whileLoop.Condition, context);  // value used for control
        var body = CompileNode(whileLoop.Body, loopContext);

        var loopBody = Expression.Block(
            Expression.IfThen(
                Expression.Not(condition),
                Expression.Break(breakLabel)),
            body,
            Expression.Label(continueLabel));

        return Expression.Loop(loopBody, breakLabel);
    }

    /// <summary>
    /// Compiles a <c>do { body } while (condition)</c> loop into an infinite LINQ loop whose body
    /// runs before the condition check.
    /// A child loop scope provides the active break and continue labels so control-flow statements
    /// target this loop and not an outer one.
    /// </summary>
    private Expression CompileDoWhileLoop(DoWhileLoop doWhileLoop, CompilationContext context) {
        var breakLabel = Expression.Label("break");
        var continueLabel = Expression.Label("continue");
        var loopContext = context.CreateLoopScope(breakLabel, continueLabel);

        var body = CompileNode(doWhileLoop.Body, loopContext);
        var condition = CompileNode(doWhileLoop.Condition, context);  // value used for control

        var loopBody = Expression.Block(
            body,
            Expression.Label(continueLabel),
            Expression.IfThen(
                Expression.Not(condition),
                Expression.Break(breakLabel)));

        return Expression.Loop(loopBody, breakLabel);
    }

    /// <summary>
    /// Compiles a <c>for (initializer; condition; increment) { body }</c> loop into a block that
    /// runs the initializer once and then executes an explicit LINQ loop containing the condition
    /// check, body, continue label, and increment step in that order.
    /// </summary>
    private Expression CompileForLoop(ForLoop forLoop, CompilationContext context) {
        var breakLabel = Expression.Label("break");
        var continueLabel = Expression.Label("continue");
        var loopContext = context.CreateLoopScope(breakLabel, continueLabel);

        Expression? initializer = null;
        if (forLoop.Initializer != null && (_analysisResult == null || !_analysisResult.CanElide(forLoop.Initializer))) {
            initializer = CompileNode(forLoop.Initializer, context);
        }
        Expression? condition = null;
        if (forLoop.Condition != null) {
            // Always compile condition: its value is used for loop control flow.
            // Analysis should never mark the condition sub-expression as CanElide.
            condition = CompileNode(forLoop.Condition, context);
        }
        Expression? increment = null;
        if (forLoop.Increment != null && (_analysisResult == null || !_analysisResult.CanElide(forLoop.Increment))) {
            increment = CompileNode(forLoop.Increment, context);
        }
        var body = CompileNode(forLoop.Body, loopContext);

        var loopBody = Expression.Block(
            condition != null
                ? Expression.IfThen(Expression.Not(condition), Expression.Break(breakLabel))
                : Expression.Empty(),
            body,
            Expression.Label(continueLabel),
            increment ?? Expression.Empty());

        var blockExpressions = new List<Expression>();
        if (initializer != null)
            blockExpressions.Add(initializer);

        blockExpressions.Add(Expression.Loop(loopBody, breakLabel));

        return blockExpressions.Count == 1 ? blockExpressions[0] : Expression.Block(blockExpressions);
    }

    /// <summary>
    /// Compiles a <c>foreach (var v in collection) { body }</c> loop into a block that acquires an
    /// <see cref="IEnumerator"/>, advances it inside an explicit LINQ loop, assigns
    /// <see cref="IEnumerator.Current"/> into the lexical loop variable each iteration, executes the
    /// body, and disposes the enumerator in a <c>finally</c> block when iteration completes.
    /// </summary>
    private Expression CompileForEachLoop(ForEachLoop foreachLoop, CompilationContext context) {
        var breakLabel = Expression.Label("break");
        var continueLabel = Expression.Label("continue");
        var loopContext = context.CreateLoopScope(breakLabel, continueLabel);

        var collection = CompileNode(foreachLoop.Collection, context);  // value used to drive iteration
        var enumeratorVar = Expression.Variable(typeof(IEnumerator), "enumerator");
        var getEnumeratorCall = Expression.Call(
            Expression.Convert(collection, typeof(IEnumerable)),
            Ref<IEnumerable>.Method(e => e.GetEnumerator()));
        var assignEnumerator = Expression.Assign(enumeratorVar, getEnumeratorCall);
        var moveNextCall = Expression.Call(enumeratorVar, Ref<IEnumerator>.Method(e => e.MoveNext()));
        var currentProperty = Expression.Property(enumeratorVar, nameof(IEnumerator.Current));

        // Pre-register the loop variable PE before compiling the body so that the
        // assignment (here) and any references inside the body share the same PE.
        var loopVarPE = loopContext.DeclareVariable(foreachLoop.LoopVariable, CreateVariableExpression(foreachLoop.LoopVariable));

        var compiledBody = CompileNode(foreachLoop.Body, loopContext);
        Expression currentValue = currentProperty;
        if (currentProperty.Type != loopVarPE.Type) {
            currentValue = Expression.Convert(currentProperty, loopVarPE.Type);
        }

        var loopBody = Expression.Block(
            Expression.IfThen(
                Expression.Not(moveNextCall),
                Expression.Break(breakLabel)),
            Expression.Assign(loopVarPE, currentValue),
            compiledBody,
            Expression.Label(continueLabel)
        );

        var tryFinally = Expression.TryFinally(
            Expression.Loop(loopBody, breakLabel),
            Expression.IfThen(
                Expression.TypeIs(enumeratorVar, typeof(IDisposable)),
                Expression.Call(Expression.Convert(enumeratorVar, typeof(IDisposable)), Ref<IDisposable>.Method(d => d.Dispose()))));

        return Expression.Block(
            [enumeratorVar, loopVarPE],
            assignEnumerator,
            tryFinally
        );
    }

    /// <summary>
    /// Compiles a <c>break</c> statement by targeting the active loop break label for unlabeled
    /// breaks, or a function-scoped named label for labeled breaks.
    /// </summary>
    private Expression CompileBreakStatement(BreakStatement breakStmt, CompilationContext context) {
        if (breakStmt.Label == null && context.CurrentBreakLabel != null) {
            return Expression.Break(context.CurrentBreakLabel);
        }

        var label = breakStmt.Label ?? "break";
        return Expression.Break(GetOrCreateLabel(label, context));
    }

    /// <summary>
    /// Compiles a <c>continue</c> statement by targeting the active loop continue label for
    /// unlabeled continues, or a function-scoped named label for labeled continues.
    /// </summary>
    private Expression CompileContinueStatement(ContinueStatement continueStmt, CompilationContext context) {
        if (continueStmt.Label == null && context.CurrentContinueLabel != null) {
            return Expression.Continue(context.CurrentContinueLabel);
        }

        var label = continueStmt.Label ?? "continue";
        return Expression.Continue(GetOrCreateLabel(label, context));
    }

    /// <summary>
    /// Compiles a label declaration into a block containing the label target followed by the
    /// labeled statement, so <c>goto</c> expressions within the same function scope can jump to it.
    /// </summary>
    private Expression CompileLabelDeclaration(LabelDeclaration labelDecl, CompilationContext context) {
        var label = GetOrCreateLabel(labelDecl.Name, context);
        var statement = CompileNode(labelDecl.Statement, context);
        return Expression.Block(
            Expression.Label(label),
            statement);
    }

    /// <summary>
    /// Compiles a <c>return</c> statement into <see cref="Expression.Return(LabelTarget)"/> or
    /// <see cref="Expression.Return(LabelTarget, Expression)"/>.
    /// The enclosing function scope owns the shared return label, which is lazily created and typed
    /// from the first returned value so all returns in that scope target the same exit point.
    /// </summary>
    private Expression CompileReturnStatement(Return returnStmt, CompilationContext context) {
        if (returnStmt.Value != null) {
            var value = CompileNode(returnStmt.Value, context);
            // Use a typed label so that Expression.Return and Expression.Label agree on type.
            if (!context.FunctionLabels.TryGetValue("return", out var returnLabel)) {
                returnLabel = Expression.Label(value.Type, "return");
                context.FunctionLabels["return"] = returnLabel;
            }
            return Expression.Return(returnLabel, value);
        }

        var voidReturnLabel = GetOrCreateLabel("return", context);
        return Expression.Return(voidReturnLabel);
    }

    /// <summary>
    /// Compiles a lambda expression by creating a new lexical function scope, declaring the lambda
    /// parameters locally, compiling the body within that scope, and then closing any function-local
    /// return label before emitting the final <see cref="Expression.Lambda(Expression, IEnumerable{ParameterExpression})"/>.
    /// </summary>
    private Expression CompileLambda(Lambda lambda, CompilationContext context) {
        var lambdaContext = context.CreateLambdaScope();
        var paramExprs = lambda.Parameters
            .Select(parameter => lambdaContext.DeclareParameter(parameter, CreateParameterExpression(parameter), export: false))
            .ToArray();
        var bodyExpr = CompileNode(lambda.Body, lambdaContext);

        // If the body introduced a return label (e.g., via a top-level Return statement
        // that wasn't consumed by an inner Block), close it here.
        if (lambdaContext.FunctionLabels.TryGetValue("return", out var bodyReturnLabel)) {
            lambdaContext.FunctionLabels.Remove("return");
            bodyExpr = Expression.Block(
                bodyExpr,
                Expression.Label(bodyReturnLabel, Expression.Default(bodyReturnLabel.Type)));
        }

        return Expression.Lambda(bodyExpr, paramExprs);
    }

    /// <summary>
    /// Compiles an invocation either as a direct method call when the delegate expression is a
    /// <see cref="Member"/>, or as a general <see cref="Expression.Invoke(Expression, IEnumerable{Expression})"/>
    /// when invoking a first-class lambda or delegate value.
    /// </summary>
    private Expression CompileInvocation(Invoke invoke, CompilationContext context) {
        var argExprs = invoke.Arguments.Select(argument => CompileNode(argument, context)).ToArray();

        if (invoke.Delegate is Member memberAccess) {
            // When the analysis has resolved the method, use its MethodInfo directly
            // to correctly handle both static and instance calls.
            if (_analysisResult.GetResolvedMember(invoke) is ClrMethod resolvedMethod) {
                var methodInfo = resolvedMethod.MethodInfo;
                if (resolvedMethod.LifetimeModifier == LifetimeModifier.Static) {
                    return Expression.Call(methodInfo, argExprs);
                }

                var instance = CompileNode(memberAccess.Value, context);
                return Expression.Call(instance, methodInfo, argExprs);
            }

            // Fallback when analysis didn't resolve the method
            return Expression.Call(
                CompileNode(memberAccess.Value, context),
                memberAccess.MemberName,
                Type.EmptyTypes,
                argExprs);
        }

        var methodExpr = CompileNode(invoke.Delegate, context);
        return Expression.Invoke(methodExpr, argExprs);
    }

    private Expression CompileAwait(Await awaitNode, CompilationContext context) {
        var operand = CompileNode(awaitNode.Operand, context);
        var awaiter = Expression.Call(operand, "GetAwaiter", Type.EmptyTypes);
        return Expression.Call(awaiter, "GetResult", Type.EmptyTypes);
    }

    /// <summary>
    /// Compiles a constructor invocation by selecting the resolved constructor and emitting an
    /// <see cref="Expression.New(System.Reflection.ConstructorInfo, IEnumerable{Expression})"/>.
    /// Optional parameters are padded with their default values when omitted by the AST.
    /// </summary>
    private Expression CompileConstructor(New @new, CompilationContext context) {
        var targetType = GetClrType(@new.Type);
        var resolvedConstructor = _analysisResult.GetResolvedMember(@new) as ITypeConstructor;

        if (resolvedConstructor is ClrConstructor clrConstructor) {
            var arguments = BuildConstructorArguments(clrConstructor, @new.Arguments, context);
            return Expression.New(clrConstructor.ConstructorInfo, arguments);
        }

        throw new InvalidOperationException($"Constructor '{@new.Type}' could not be resolved to a CLR constructor.");
    }

    private Expression[] BuildConstructorArguments(ITypeConstructor constructor, Node[] providedArguments, CompilationContext context) {
        var parameters = constructor.Parameters.ToArray();
        var result = new Expression[parameters.Length];

        for (var index = 0; index < parameters.Length; index++) {
            if (index < providedArguments.Length) {
                var argument = CompileNode(providedArguments[index], context);
                var parameterType = GetClrType(parameters[index].ParameterTypeDefinition);
                result[index] = argument.Type == parameterType
                    ? argument
                    : Expression.Convert(argument, parameterType);
                continue;
            }

            if (!parameters[index].IsOptional) {
                throw new InvalidOperationException($"Constructor '{constructor}' requires argument '{parameters[index].Name}'.");
            }

            var optionalType = GetClrType(parameters[index].ParameterTypeDefinition);
            result[index] = Expression.Constant(parameters[index].DefaultValue, optionalType);
        }

        return result;
    }

    private static Type GetClrType(ITypeDefinition typeDefinition) {
        ArgumentNullException.ThrowIfNull(typeDefinition);

        return typeDefinition.GetRuntimeType() ?? throw new InvalidOperationException($"Type '{typeDefinition.FullName}' does not have a common language runtime type.");
    }

    /// <summary>
    /// Compiles a <c>try</c>/<c>catch</c>/<c>finally</c> construct by emitting the try block,
    /// compiling each catch body inside its own child lexical scope with the exception variable
    /// bound locally, and then selecting the appropriate LINQ try expression shape based on whether
    /// catch clauses, a finally block, or both are present.
    /// </summary>
    private Expression CompileTryCatchFinally(TryCatchFinally tryCatch, CompilationContext context) {
        var tryBlock = CompileNode(tryCatch.TryBlock, context);

        var catchClauses = tryCatch.CatchClauses?.Select(catchClause => {
            var exceptionType = catchClause.ExceptionType != null
                ? GetClrType(catchClause.ExceptionType)
                : typeof(Exception);

            var exceptionParam = Expression.Parameter(exceptionType, catchClause.VariableName ?? "ex");

            // Create a synthetic Parameter node to bind the exception variable to the cache
            // This allows references to the exception variable in the catch body
            var catchContext = context.CreateChild();
            if (catchClause.VariableName != null) {
                var exceptionVarNode = new Parameter(catchClause.VariableName, catchClause.ExceptionType);
                catchContext.DeclareParameter(exceptionVarNode, exceptionParam, export: false);
            }

            var catchBody = CompileNode(catchClause.Body, catchContext);

            return Expression.Catch(exceptionParam, catchBody);
        }).ToArray() ?? Array.Empty<CatchBlock>();

        var finallyBlock = tryCatch.FinallyBlock != null ? CompileNode(tryCatch.FinallyBlock, context) : null;

        if (catchClauses.Length > 0 && finallyBlock != null) {
            return Expression.TryCatchFinally(tryBlock, finallyBlock, catchClauses);
        }
        else if (catchClauses.Length > 0) {
            return Expression.TryCatch(tryBlock, catchClauses);
        }
        else if (finallyBlock != null) {
            return Expression.TryFinally(tryBlock, finallyBlock);
        }

        return tryBlock;
    }

    /// <summary>
    /// Compiles a <c>using</c> statement into a <c>try/finally</c> pattern where the resource is
    /// evaluated once, the body executes normally, and <c>Dispose()</c> is called in the finally
    /// block when the compiled resource type exposes a disposable cleanup method.
    /// </summary>
    // AOT-safe reference to IDisposable.Dispose, resolved at compile time.
    private static readonly System.Reflection.MethodInfo DisposeMethod =
        Ref<IDisposable>.Method(d => d.Dispose());

    private Expression CompileUsingStatement(UsingStatement usingStmt, CompilationContext context) {
        var resource = CompileNode(usingStmt.Resource, context);
        var resourceType = resource.Type;
        var body = CompileNode(usingStmt.Body, context);

        // using statement is: try { body } finally { resource.Dispose() }
        if (typeof(IDisposable).IsAssignableFrom(resourceType)) {
            var disposeCall = Expression.Call(
                Expression.TypeAs(resource, typeof(IDisposable)),
                DisposeMethod);
            return Expression.TryFinally(body, disposeCall);
        }

        // Fallback: if resource doesn't implement IDisposable, just execute the body
        return body;
    }

    private LabelTarget GetOrCreateLabel(string name, CompilationContext context) {
        if (!context.FunctionLabels.TryGetValue(name, out var label)) {
            label = Expression.Label(name);
            context.FunctionLabels[name] = label;
        }

        return label;
    }
}