namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents an anonymous function (lambda expression) in an interpretation tree.
/// </summary>
/// <remarks>
/// Defines a callable unit with a parameter list and a body expression. Compiles to a
/// <see cref="Exprs.LambdaExpression"/> which can be invoked via
/// <see cref="Invoke"/> or compiled into a delegate.
/// <para>
/// Lambda nodes introduce a new return scope: <see cref="ReturnStatement.Return"/> nodes
/// inside the body exit this lambda, not any enclosing expression.
/// </para>
/// </remarks>
/// <param name="Parameters">The parameters accepted by this lambda.</param>
/// <param name="Body">The body expression evaluated when the lambda is invoked.</param>
public sealed record Lambda(IReadOnlyList<Parameter> Parameters, Node Body) : Operator {
    public override IEnumerable<Node?> Children {
        get {
            foreach (var p in Parameters) yield return p;
            yield return Body;
        }
    }

    public override string ToString() {
        var paramList = string.Join(", ", Parameters);
        return $"({paramList}) => {Body}";
    }
}