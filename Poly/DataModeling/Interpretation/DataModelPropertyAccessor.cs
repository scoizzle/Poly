using Poly.Interpretation;
using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Introspection;

namespace Poly.DataModeling.Interpretation;

/// <summary>
/// Accesses a dynamic object's property by name using IDictionary<string, object?> semantics.
/// </summary>
internal sealed record DataModelPropertyAccessor(Node Instance, string PropertyName, ITypeDefinition MemberType) : Node {
    public override TResult Transform<TResult>(ITransformer<TResult> transformer) => throw new NotSupportedException("DataModelPropertyAccessor transformation is not supported.");

    public override string ToString() => $"{Instance}.{PropertyName}";
}