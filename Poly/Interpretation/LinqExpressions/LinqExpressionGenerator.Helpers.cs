using System.Linq.Expressions;

using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation.LinqExpressions;

public sealed partial class LinqExpressionGenerator {
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

        var allVariables = variables.Concat(blockContext.HoistedVariables).Distinct().ToArray();
        return Expression.Block(allVariables, compiledNodes);
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

        return Expression.Variable(typeof(object), variable.Name);
    }

    /// <summary>
    /// Compiles a variable reference by resolving it from the nearest lexical scope.
    /// Undeclared variables fail closed; declare on <see cref="Block.Variables"/> or foreach.
    /// </summary>
    private ParameterExpression CompileVariable(Variable variable, CompilationContext context) {
        if (context.TryGetVariable(variable, out var existing)) {
            return existing;
        }

        throw new InvalidOperationException(
            $"Variable '{variable.Name}' is not declared in this scope");
    }

    private Expression CompileVariableUse(Variable variable, CompilationContext context) =>
        CompileVariable(variable, context);

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

}