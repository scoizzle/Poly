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
            Equal eq => Expression.Equal(CompileNode(eq.LeftHandValue), CompileNode(eq.RightHandValue)),
            NotEqual neq => Expression.NotEqual(CompileNode(neq.LeftHandValue), CompileNode(neq.RightHandValue)),
            LessThan lt => Expression.LessThan(CompileNode(lt.LeftHandValue), CompileNode(lt.RightHandValue)),
            LessThanOrEqual lte => Expression.LessThanOrEqual(CompileNode(lte.LeftHandValue), CompileNode(lte.RightHandValue)),
            GreaterThan gt => Expression.GreaterThan(CompileNode(gt.LeftHandValue), CompileNode(gt.RightHandValue)),
            GreaterThanOrEqual gte => Expression.GreaterThanOrEqual(CompileNode(gte.LeftHandValue), CompileNode(gte.RightHandValue)),

            // Boolean operations
            And and => Expression.AndAlso(CompileNode(and.LeftHandValue), CompileNode(and.RightHandValue)),
            Or or => Expression.OrElse(CompileNode(or.LeftHandValue), CompileNode(or.RightHandValue)),

            // Conditional
            Conditional cond => Expression.Condition(
                CompileNode(cond.Condition),
                CompileNode(cond.IfTrue),
                CompileNode(cond.IfFalse)),

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

        // Apply numeric type promotion if needed
        if (_analysisResult.GetResolvedType(leftNode) is ClrTypeDefinition leftType &&
            _analysisResult.GetResolvedType(rightNode) is ClrTypeDefinition rightType) {
            var promotedType = GetPromotedNumericType(leftType.Type, rightType.Type);
            if (promotedType != null) {
                leftExpr = leftExpr.Type == promotedType ? leftExpr : Expression.Convert(leftExpr, promotedType);
                rightExpr = rightExpr.Type == promotedType ? rightExpr : Expression.Convert(rightExpr, promotedType);
            }
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
        if (_analysisResult.GetResolvedType(node) is not ClrTypeDefinition typeDef)
            throw new InvalidOperationException($"Type for node '{node}' was not resolved by semantic analysis.");

        return typeDef.Type;
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
}