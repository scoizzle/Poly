using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

using PrimType = Poly.Introspection.PrimitiveType;
using Syntactic = Poly.Ast.Nodes;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Lowering;

// Properties: property/field emission, value-type shapes, and property default lowering.
public sealed partial class DomainToCSharpExporter {
    internal static TypeDefinitionNode BuildValueTypeTypeDef(
        ValueType valueType, Domain domain, INodeMetadataProvider metadata) {
        var props = new List<PropertyDefinitionNode>();
        foreach (var prop in valueType.Properties) {
            var propRef = MapDomainTypeRef(prop.Type, domain, metadata);
            props.Add(new PropertyDefinitionNode(
                prop.Name, propRef,
                Getter: new PropertyGetterDefinitionNode(),
                Setter: new PropertySetterDefinitionNode(),
                Initializer: IsNonNullableReferenceScalar(propRef)
                    ? new Syntactic.PropertyInitializerDefinitionNode(
                        new Syntactic.NullForgiving(new Syntactic.Default()))
                    : null
            ));
        }
        return new TypeDefinitionNode(
            valueType.Name,
            Properties: props,
            Semantics: Syntactic.TypeDefinitionSemantics.ImmutableValue
        );
    }

    /// <summary>
    /// True when the mapped CLR type is a non-nullable reference type (currently the
    /// only one is <c>Text → string</c>), which the parameterless EF-materialization
    /// ctor leaves unset and would otherwise raise CS8618.
    /// </summary>
    private static bool IsNonNullableReferenceScalar(Node propRef) =>
        propRef is PrimitiveTypeReference { PrimitiveId: PrimType.String };

    private static bool IsDomainValueType(Domain domain, string typeName) =>
        domain.Types.OfType<ValueType>().Any(v =>
            string.Equals(v.Name, typeName, StringComparison.Ordinal));

    /// <summary>
    /// Lowers a <see cref="DefaultValueConstraint"/> to its C# constant/enum node,
    /// used as an optional-parameter default on the Create/CreateNav/ctor signatures.
    /// Returns null for runtime defaults (now/today/guid) — those use a
    /// <c>T? = null</c> sentinel and a <c>?? &lt;runtime&gt;</c> coalesce in the ctor
    /// body. Shared with the create-in call site so signature and call stay in lockstep.
    /// </summary>
    internal static Node? LowerDefaultConstantNode(
        DefaultValueConstraint defaultValue,
        Property prop,
        Domain domain,
        INodeMetadataProvider metadata) {
        if (defaultValue.Expression is Literal lit)
            return new Constant(lit.Value);
        if (defaultValue.Expression is PropertyAccess pa) {
            if (TryResolveEnumType(domain, metadata, prop.Type.TypeName, out var enumType)
                && enumType is not null
                && enumType.MemberNames.Contains(pa.Name, StringComparer.Ordinal)) {
                return new Member(new NamedTypeReference(enumType.Name), pa.Name);
            }
            // Non-keyword, non-enum-member PropertyAccess default cannot be lowered —
            // fail loud instead of silently dropping the constraint (which would turn
            // the property into a required Create parameter).
            throw new NotSupportedException(
                $"default({pa.Name}) on property '{prop.Name}' (type '{prop.Type.TypeName}') cannot be lowered: " +
                $"'{pa.Name}' is not a member of an enum that '{prop.Name}' is typed with.");
        }
        return null;
    }
}