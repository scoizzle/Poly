using System.Linq.Expressions;

using Poly.Interpretation.AbstractSyntaxTree.Arithmetic;
using Poly.Interpretation.AbstractSyntaxTree.Boolean;
using Poly.Interpretation.AbstractSyntaxTree.Comparison;
using Poly.Interpretation.AbstractSyntaxTree.Equality;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;
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
    private readonly Dictionary<Variable, ParameterExpression> _variableCache = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Parameter, ParameterExpression> _parameterCache = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, LabelTarget> _labelMap = new();
    private readonly List<INodeCompiler> _customCompilers = new();
    private LabelTarget? _currentBreakLabel;
    private LabelTarget? _currentContinueLabel;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinqExpressionGenerator"/> class.
    /// </summary>
    /// <param name="analysisResult">The semantic analysis result containing type and member information.</param>
    public LinqExpressionGenerator(AnalysisResult analysisResult)
    {
        ArgumentNullException.ThrowIfNull(analysisResult);
        _analysisResult = analysisResult;
    }

    /// <summary>
    /// Registers a custom compiler for handling domain-specific node types.
    /// </summary>
    /// <param name="compiler">The compiler to register.</param>
    /// <returns>This generator for fluent chaining.</returns>
    public LinqExpressionGenerator RegisterCompiler(INodeCompiler compiler)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        _customCompilers.Add(compiler);
        return this;
    }

    /// <summary>
    /// Compiles an AST node to a LINQ Expression.
    /// </summary>
    /// <param name="node">The AST node to compile.</param>
    /// <returns>The compiled LINQ Expression.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="node"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the expression cannot be compiled.</exception>
    public Expression Compile(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return CompileNode(node);
    }

    /// <summary>
    /// Compiles an AST node to a lambda expression with the specified parameter.
    /// </summary>
    /// <param name="node">The AST node to compile as the lambda body.</param>
    /// <param name="parameter">The lambda parameter.</param>
    /// <returns>A compiled lambda expression.</returns>
    /// <exception cref="ArgumentNullException">Thrown when arguments are null.</exception>
    public LambdaExpression CompileAsLambda(Node node, Parameter parameter)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(parameter);

        var bodyExpr = CompileNode(node);

        if (!_parameterCache.TryGetValue(parameter, out var paramExpr)) {
            throw new InvalidOperationException($"Parameter '{parameter.Name}' must be part of the context used for compilation.");
        }

        return Expression.Lambda(bodyExpr, paramExpr);
    }

    /// <summary>
    /// Compiles an AST node to a lambda expression with the specified parameters.
    /// </summary>
    /// <param name="node">The AST node to compile as the lambda body.</param>
    /// <param name="parameters">The lambda parameters.</param>
    /// <returns>A compiled lambda expression.</returns>
    /// <exception cref="ArgumentNullException">Thrown when arguments are null.</exception>
    public LambdaExpression CompileAsLambda(Node node, params Parameter[] parameters)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(parameters);

        if (parameters.Length == 0) {
            throw new ArgumentException("At least one parameter must be provided.", nameof(parameters));
        }

        var bodyExpr = CompileNode(node);
        var paramExpressions = parameters.Select(param => {
            return _parameterCache[param];
        }).ToArray();

        return Expression.Lambda(bodyExpr, paramExpressions);
    }

    /// <summary>
    /// Compiles an AST node and returns a compiled delegate that can be invoked.
    /// </summary>
    /// <param name="node">The AST node to compile.</param>
    /// <param name="parameter">The lambda parameter.</param>
    /// <returns>A compiled and invokable delegate.</returns>
    /// <exception cref="ArgumentNullException">Thrown when arguments are null.</exception>
    public Delegate CompileAsDelegate(Node node, Parameter parameter)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(parameter);

        var lambda = CompileAsLambda(node, parameter);
        return lambda.Compile();
    }

    /// <summary>
    /// Compiles an AST node and returns a compiled delegate that can be invoked.
    /// </summary>
    /// <param name="node">The AST node to compile.</param>
    /// <param name="parameters">The lambda parameters.</param>
    /// <returns>A compiled and invokable delegate.</returns>
    /// <exception cref="ArgumentNullException">Thrown when arguments are null.</exception>
    public Delegate CompileAsDelegate(Node node, params Parameter[] parameters)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(parameters);

        var lambda = CompileAsLambda(node, parameters);
        return lambda.Compile();
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
        where TDelegate : Delegate
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(parameters);

        var lambda = CompileAsLambda(node, parameters);
        return (TDelegate)(object)lambda.Compile();
    }

    private Expression CompileNode(Node node)
    {
        // Check if this node has a replacement from analysis passes (e.g., DataModel transforms)
        // This allows analyzers to transform nodes without modifying the original AST
        var allMetadata = _analysisResult.GetAllMetadata(node);
        foreach (var metadata in allMetadata) {
            var replacementProperty = metadata.GetType().GetProperty("Replacement");
            if (replacementProperty?.PropertyType.IsAssignableTo(typeof(Node)) == true) {
                if (replacementProperty.GetValue(metadata) is Node replacement) {
                    node = replacement;
                    break;
                }
            }
        }

        // Try custom compilers (allows external systems to handle their node types)
        foreach (var compiler in _customCompilers) {
            if (compiler.TryCompile(node, CompileNode, out var customExpr)) {
                return customExpr!;
            }
        }

        return node switch {
            // Leaf nodes
            Constant constant => Expression.Constant(constant.Value),
            Variable variable => CompileVariable(variable),
            Parameter parameter => CompileParameter(parameter),

            // Binary arithmetic operations
            Add add => CompileBinaryArithmetic(add.LeftHandValue, add.RightHandValue, Expression.Add),
            Subtract sub => CompileBinaryArithmetic(sub.LeftHandValue, sub.RightHandValue, Expression.Subtract),
            Multiply mul => CompileBinaryArithmetic(mul.LeftHandValue, mul.RightHandValue, Expression.Multiply),
            Divide div => CompileBinaryArithmetic(div.LeftHandValue, div.RightHandValue, Expression.Divide),
            Modulo mod => CompileBinaryArithmetic(mod.LeftHandValue, mod.RightHandValue, Expression.Modulo),

            // Unary operations
            UnaryMinus minus => Expression.Negate(CompileNode(minus.Operand)),
            Not not => Expression.Not(CompileNode(not.Value)),

            // Comparison operations
            Equal eq => CompileBinaryComparison(eq.LeftHandValue, eq.RightHandValue, Expression.Equal),
            NotEqual neq => CompileBinaryComparison(neq.LeftHandValue, neq.RightHandValue, Expression.NotEqual),
            LessThan lt => CompileBinaryComparison(lt.LeftHandValue, lt.RightHandValue, Expression.LessThan),
            LessThanOrEqual lte => CompileBinaryComparison(lte.LeftHandValue, lte.RightHandValue, Expression.LessThanOrEqual),
            GreaterThan gt => CompileBinaryComparison(gt.LeftHandValue, gt.RightHandValue, Expression.GreaterThan),
            GreaterThanOrEqual gte => CompileBinaryComparison(gte.LeftHandValue, gte.RightHandValue, Expression.GreaterThanOrEqual),

            // Boolean operations
            And and => Expression.AndAlso(CompileNode(and.LeftHandValue), CompileNode(and.RightHandValue)),
            Or or => Expression.OrElse(CompileNode(or.LeftHandValue), CompileNode(or.RightHandValue)),

            // Conditional
            Conditional cond => CompileConditional(cond),

            // Member and index access
            MemberAccess member => Expression.PropertyOrField(CompileNode(member.Value), member.MemberName),
            IndexAccess index => CompileIndexAccess(index),

            // Method invocation
            MethodInvocation method => Expression.Call(
                method.Target != null ? CompileNode(method.Target) : null!,
                method.MethodName,
                Type.EmptyTypes,
                method.Arguments.Select(arg => CompileNode(arg)).ToArray()),

            // Type reference
            TypeReference => Expression.Constant(null),

            // Type cast
            TypeCast cast => CompileTypeCast(cast),

            // Coalesce
            Coalesce coalesce => CompileCoalesce(coalesce),

            // Block
            Block block => Expression.Block(
                block.Variables.Select(v => v switch {
                    Variable variable => CompileVariable(variable),
                    Parameter parameter => CompileParameter(parameter),
                    _ => throw new InvalidOperationException("Block variables must be Variable or Parameter nodes.")
                }).ToArray(),
                block.Nodes.Select(n => CompileNode(n)).ToArray()),

            // Assignment
            Assignment assign => CompileAssignment(assign),

            // Control flow - conditionals
            IfStatement ifStmt => CompileIfStatement(ifStmt),
            SwitchStatement switchStmt => CompileSwitchStatement(switchStmt),

            // Control flow - loops
            WhileLoop whileLoop => CompileWhileLoop(whileLoop),
            DoWhileLoop doWhileLoop => CompileDoWhileLoop(doWhileLoop),
            ForLoop forLoop => CompileForLoop(forLoop),

            // Control flow - jumps
            BreakStatement breakStmt => CompileBreakStatement(breakStmt),
            ContinueStatement continueStmt => CompileContinueStatement(continueStmt),
            GotoStatement gotoStmt => Expression.Goto(GetOrCreateLabel(gotoStmt.Target)),
            LabelDeclaration labelDecl => CompileLabelDeclaration(labelDecl),
            ReturnStatement returnStmt => CompileReturnStatement(returnStmt),

            // Exception handling
            ThrowStatement throwStmt => Expression.Throw(CompileNode(throwStmt.Exception)),
            TryCatchFinally tryCatch => CompileTryCatchFinally(tryCatch),

            // Resource management
            UsingStatement usingStmt => CompileUsingStatement(usingStmt),

            _ => throw new InvalidOperationException($"Unsupported node type: {node.GetType().Name}")
        };
    }

    private Expression CompileAssignment(Assignment assignment)
    {
        Expression destination = assignment.Destination switch {
            Variable variable => CompileVariable(variable),
            Parameter parameter => CompileParameter(parameter),
            _ => CompileNode(assignment.Destination)
        };

        var valueExpr = CompileNode(assignment.Value);

        if (destination is ParameterExpression param && valueExpr.Type != param.Type) {
            valueExpr = Expression.Convert(valueExpr, param.Type);
        }

        return Expression.Assign(destination, valueExpr);
    }

    private Expression CompileBinaryComparison(
        Node leftNode,
        Node rightNode,
        Func<Expression, Expression, BinaryExpression> factory)
    {
        var leftExpr = CompileNode(leftNode);
        var rightExpr = CompileNode(rightNode);

        var promotedType = GetPromotedNumericType(leftExpr.Type, rightExpr.Type);
        if (promotedType != null) {
            leftExpr = leftExpr.Type == promotedType ? leftExpr : Expression.Convert(leftExpr, promotedType);
            rightExpr = rightExpr.Type == promotedType ? rightExpr : Expression.Convert(rightExpr, promotedType);
        }

        return factory(leftExpr, rightExpr);
    }

    private Expression CompileConditional(Conditional cond)
    {
        var condition = CompileNode(cond.Condition);
        var ifTrue = CompileNode(cond.IfTrue);
        var ifFalse = CompileNode(cond.IfFalse);

        // Ensure both branches have compatible types
        var commonType = GetCommonType(ifTrue.Type, ifFalse.Type);
        if (commonType != null) {
            ifTrue = ifTrue.Type == commonType ? ifTrue : Expression.Convert(ifTrue, commonType);
            ifFalse = ifFalse.Type == commonType ? ifFalse : Expression.Convert(ifFalse, commonType);
        }

        return Expression.Condition(condition, ifTrue, ifFalse);
    }

    private Expression CompileBinaryArithmetic(
        Node leftNode,
        Node rightNode,
        Func<Expression, Expression, BinaryExpression> factory)
    {
        var leftExpr = CompileNode(leftNode);
        var rightExpr = CompileNode(rightNode);

        // Handle string concatenation explicitly
        if (leftExpr.Type == typeof(string) && rightExpr.Type == typeof(string)) {
            var concat = typeof(string).GetMethod(nameof(string.Concat), new[] { typeof(string), typeof(string) })
                ?? throw new InvalidOperationException("string.Concat overload not found.");
            return Expression.Call(concat, leftExpr, rightExpr);
        }

        var promotedType = GetPromotedNumericType(leftExpr.Type, rightExpr.Type);
        if (promotedType != null) {
            leftExpr = leftExpr.Type == promotedType ? leftExpr : Expression.Convert(leftExpr, promotedType);
            rightExpr = rightExpr.Type == promotedType ? rightExpr : Expression.Convert(rightExpr, promotedType);
        }

        return factory(leftExpr, rightExpr);
    }

    private static Type? GetPromotedNumericType(Type left, Type right)
    {
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

    private static Type? GetCommonType(Type left, Type right)
    {
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

    private Expression CompileCoalesce(Coalesce coalesce)
    {
        var leftExpr = CompileNode(coalesce.LeftHandValue);
        var rightExpr = CompileNode(coalesce.RightHandValue);

        var rightType = (_analysisResult.GetResolvedType(coalesce.RightHandValue) as ClrTypeDefinition)?.Type ?? rightExpr.Type;

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

    private Type GetClrType(Node node)
    {
        var typeDef = _analysisResult.GetResolvedType(node);
        if (typeDef == null)
            throw new InvalidOperationException($"Type for node '{node}' was not resolved by semantic analysis.");

        // Prefer ClrTypeDefinition, but fall back to ReflectedType for non-CLR types like DataModels
        return typeDef is ClrTypeDefinition clrTypeDef
            ? clrTypeDef.Type
            : typeDef.ReflectedType;
    }

    private ParameterExpression CompileParameter(Parameter parameter)
    {
        if (_parameterCache.TryGetValue(parameter, out var existing)) {
            return existing;
        }

        var type = GetClrType(parameter);
        var paramExpr = Expression.Parameter(type, parameter.Name);
        _parameterCache[parameter] = paramExpr;
        return paramExpr;
    }

    private ParameterExpression CompileVariable(Variable variable)
    {
        if (_variableCache.TryGetValue(variable, out var existing)) {
            return existing;
        }

        var clrType = (_analysisResult.GetResolvedType(variable) as ClrTypeDefinition)?.Type ?? typeof(object);
        var paramExpr = Expression.Variable(clrType, variable.Name);
        _variableCache[variable] = paramExpr;
        return paramExpr;
    }

    private Expression CompileIndexAccess(IndexAccess indexAccess)
    {
        var target = CompileNode(indexAccess.Value);
        var indices = indexAccess.Arguments.Select(arg => CompileNode(arg)).ToArray();

        if (target.Type.IsArray) {
            return Expression.ArrayIndex(target, indices);
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

    private Expression CompileTypeCast(TypeCast typeCast)
    {
        var operand = CompileNode(typeCast.Operand);
        var type = GetClrType(typeCast);
        return typeCast.IsChecked
            ? Expression.ConvertChecked(operand, type)
            : Expression.Convert(operand, type);
    }

    private Expression CompileIfStatement(IfStatement ifStmt)
    {
        var condition = CompileNode(ifStmt.Condition);
        var thenBranch = CompileNode(ifStmt.ThenBranch);

        if (ifStmt.ElseBranch != null) {
            var elseBranch = CompileNode(ifStmt.ElseBranch);
            // For IfThenElse, both branches should have compatible types
            if (thenBranch.Type == elseBranch.Type) {
                return Expression.IfThenElse(condition, thenBranch, elseBranch);
            }
            // If types differ, try to convert to common type
            if (thenBranch.Type == typeof(void))
                return Expression.IfThenElse(condition, thenBranch, elseBranch);
            else if (elseBranch.Type == typeof(void))
                return Expression.IfThenElse(condition, thenBranch, elseBranch);
        }

        // No else branch - use IfThen (returns void)
        return Expression.IfThen(condition, thenBranch);
    }

    private Expression CompileSwitchStatement(SwitchStatement switchStmt)
    {
        var switchValue = CompileNode(switchStmt.Value);
        var switchType = switchValue.Type;

        var cases = switchStmt.Cases.Select(caseNode => {
            var pattern = CompileNode(caseNode.Pattern);
            var body = CompileNode(caseNode.Body);
            // SwitchCase expects Expression array for test values
            return Expression.SwitchCase(body, pattern);
        }).ToArray();

        var defaultCase = switchStmt.DefaultCase != null ? CompileNode(switchStmt.DefaultCase) : null;

        return Expression.Switch(switchType, switchValue, defaultCase, null, cases);
    }

    private Expression CompileWhileLoop(WhileLoop whileLoop)
    {
        var breakLabel = Expression.Label("break");
        var continueLabel = Expression.Label("continue");

        var savedBreak = _currentBreakLabel;
        var savedContinue = _currentContinueLabel;
        _currentBreakLabel = breakLabel;
        _currentContinueLabel = continueLabel;

        var condition = CompileNode(whileLoop.Condition);
        var body = CompileNode(whileLoop.Body);

        _currentBreakLabel = savedBreak;
        _currentContinueLabel = savedContinue;

        var loopBody = Expression.Block(
            Expression.IfThen(
                Expression.Not(condition),
                Expression.Break(breakLabel)),
            body,
            Expression.Label(continueLabel));

        return Expression.Block(
            Expression.Loop(loopBody, breakLabel),
            Expression.Label(breakLabel));
    }

    private Expression CompileDoWhileLoop(DoWhileLoop doWhileLoop)
    {
        var breakLabel = Expression.Label("break");
        var continueLabel = Expression.Label("continue");

        var savedBreak = _currentBreakLabel;
        var savedContinue = _currentContinueLabel;
        _currentBreakLabel = breakLabel;
        _currentContinueLabel = continueLabel;

        var body = CompileNode(doWhileLoop.Body);
        var condition = CompileNode(doWhileLoop.Condition);

        _currentBreakLabel = savedBreak;
        _currentContinueLabel = savedContinue;

        var loopBody = Expression.Block(
            body,
            Expression.Label(continueLabel),
            Expression.IfThen(
                Expression.Not(condition),
                Expression.Break(breakLabel)));

        return Expression.Block(
            Expression.Loop(loopBody, breakLabel),
            Expression.Label(breakLabel));
    }

    private Expression CompileForLoop(ForLoop forLoop)
    {
        var breakLabel = Expression.Label("break");
        var continueLabel = Expression.Label("continue");

        var savedBreak = _currentBreakLabel;
        var savedContinue = _currentContinueLabel;
        _currentBreakLabel = breakLabel;
        _currentContinueLabel = continueLabel;

        var initializer = forLoop.Initializer != null ? CompileNode(forLoop.Initializer) : null;
        var condition = forLoop.Condition != null ? CompileNode(forLoop.Condition) : null;
        var increment = forLoop.Increment != null ? CompileNode(forLoop.Increment) : null;
        var body = CompileNode(forLoop.Body);

        _currentBreakLabel = savedBreak;
        _currentContinueLabel = savedContinue;

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
        blockExpressions.Add(Expression.Label(breakLabel));

        return blockExpressions.Count == 1 ? blockExpressions[0] : Expression.Block(blockExpressions);
    }

    private Expression CompileBreakStatement(BreakStatement breakStmt)
    {
        var label = breakStmt.Label ?? "break";
        return Expression.Break(GetOrCreateLabel(label));
    }

    private Expression CompileContinueStatement(ContinueStatement continueStmt)
    {
        var label = continueStmt.Label ?? "continue";
        return Expression.Continue(GetOrCreateLabel(label));
    }

    private Expression CompileLabelDeclaration(LabelDeclaration labelDecl)
    {
        var label = GetOrCreateLabel(labelDecl.Name);
        var statement = CompileNode(labelDecl.Statement);
        return Expression.Block(
            Expression.Label(label),
            statement);
    }

    private Expression CompileReturnStatement(ReturnStatement returnStmt)
    {
        if (returnStmt.Value != null) {
            var value = CompileNode(returnStmt.Value);
            var returnLabel = GetOrCreateLabel("return");
            return Expression.Return(returnLabel, value);
        }

        var voidReturnLabel = GetOrCreateLabel("return");
        return Expression.Return(voidReturnLabel);
    }

    private Expression CompileTryCatchFinally(TryCatchFinally tryCatch)
    {
        var tryBlock = CompileNode(tryCatch.TryBlock);

        var catchClauses = tryCatch.CatchClauses?.Select(catchClause => {
            var exceptionType = catchClause.ExceptionType != null
                ? GetClrType(catchClause.ExceptionType)
                : typeof(Exception);

            var exceptionParam = Expression.Parameter(exceptionType, catchClause.VariableName ?? "ex");

            // Create a synthetic Parameter node to bind the exception variable to the cache
            // This allows references to the exception variable in the catch body
            if (catchClause.VariableName != null) {
                var exceptionVarNode = new Parameter(catchClause.VariableName, catchClause.ExceptionType);
                _parameterCache[exceptionVarNode] = exceptionParam;
            }

            var catchBody = CompileNode(catchClause.Body);

            // Remove from cache after compilation to avoid pollution
            if (catchClause.VariableName != null) {
                var keyToRemove = _parameterCache.Keys.FirstOrDefault(k => k is Parameter p && p.Name == catchClause.VariableName);
                if (keyToRemove != null)
                    _parameterCache.Remove(keyToRemove);
            }

            return Expression.Catch(exceptionParam, catchBody);
        }).ToArray() ?? Array.Empty<CatchBlock>();

        var finallyBlock = tryCatch.FinallyBlock != null ? CompileNode(tryCatch.FinallyBlock) : null;

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

    private Expression CompileUsingStatement(UsingStatement usingStmt)
    {
        var resourceType = GetClrType(usingStmt.Resource);
        var resource = CompileNode(usingStmt.Resource);
        var body = CompileNode(usingStmt.Body);

        // using statement is: try { body } finally { resource.Dispose() }
        var disposeMethod = resourceType.GetMethod(nameof(IDisposable.Dispose));
        if (disposeMethod != null) {
            // Call Dispose on the compiled resource expression
            var disposeCall = Expression.Call(resource, disposeMethod);
            return Expression.TryFinally(body, disposeCall);
        }

        // Fallback: if no Dispose method found, just execute the body
        return body;
    }

    /// <summary>
    /// Gets the parameter expressions that were created during compilation.
    /// </summary>
    /// <returns>The collection of parameter expressions created.</returns>
    public IEnumerable<ParameterExpression> GetParameters() => _parameterCache.Values;

    private LabelTarget GetOrCreateLabel(string name)
    {
        if (!_labelMap.TryGetValue(name, out var label)) {
            label = Expression.Label(name);
            _labelMap[name] = label;
        }

        return label;
    }
}