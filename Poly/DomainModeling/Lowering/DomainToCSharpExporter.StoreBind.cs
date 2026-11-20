using Poly.Interpretation.CSharp;

namespace Poly.DomainModeling.Lowering;

public sealed partial class DomainToCSharpExporter {
    internal static Node StoreJobObjectType() =>
        ClrTypeReferences.Of(typeof(object));

    internal static Node TypedNull(string typeName) =>
        new TypeCast(new Constant(null),
            new OptionalTypeReference(new NamedTypeReference(typeName)));
}