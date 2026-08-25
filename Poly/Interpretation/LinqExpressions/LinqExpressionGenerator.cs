using System.Linq.Expressions;

using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation.LinqExpressions;

/// <summary>Generates LINQ Expression trees from analyzed AST nodes for testing
/// and legacy compilation purposes. This is a test/reference path — NOT the
/// canonical semantics backend.</summary>
/// <remarks>
/// <para>Consumes an <see cref="AnalysisResult"/> (output from the semantic
/// analysis system) and compiles AST nodes into executable LINQ Expression trees.
/// Primarily useful for testing the analysis system and generating lambda
/// expressions from interpreted code.</para>
/// <para>New language semantics should land in analysis → direct lowering
/// (<see cref="DirectVmAbiEmitter"/>) first. Parity tests may continue to
/// use this path for cross-validation.</para>
/// </remarks>
public sealed partial class LinqExpressionGenerator {
    private readonly AnalysisResult _analysisResult;

    /// <summary>Holds the compiled LINQ Expression and the set of parameters
    /// that need to be bound before invocation.</summary>
    /// <param name="Expression">The compiled expression tree.</param>
    /// <param name="Parameters">The parameter expressions captured during
    /// compilation (e.g. for lambda parameters).</param>
    public sealed record CompilationResult(Expression Expression, IReadOnlyList<ParameterExpression> Parameters);

    private sealed class CompilationState {
        private readonly List<ParameterExpression> _exportedParameters = [];
        private readonly HashSet<ParameterExpression> _exportedParameterSet = [];

        public IReadOnlyList<ParameterExpression> ExportedParameters => _exportedParameters;

        public void ExportParameter(ParameterExpression parameter) {
            if (_exportedParameterSet.Add(parameter)) {
                _exportedParameters.Add(parameter);
            }
        }
    }

    private sealed class CompilationContext {
        private readonly Dictionary<Variable, ParameterExpression> _localVariables = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Parameter, ParameterExpression> _localParameters = new(ReferenceEqualityComparer.Instance);

        private CompilationContext(
            CompilationContext? parent,
            CompilationState state,
            Dictionary<string, LabelTarget> functionLabels,
            LabelTarget? currentBreakLabel,
            LabelTarget? currentContinueLabel,
            bool hasReturnScope) {
            Parent = parent;
            State = state;
            FunctionLabels = functionLabels;
            CurrentBreakLabel = currentBreakLabel;
            CurrentContinueLabel = currentContinueLabel;
            HasReturnScope = hasReturnScope;
        }

        public CompilationContext? Parent { get; }
        public CompilationState State { get; }
        public Dictionary<string, LabelTarget> FunctionLabels { get; }
        public LabelTarget? CurrentBreakLabel { get; }
        public LabelTarget? CurrentContinueLabel { get; }
        public bool HasReturnScope { get; }

        public static CompilationContext CreateRoot() => new(
            parent: null,
            state: new CompilationState(),
            functionLabels: [],
            currentBreakLabel: null,
            currentContinueLabel: null,
            hasReturnScope: false);

        public CompilationContext CreateChild() => new(
            parent: this, state: State, functionLabels: FunctionLabels,
            currentBreakLabel: CurrentBreakLabel, currentContinueLabel: CurrentContinueLabel,
            hasReturnScope: HasReturnScope);

        public CompilationContext CreateBlockScope() => new(
            parent: this, state: State, functionLabels: FunctionLabels,
            currentBreakLabel: CurrentBreakLabel, currentContinueLabel: CurrentContinueLabel,
            hasReturnScope: true);

        public CompilationContext CreateLoopScope(LabelTarget breakLabel, LabelTarget continueLabel) => new(
            parent: this, state: State, functionLabels: FunctionLabels,
            currentBreakLabel: breakLabel, currentContinueLabel: continueLabel,
            hasReturnScope: HasReturnScope);

        public CompilationContext CreateLambdaScope() => new(
            parent: this, state: State, functionLabels: [],
            currentBreakLabel: null, currentContinueLabel: null,
            hasReturnScope: false);

        public bool TryGetVariable(Variable variable, out ParameterExpression expression) {
            if (_localVariables.TryGetValue(variable, out var localExpression)) {
                expression = localExpression;
                return true;
            }
            if (Parent != null) {
                return Parent.TryGetVariable(variable, out expression);
            }
            expression = null!;
            return false;
        }

        public bool TryGetParameter(Parameter parameter, out ParameterExpression expression) {
            if (_localParameters.TryGetValue(parameter, out var localExpression)) {
                expression = localExpression;
                return true;
            }
            if (Parent != null) {
                return Parent.TryGetParameter(parameter, out expression);
            }
            expression = null!;
            return false;
        }

        public ParameterExpression DeclareVariable(Variable variable, ParameterExpression expression) {
            _localVariables[variable] = expression;
            return expression;
        }

        public ParameterExpression DeclareParameter(Parameter parameter, ParameterExpression expression, bool export) {
            _localParameters[parameter] = expression;
            if (export) {
                State.ExportParameter(expression);
            }
            return expression;
        }

        public CompilationContext GetRoot() => Parent is null ? this : Parent.GetRoot();
    }

    public LinqExpressionGenerator(AnalysisResult analysisResult) {
        ArgumentNullException.ThrowIfNull(analysisResult);
        _analysisResult = analysisResult;
    }

    public CompilationResult Compile(Node node) {
        ArgumentNullException.ThrowIfNull(node);
        return ExecuteCompilation(context => new CompilationResult(
            CompileNode(node, context),
            context.State.ExportedParameters.ToArray()));
    }

    public LambdaExpression CompileAsLambda(Node node, Parameter parameter) {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(parameter);
        return ExecuteCompilation(context => CompileAsLambdaCore(node, parameter, context));
    }

    public LambdaExpression CompileAsLambda(Node node, params Parameter[] parameters) {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.Length == 0) {
            throw new ArgumentException("At least one parameter must be provided.", nameof(parameters));
        }
        return ExecuteCompilation(context => CompileAsLambdaCore(node, parameters, context));
    }

    public Delegate CompileAsDelegate(Node node, Parameter parameter) {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(parameter);
        return ExecuteCompilation(context => CompileAsLambdaCore(node, parameter, context).Compile());
    }

    public Delegate CompileAsDelegate(Node node, params Parameter[] parameters) {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(parameters);
        return ExecuteCompilation(context => CompileAsLambdaCore(node, parameters, context).Compile());
    }

    public TDelegate CompileAsDelegate<TDelegate>(Node node, params Parameter[] parameters)
        where TDelegate : Delegate {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(parameters);
        return ExecuteCompilation(context => (TDelegate)(object)CompileAsLambdaCore(node, parameters, context).Compile());
    }

    private static TResult ExecuteCompilation<TResult>(Func<CompilationContext, TResult> compile) {
        ArgumentNullException.ThrowIfNull(compile);
        return compile(CompilationContext.CreateRoot());
    }

    private LambdaExpression CompileAsLambdaCore(Node node, Parameter parameter, CompilationContext context) {
        var bodyExpr = CompileNode(node, context);
        if (!context.TryGetParameter(parameter, out var paramExpr)) {
            throw new InvalidOperationException($"Parameter '{parameter.Name}' must be part of the context used for compilation.");
        }
        return Expression.Lambda(bodyExpr, paramExpr);
    }

    private LambdaExpression CompileAsLambdaCore(Node node, Parameter[] parameters, CompilationContext context) {
        var bodyExpr = CompileNode(node, context);
        var paramExpressions = parameters.Select(param => {
            if (!context.TryGetParameter(param, out var expression)) {
                throw new InvalidOperationException($"Parameter '{param.Name}' must be part of the context used for compilation.");
            }
            return expression;
        }).ToArray();
        return Expression.Lambda(bodyExpr, paramExpressions);
    }

    private Expression CompileNode(Node node, CompilationContext context) {
        var replacement = _analysisResult.GetNodeReplacement(node);
        if (replacement != null) {
            node = replacement;
        }
        return node switch {
            Constant constant => Expression.Constant(constant.Value),
            Variable variable => CompileVariable(variable, context),
            Parameter parameter => CompileParameter(parameter, context),
            Add add => CompileBinaryArithmetic(add.LeftHandValue, add.RightHandValue, Expression.Add, context),
            Subtract sub => CompileBinaryArithmetic(sub.LeftHandValue, sub.RightHandValue, Expression.Subtract, context),
            Multiply mul => CompileBinaryArithmetic(mul.LeftHandValue, mul.RightHandValue, Expression.Multiply, context),
            Divide div => CompileBinaryArithmetic(div.LeftHandValue, div.RightHandValue, Expression.Divide, context),
            Modulo mod => CompileBinaryArithmetic(mod.LeftHandValue, mod.RightHandValue, Expression.Modulo, context),
            UnaryMinus minus => Expression.Negate(CompileNode(minus.Operand, context)),
            Not not => Expression.Not(CompileNode(not.Value, context)),
            Equal eq => CompileBinaryComparison(eq.LeftHandValue, eq.RightHandValue, Expression.Equal, context),
            NotEqual neq => CompileBinaryComparison(neq.LeftHandValue, neq.RightHandValue, Expression.NotEqual, context),
            LessThan lt => CompileBinaryComparison(lt.LeftHandValue, lt.RightHandValue, Expression.LessThan, context),
            LessThanOrEqual lte => CompileBinaryComparison(lte.LeftHandValue, lte.RightHandValue, Expression.LessThanOrEqual, context),
            GreaterThan gt => CompileBinaryComparison(gt.LeftHandValue, gt.RightHandValue, Expression.GreaterThan, context),
            GreaterThanOrEqual gte => CompileBinaryComparison(gte.LeftHandValue, gte.RightHandValue, Expression.GreaterThanOrEqual, context),
            And and => Expression.AndAlso(CompileNode(and.LeftHandValue, context), CompileNode(and.RightHandValue, context)),
            Or or => Expression.OrElse(CompileNode(or.LeftHandValue, context), CompileNode(or.RightHandValue, context)),
            Conditional cond => CompileConditional(cond, context),
            Member member => Expression.PropertyOrField(CompileNode(member.Value, context), member.MemberName),
            IndexAccess index => CompileIndexAccess(index, context),
            Await awaitNode => CompileAwait(awaitNode, context),
            Invoke method => CompileInvocation(method, context),
            New @new => CompileConstructor(@new, context),
            TypeReference => Expression.Constant(null),
            TypeCast cast => CompileTypeCast(cast, context),
            TypeIs typeIs => CompileTypeIs(typeIs, context),
            TypeAs typeAs => CompileTypeAs(typeAs, context),
            Coalesce coalesce => CompileCoalesce(coalesce, context),
            Block block => CompileBlock(block, context),
            Assignment assign => CompileAssignment(assign, context),
            IfStatement ifStmt => CompileIfStatement(ifStmt, context),
            SwitchStatement switchStmt => CompileSwitchStatement(switchStmt, context),
            WhileLoop whileLoop => CompileWhileLoop(whileLoop, context),
            DoWhileLoop doWhileLoop => CompileDoWhileLoop(doWhileLoop, context),
            ForLoop forLoop => CompileForLoop(forLoop, context),
            ForEachLoop forEachLoop => CompileForEachLoop(forEachLoop, context),
            BreakStatement breakStmt => CompileBreakStatement(breakStmt, context),
            ContinueStatement continueStmt => CompileContinueStatement(continueStmt, context),
            GotoStatement gotoStmt => Expression.Goto(GetOrCreateLabel(gotoStmt.Target, context)),
            LabelDeclaration labelDecl => CompileLabelDeclaration(labelDecl, context),
            Return returnStmt => CompileReturnStatement(returnStmt, context),
            ThrowStatement throwStmt => Expression.Throw(CompileNode(throwStmt.Exception, context)),
            TryCatchFinally tryCatch => CompileTryCatchFinally(tryCatch, context),
            UsingStatement usingStmt => CompileUsingStatement(usingStmt, context),
            Lambda lambda => CompileLambda(lambda, context),
            ThisReference thisRef => Expression.Default(GetClrType(thisRef)),
            NullForgiving nf => CompileNode(nf.Operand, context),
            Default d => d.TargetType != null
                ? Expression.Default(GetClrType(d.TargetType))
                : Expression.Default(GetClrType(d)),
            ParameterReference => Expression.Default(typeof(object)),
            BitwiseAnd ba => Expression.And(CompileNode(ba.LeftHandValue, context), CompileNode(ba.RightHandValue, context)),
            BitwiseOr bo => Expression.Or(CompileNode(bo.LeftHandValue, context), CompileNode(bo.RightHandValue, context)),
            BitwiseXor bx => Expression.ExclusiveOr(CompileNode(bx.LeftHandValue, context), CompileNode(bx.RightHandValue, context)),
            BitwiseNot bn => Expression.Not(CompileNode(bn.Operand, context)),
            ShiftLeft sl => Expression.LeftShift(CompileNode(sl.LeftHandValue, context), CompileNode(sl.RightHandValue, context)),
            ShiftRight sr => Expression.RightShift(CompileNode(sr.LeftHandValue, context), CompileNode(sr.RightHandValue, context)),
            PopCount pc => Expression.Call(null,
                typeof(System.Numerics.BitOperations).GetMethod(nameof(System.Numerics.BitOperations.PopCount), [typeof(ulong)])!,
                Expression.Convert(CompileNode(pc.Operand, context), typeof(ulong))),
            SuspendNode sn => CompileNode(sn.Inner, context),
            CallExternal ce => throw new InvalidOperationException(
                $"CallExternal '{ce.MethodName}' is VM-host ABI; the LINQ path does not execute it."),
            _ => throw new InvalidOperationException($"Unsupported node type: {node.GetType().Name}")
        };
    }
}