using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.AbstractSyntaxTree.Arithmetic;
using Poly.Interpretation.AbstractSyntaxTree.Boolean;
using Poly.Interpretation.AbstractSyntaxTree.Comparison;
using Poly.Interpretation.AbstractSyntaxTree.Equality;

namespace Poly.Interpretation;

/// <summary>
/// Generic transformer interface for converting abstract syntax tree nodes to backend-specific representations.
/// </summary>
public interface ITransformer<out TResult>
{
    /// <summary>
    /// Gets the platform-specific representation of "unit" or "void".
    /// Used when statements don't return meaningful values.
    /// </summary>
    TResult Unit { get; }

    // Literals and constants
    TResult Transform<T>(Constant<T> constant);
    TResult Transform(Variable variable);
    TResult Transform(Parameter parameter);

    // Arithmetic operations
    TResult Transform(Add add);
    TResult Transform(Subtract subtract);
    TResult Transform(Multiply multiply);
    TResult Transform(Divide divide);
    TResult Transform(Modulo modulo);
    TResult Transform(UnaryMinus unaryMinus);

    // Comparison operations
    TResult Transform(Equal equal);
    TResult Transform(NotEqual notEqual);
    TResult Transform(LessThan lessThan);
    TResult Transform(LessThanOrEqual lessThanOrEqual);
    TResult Transform(GreaterThan greaterThan);
    TResult Transform(GreaterThanOrEqual greaterThanOrEqual);

    // Boolean operations
    TResult Transform(And and);
    TResult Transform(Or or);
    TResult Transform(Not not);

    // Control flow
    TResult Transform(Conditional conditional);

    // Member and index access
    TResult Transform(MemberAccess memberAccess);
    TResult Transform(IndexAccess indexAccess);

    // Invocation and assignment
    TResult Transform(MethodInvocation invocation);
    TResult Transform(Assignment assignment);

    // Blocks and types
    TResult Transform(Block block);
    TResult Transform(Coalesce coalesce);
    TResult Transform(TypeCast typeCast);
}