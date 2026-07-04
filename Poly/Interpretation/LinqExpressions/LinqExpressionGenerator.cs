using System.Linq.Expressions;

using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation.LinqExpressions;

/// <summary>
/// Generates LINQ Expression trees from analyzed AST nodes for testing and compilation purposes.
/// </summary>
/// <remarks>
/// This class consumes an AnalysisResult (output from the semantic analysis system) and compiles
/// AST nodes into executable LINQ Expression trees. It's primarily useful for testing the analysis
/// system and generating lambda expressions from interpreted code.
/// </remarks>
public sealed class LinqExpressionGenerator {
    private readonly AnalysisResult _analysisResult;

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
            parent: this,
            state: State,
            functionLabels: FunctionLabels,
            currentBreakLabel: CurrentBreakLabel,
            currentContinueLabel: CurrentContinueLabel,
            hasReturnScope: HasReturnScope);

        public CompilationContext CreateBlockScope() => new(
            parent: this,
            state: State,
            functionLabels: FunctionLabels,
            currentBreakLabel: CurrentBreakLabel,
            currentContinueLabel: CurrentContinueLabel,
            hasReturnScope: true);

        public CompilationContext CreateLoopScope(LabelTarget breakLabel, LabelTarget continueLabel) => new(
            parent: this,
            state: State,
            functionLabels: FunctionLabels,
            currentBreakLabel: breakLabel,
            currentContinueLabel: continueLabel,
            hasReturnScope: HasReturnScope);

        public CompilationContext CreateLambdaScope() => new(
            parent: this,
            state: State,
            functionLabels: [],
            currentBreakLabel: null,
            currentContinueLabel: null,
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

    /// <summary>
    /// Initializes a new instance of the <see cref="LinqExpressionGenerator"/> class.
    /// </summary>
    /// <param name="analysisResult">The semantic analysis result containing type and member information.</param>
    public LinqExpressionGenerator(AnalysisResult analysisResult) {
        ArgumentNullException.ThrowIfNull(analysisResult);
        _analysisResult = analysisResult;
    }

    /// <summary>
    /// Compiles an AST node to a LINQ Expression.
    /// </summary>
    /// <param name="node">The AST node to compile.</param>
    /// <returns>The compiled expression and any generated parameters needed by consumers.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="node"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the expression cannot be compiled.</exception>
    public CompilationResult Compile(Node node) {
        ArgumentNullException.ThrowIfNull(node);
        return ExecuteCompilation(context => new CompilationResult(
            CompileNode(node, context),
            context.State.ExportedParameters.ToArray()));
    }

    /// <summary>
    /// Compiles an AST node to a lambda expression with the specified parameter.
    /// </summary>
    /// <param name="node">The AST node to compile as the lambda body.</param>
    /// <param name="parameter">The lambda parameter.</param>
    /// <returns>A compiled lambda expression.</returns>
    /// <exception cref="ArgumentNullException">Thrown when arguments are null.</exception>
    public LambdaExpression CompileAsLambda(Node node, Parameter parameter) {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(parameter);
        return ExecuteCompilation(context => CompileAsLambdaCore(node, parameter, context));
    }

    /// <summary>
    /// Compiles an AST node to a lambda expression with the specified parameters.
    /// </summary>
    /// <param name="node">The AST node to compile as the lambda body.</param>
    /// <param name="parameters">The lambda parameters.</param>
    /// <returns>A compiled lambda expression.</returns>
    /// <exception cref="ArgumentNullException">Thrown when arguments are null.</exception>
    public LambdaExpression CompileAsLambda(Node node, params Parameter[] parameters) {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(parameters);

        if (parameters.Length == 0) {
            throw new ArgumentException("At least one parameter must be provided.", nameof(parameters));
        }

        return ExecuteCompilation(context => CompileAsLambdaCore(node, parameters, context));
    }

    /// <summary>
    /// Compiles an AST node and returns a compiled delegate that can be invoked.
    /// </summary>
    /// <param name="node">The AST node to compile.</param>
    /// <param name="parameter">The lambda parameter.</param>
    /// <returns>A compiled and invokable delegate.</returns>
    /// <exception cref="ArgumentNullException">Thrown when arguments are null.</exception>
    public Delegate CompileAsDelegate(Node node, Parameter parameter) {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(parameter);
        return ExecuteCompilation(context => CompileAsLambdaCore(node, parameter, context).Compile());
    }

    /// <summary>
    /// Compiles an AST node and returns a compiled delegate that can be invoked.
    /// </summary>
    /// <param name="node">The AST node to compile.</param>
    /// <param name="parameters">The lambda parameters.</param>
    /// <returns>A compiled and invokable delegate.</returns>
    /// <exception cref="ArgumentNullException">Thrown when arguments are null.</exception>
    public Delegate CompileAsDelegate(Node node, params Parameter[] parameters) {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(parameters);

        return ExecuteCompilation(context => CompileAsLambdaCore(node, parameters, context).Compile());
    }

    /// <summary>
    /// Compiles an AST node and returns a strongly-typed compiled delegate.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate type to compile to (must be a Func or Action).</typeparam>
    /// <param name="node">The AST node to compile as the lambda body.</param>
    /// <param name="parameters">The lambda parameters.</param>
    /// <returns>A compiled and invokable strongly-typed delegate.</returns>
    /// <exception cref="ArgumentNullException">Thrown when arguments are null.</exception>
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

    /// <summary>
    /// Compiles a node into a single-parameter lambda by first emitting the body expression,
    /// then resolving the already-declared lexical parameter that should become the lambda's
    /// formal argument.
    /// </summary>
    private LambdaExpression CompileAsLambdaCore(Node node, Parameter parameter, CompilationContext context) {
        var bodyExpr = CompileNode(node, context);

        if (!context.TryGetParameter(parameter, out var paramExpr)) {
            throw new InvalidOperationException($"Parameter '{parameter.Name}' must be part of the context used for compilation.");
        }

        return Expression.Lambda(bodyExpr, paramExpr);
    }

    /// <summary>
    /// Compiles a node into a multi-parameter lambda by emitting the body once and then binding
    /// the requested lexical parameters, in order, as the lambda's formal parameter list.
    /// </summary>
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

    /// <summary>
    /// Compiles a single AST node into its LINQ expression equivalent.
    /// High-level nodes may first be lowered by analysis metadata, custom compilers get first
    /// chance to handle the node, and otherwise the method dispatches to the node-specific
    /// compiler that emits the corresponding expression-tree shape.
    /// </summary>
    private Expression CompileNode(Node node, CompilationContext context) {
        // Check if this node has a replacement from analysis passes (e.g., DataModel transforms)
        // This allows analyzers to lower high-level nodes to core nodes without mutating the AST
        var replacement = _analysisResult.GetNodeReplacement(node);
        if (replacement != null) {
            node = replacement;
        }

        return node switch {
            // Leaf nodes
            Constant constant => Expression.Constant(constant.Value),
            Variable variable => CompileVariable(variable, context),
            Parameter parameter => CompileParameter(parameter, context),

            // Binary arithmetic operations
            Add add => CompileBinaryArithmetic(add.LeftHandValue, add.RightHandValue, Expression.Add, context),
            Subtract sub => CompileBinaryArithmetic(sub.LeftHandValue, sub.RightHandValue, Expression.Subtract, context),
            Multiply mul => CompileBinaryArithmetic(mul.LeftHandValue, mul.RightHandValue, Expression.Multiply, context),
            Divide div => CompileBinaryArithmetic(div.LeftHandValue, div.RightHandValue, Expression.Divide, context),
            Modulo mod => CompileBinaryArithmetic(mod.LeftHandValue, mod.RightHandValue, Expression.Modulo, context),

            // Unary operations
            UnaryMinus minus => Expression.Negate(CompileNode(minus.Operand, context)),
            Not not => Expression.Not(CompileNode(not.Value, context)),

            // Comparison operations
            Equal eq => CompileBinaryComparison(eq.LeftHandValue, eq.RightHandValue, Expression.Equal, context),
            NotEqual neq => CompileBinaryComparison(neq.LeftHandValue, neq.RightHandValue, Expression.NotEqual, context),
            LessThan lt => CompileBinaryComparison(lt.LeftHandValue, lt.RightHandValue, Expression.LessThan, context),
            LessThanOrEqual lte => CompileBinaryComparison(lte.LeftHandValue, lte.RightHandValue, Expression.LessThanOrEqual, context),
            GreaterThan gt => CompileBinaryComparison(gt.LeftHandValue, gt.RightHandValue, Expression.GreaterThan, context),
            GreaterThanOrEqual gte => CompileBinaryComparison(gte.LeftHandValue, gte.RightHandValue, Expression.GreaterThanOrEqual, context),

            // Boolean operations
            And and => Expression.AndAlso(CompileNode(and.LeftHandValue, context), CompileNode(and.RightHandValue, context)),
            Or or => Expression.OrElse(CompileNode(or.LeftHandValue, context), CompileNode(or.RightHandValue, context)),

            // Conditional
            Conditional cond => CompileConditional(cond, context),

            // Member and index access
            Member member => Expression.PropertyOrField(CompileNode(member.Value, context), member.MemberName),
            IndexAccess index => CompileIndexAccess(index, context),

            // Await (synchronous extraction for simulation)
            Await awaitNode => CompileAwait(awaitNode, context),

            // Method invocation
            Invoke method => CompileInvocation(method, context),

            // Constructor invocation
            New @new => CompileConstructor(@new, context),

            // Type reference
            TypeReference => Expression.Constant(null),

            // Type cast
            TypeCast cast => CompileTypeCast(cast, context),
            TypeIs typeIs => CompileTypeIs(typeIs, context),
            TypeAs typeAs => CompileTypeAs(typeAs, context),

            // Coalesce
            Coalesce coalesce => CompileCoalesce(coalesce, context),

            // Block
            Block block => CompileBlock(block, context),

            // Assignment
            Assignment assign => CompileAssignment(assign, context),

            // Control flow - conditionals
            IfStatement ifStmt => CompileIfStatement(ifStmt, context),
            SwitchStatement switchStmt => CompileSwitchStatement(switchStmt, context),

            // Control flow - loops
            WhileLoop whileLoop => CompileWhileLoop(whileLoop, context),
            DoWhileLoop doWhileLoop => CompileDoWhileLoop(doWhileLoop, context),
            ForLoop forLoop => CompileForLoop(forLoop, context),
            ForEachLoop forEachLoop => CompileForEachLoop(forEachLoop, context),

            // Control flow - jumps
            BreakStatement breakStmt => CompileBreakStatement(breakStmt, context),
            ContinueStatement continueStmt => CompileContinueStatement(continueStmt, context),
            GotoStatement gotoStmt => Expression.Goto(GetOrCreateLabel(gotoStmt.Target, context)),
            LabelDeclaration labelDecl => CompileLabelDeclaration(labelDecl, context),
            Return returnStmt => CompileReturnStatement(returnStmt, context),

            // Exception handling
            ThrowStatement throwStmt => Expression.Throw(CompileNode(throwStmt.Exception, context)),
            TryCatchFinally tryCatch => CompileTryCatchFinally(tryCatch, context),

            // Resource management
            UsingStatement usingStmt => CompileUsingStatement(usingStmt, context),

            // First-class functions
            Lambda lambda => CompileLambda(lambda, context),

            // Leaf references
            ThisReference thisRef => Expression.Default(GetClrType(thisRef)),
            NullForgiving nf => CompileNode(nf.Operand, context),
            Default d => d.TargetType != null
                ? Expression.Default(GetClrType(d.TargetType))
                : Expression.Default(GetClrType(d)),
            ParameterReference => Expression.Default(typeof(object)),

            _ => throw new InvalidOperationException($"Unsupported node type: {node.GetType().Name}")
        };
    }

    /// <summary>
    /// Compiles a block into an <see cref="Expression.Block(IEnumerable{ParameterExpression}, IEnumerable{Expression})"/>.
    /// Block-scoped locals are declared up front in a child lexical scope, child nodes are
    /// emitted in sequence, and the outermost return-owning block closes the shared return label
    /// so nested <c>return</c> nodes terminate within the correct expression-tree scope.
    /// </summary>
    private BlockExpression CompileBlock(Block block, CompilationContext context) {
        var blockContext = context.CreateBlockScope();

        var variables = block.Variables
            .Select(v => v switch {
                Variable variable => blockContext.DeclareVariable(variable, CreateVariableExpression(variable)),
                Parameter parameter => blockContext.DeclareParameter(parameter, CreateParameterExpression(parameter), export: false),
                _ => throw new InvalidOperationException("Block variables must be Variable or Parameter nodes.")
            })
            .ToArray();

        // DCE: avoid lowering elidable (pure unused) nodes. Always keep the last (block result).
        var nodesToCompile = new List<Node>();
        for (int i = 0; i < block.Nodes.Count; i++) {
            var n = block.Nodes[i];
            if (i == block.Nodes.Count - 1 || (_analysisResult != null && !_analysisResult.CanElide(n))) {
                nodesToCompile.Add(n);
            }
        }
        var compiledNodes = nodesToCompile.Select(n => CompileNode(n, blockContext)).ToList();

        // The outermost block that introduced a "return" label closes it here so that
        // Return nodes nested anywhere inside (including in ForEachLoop bodies) have a
        // valid label target within the same expression-tree scope.
        if (!context.HasReturnScope && blockContext.FunctionLabels.TryGetValue("return", out var returnLabel)) {
            blockContext.FunctionLabels.Remove("return");
            compiledNodes.Add(Expression.Label(returnLabel, Expression.Default(returnLabel.Type)));
        }

        return Expression.Block(variables, compiledNodes);
    }

    /// <summary>
    /// Compiles an assignment into <see cref="Expression.Assign(Expression, Expression)"/>.
    /// The destination is resolved as a writable variable, parameter, or member/index expression,
    /// and the value is converted when necessary so the emitted assignment matches the storage type.
    /// </summary>
    private Expression CompileAssignment(Assignment assignment, CompilationContext context) {
        Expression destination = assignment.Destination switch {
            Variable variable => CompileVariable(variable, context),
            Parameter parameter => CompileParameter(parameter, context),
            _ => CompileNode(assignment.Destination, context)
        };

        var valueExpr = CompileNode(assignment.Value, context);

        if (destination is ParameterExpression param && valueExpr.Type != param.Type) {
            valueExpr = Expression.Convert(valueExpr, param.Type);
        }

        return Expression.Assign(destination, valueExpr);
    }

    /// <summary>
    /// Compiles a comparison such as <c>a == b</c> or <c>a &lt; b</c> by first emitting both sides,
    /// applying numeric promotion when required, and then invoking the supplied comparison factory
    /// to produce the final binary expression.
    /// </summary>
    private Expression CompileBinaryComparison(
        Node leftNode,
        Node rightNode,
        Func<Expression, Expression, BinaryExpression> factory,
        CompilationContext context) {
        var leftExpr = CompileNode(leftNode, context);
        var rightExpr = CompileNode(rightNode, context);

        var promotedType = GetPromotedNumericType(leftExpr.Type, rightExpr.Type);
        if (promotedType != null) {
            leftExpr = leftExpr.Type == promotedType ? leftExpr : Expression.Convert(leftExpr, promotedType);
            rightExpr = rightExpr.Type == promotedType ? rightExpr : Expression.Convert(rightExpr, promotedType);
        }

        return factory(leftExpr, rightExpr);
    }

    /// <summary>
    /// Compiles a ternary conditional of the form <c>condition ? ifTrue : ifFalse</c>.
    /// Each branch is emitted independently and, when possible, converted to a common type so the
    /// resulting <see cref="Expression.Condition(Expression, Expression, Expression)"/> is type-correct.
    /// </summary>
    private Expression CompileConditional(Conditional cond, CompilationContext context) {
        var condition = CompileNode(cond.Condition, context);
        var ifTrue = CompileNode(cond.IfTrue, context);
        var ifFalse = CompileNode(cond.IfFalse, context);

        // Ensure both branches have compatible types
        var commonType = GetCommonType(ifTrue.Type, ifFalse.Type);
        if (commonType != null) {
            ifTrue = ifTrue.Type == commonType ? ifTrue : Expression.Convert(ifTrue, commonType);
            ifFalse = ifFalse.Type == commonType ? ifFalse : Expression.Convert(ifFalse, commonType);
        }

        return Expression.Condition(condition, ifTrue, ifFalse);
    }

    /// <summary>
    /// Compiles arithmetic such as <c>a + b</c>, <c>a - b</c>, or <c>a * b</c> by emitting both operands,
    /// handling special cases like string concatenation, applying numeric promotion, and then using
    /// the provided factory to build the final binary arithmetic expression.
    /// </summary>
    private Expression CompileBinaryArithmetic(
        Node leftNode,
        Node rightNode,
        Func<Expression, Expression, BinaryExpression> factory,
        CompilationContext context) {
        var leftExpr = CompileNode(leftNode, context);
        var rightExpr = CompileNode(rightNode, context);

        // Handle string concatenation explicitly
        if (leftExpr.Type == typeof(string) && rightExpr.Type == typeof(string)) {
            var concat = Ref.Method(() => string.Concat(null!, null!));
            return Expression.Call(concat, leftExpr, rightExpr);
        }

        var promotedType = GetPromotedNumericType(leftExpr.Type, rightExpr.Type);
        if (promotedType != null) {
            leftExpr = leftExpr.Type == promotedType ? leftExpr : Expression.Convert(leftExpr, promotedType);
            rightExpr = rightExpr.Type == promotedType ? rightExpr : Expression.Convert(rightExpr, promotedType);
        }

        return factory(leftExpr, rightExpr);
    }

    private static Type? GetPromotedNumericType(Type left, Type right) {
        // C# numeric promotion rules
        if (left == typeof(decimal) || right == typeof(decimal)) return typeof(decimal);
        if (left == typeof(double) || right == typeof(double)) return typeof(double);
        if (left == typeof(float) || right == typeof(float)) return typeof(float);
        if (left == typeof(ulong) || right == typeof(ulong)) return typeof(ulong);
        if (left == typeof(long) || right == typeof(long)) return typeof(long);
        if (left == typeof(uint) || right == typeof(uint)) return typeof(uint);

        // For int, short, byte, sbyte, ushort -> promote to int
        var numericTypes = new[] { typeof(int), typeof(short), typeof(byte), typeof(sbyte), typeof(ushort) };
        if (numericTypes.Contains(left) || numericTypes.Contains(right)) return typeof(int);

        return null;
    }

    private static Type? GetCommonType(Type left, Type right) {
        // Same types are already compatible
        if (left == right) return left;

        // Handle void types (for statements)
        if (left == typeof(void) || right == typeof(void)) return null;

        // Numeric promotion
        var promoted = GetPromotedNumericType(left, right);
        if (promoted != null) return promoted;

        // Reference types - find common base or interface
        if (!left.IsValueType && !right.IsValueType) {
            // If one is assignable to the other, use the more general one
            if (left.IsAssignableFrom(right)) return left;
            if (right.IsAssignableFrom(left)) return right;

            // Otherwise, use object as the common type
            return typeof(object);
        }

        // Nullable types
        var leftUnderlying = Nullable.GetUnderlyingType(left);
        var rightUnderlying = Nullable.GetUnderlyingType(right);

        if (leftUnderlying != null && rightUnderlying != null) {
            // Both nullable - promote underlying types
            var commonUnderlying = GetCommonType(leftUnderlying, rightUnderlying);
            return commonUnderlying != null ? typeof(Nullable<>).MakeGenericType(commonUnderlying) : null;
        }
        else if (leftUnderlying != null && right.IsValueType) {
            // Left is nullable, right is value type
            var commonUnderlying = GetCommonType(leftUnderlying, right);
            return commonUnderlying != null ? typeof(Nullable<>).MakeGenericType(commonUnderlying) : null;
        }
        else if (rightUnderlying != null && left.IsValueType) {
            // Right is nullable, left is value type
            var commonUnderlying = GetCommonType(left, rightUnderlying);
            return commonUnderlying != null ? typeof(Nullable<>).MakeGenericType(commonUnderlying) : null;
        }

        // No common type found
        return null;
    }

    /// <summary>
    /// Compiles a null-coalescing expression of the form <c>left ?? right</c>.
    /// The emitted expression normalizes the left and right operands to compatible nullable or
    /// reference types before producing the final <see cref="Expression.Coalesce(Expression, Expression)"/>.
    /// </summary>
    private Expression CompileCoalesce(Coalesce coalesce, CompilationContext context) {
        var leftExpr = CompileNode(coalesce.LeftHandValue, context);
        var rightExpr = CompileNode(coalesce.RightHandValue, context);

        var rightType = (_analysisResult.GetResolvedType(coalesce.RightHandValue) as ClrTypeDefinition)?.RuntimeType ?? rightExpr.Type;

        // For value types, ensure the left side is nullable to allow coalesce
        if (rightType.IsValueType && Nullable.GetUnderlyingType(rightType) is null) {
            var nullableRight = typeof(Nullable<>).MakeGenericType(rightType);
            leftExpr = leftExpr.Type == nullableRight ? leftExpr : Expression.Convert(leftExpr, nullableRight);
            rightExpr = rightExpr.Type == rightType ? rightExpr : Expression.Convert(rightExpr, rightType);
            return Expression.Coalesce(leftExpr, rightExpr);
        }

        // Reference types or nullable value types
        leftExpr = leftExpr.Type == rightType ? leftExpr : Expression.Convert(leftExpr, rightType);
        rightExpr = rightExpr.Type == rightType ? rightExpr : Expression.Convert(rightExpr, rightType);
        return Expression.Coalesce(leftExpr, rightExpr);
    }

    private Type GetClrType(Node node) {
        var typeDef = _analysisResult.GetResolvedType(node);
        if (typeDef == null)
            throw new InvalidOperationException($"Type for node '{node}' was not resolved by semantic analysis.");

        return typeDef.GetRuntimeType() ?? throw new InvalidOperationException($"Type '{typeDef.FullName}' does not have a common language runtime type.");
    }

    private ParameterExpression CreateParameterExpression(Parameter parameter) {
        var type = GetClrType(parameter);
        return Expression.Parameter(type, parameter.Name);
    }

    /// <summary>
    /// Compiles a parameter reference by resolving it lexically when already in scope, or by
    /// declaring it at the root compilation scope when it is a free parameter that must be exposed
    /// to callers as part of the compilation result.
    /// </summary>
    private ParameterExpression CompileParameter(Parameter parameter, CompilationContext context) {
        if (context.TryGetParameter(parameter, out var existing)) {
            return existing;
        }

        // Free parameters belong to the root compilation scope so they can be surfaced
        // to callers while still remaining visible to nested lexical scopes.
        return context.GetRoot().DeclareParameter(parameter, CreateParameterExpression(parameter), export: true);
    }

    private ParameterExpression CreateVariableExpression(Variable variable) {
        var resolvedType = _analysisResult.GetResolvedType(variable) as ClrTypeDefinition;
        if (resolvedType?.RuntimeType is Type runtimeType) {
            return Expression.Variable(runtimeType, variable.Name);
        }

        if (variable.Value is Constant { Value: not null } constant) {
            return Expression.Variable(constant.Value.GetType(), variable.Name);
        }

        return Expression.Variable(typeof(object), variable.Name);
    }

    /// <summary>
    /// Compiles a variable reference by resolving it from the nearest lexical scope, or by
    /// declaring a new local variable expression in the current scope when the variable represents
    /// a writable local introduced by the surrounding construct.
    /// </summary>
    private ParameterExpression CompileVariable(Variable variable, CompilationContext context) {
        if (context.TryGetVariable(variable, out var existing)) {
            return existing;
        }

        return context.DeclareVariable(variable, CreateVariableExpression(variable));
    }

    /// <summary>
    /// Compiles an index access such as <c>target[index]</c>.
    /// Arrays become writable array-access expressions, indexer properties become
    /// <see cref="Expression.MakeIndex(Expression, System.Reflection.PropertyInfo, IEnumerable{Expression})"/>,
    /// and other fallback shapes are emitted as array indexing when possible.
    /// </summary>
    private Expression CompileIndexAccess(IndexAccess indexAccess, CompilationContext context) {
        var target = CompileNode(indexAccess.Value, context);
        var indices = indexAccess.Arguments.Select(arg => CompileNode(arg, context)).ToArray();

        if (target.Type.IsArray) {
            // Use ArrayAccess so the expression is writable and can be used on the left side of Assignment.
            return Expression.ArrayAccess(target, indices);
        }
        else {
            var indexerProperty = target.Type.GetProperties()
                .FirstOrDefault(p => p.GetIndexParameters().Length > 0);

            if (indexerProperty != null) {
                return Expression.MakeIndex(target, indexerProperty, indices);
            }

            return Expression.ArrayIndex(target, indices);
        }
    }

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