using PrimType = Poly.Introspection.PrimitiveType;

namespace Poly.DomainModeling.Lowering;

public sealed partial class DomainToCSharpExporter {
    internal static Node StoreJobObjectType() =>
        new PrimitiveTypeReference(PrimType.Structure);

    internal static Node TypedNull(string typeName) =>
        new TypeCast(new Constant(null),
            new OptionalTypeReference(new NamedTypeReference(typeName)));
}