using Poly.DomainModeling.TypeExpressions;
using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.AbstractSyntaxTree.TypeDefinitions;

using AstCollectionKind = Poly.Interpretation.AbstractSyntaxTree.TypeDefinitions.CollectionKind;
using DataModelCollectionKind = Poly.DomainModeling.TypeExpressions.CollectionKind;

namespace Poly.DomainModeling;

/// <summary>
/// Extension methods for converting DataModel types to AST representations.
/// </summary>
public static class DataModelAstExtensions {
    /// <summary>
    /// Converts a DataModel to a list of TypeDefinitionNode AST nodes.
    /// </summary>
    public static IReadOnlyList<TypeDefinitionNode> ToAst(this DataModel model)
    {
        return model.Types.Select(t => t.ToAst()).ToList();
    }

    /// <summary>
    /// Converts a DataType to a TypeDefinitionNode AST node.
    /// </summary>
    public static TypeDefinitionNode ToAst(this DataType type)
    {
        var properties = type.Properties
            .Select(p => p.ToAst())
            .ToList();

        return new TypeDefinitionNode(
            Name: type.Name,
            Properties: properties,
            TypeCategory: Introspection.TypeCategory.None
        );
    }

    /// <summary>
    /// Converts a DataProperty to a PropertyDefinitionNode AST node.
    /// </summary>
    public static PropertyDefinitionNode ToAst(this DataProperty property)
    {
        var typeNode = property.Type.ToAst();
        var defaultValueNode = property.DefaultValue != null
            ? new Constant(property.DefaultValue)
            : null;

        // TODO: Convert constraints to AST nodes when constraint AST is defined
        return new PropertyDefinitionNode(
            Name: property.Name,
            PropertyType: typeNode,
            DefaultValue: defaultValueNode
        );
    }

    /// <summary>
    /// Converts a TypeExpression to an AST Node representing the type.
    /// </summary>
    public static Node ToAst(this TypeExpression typeExpr)
    {
        return typeExpr switch {
            PrimitiveType prim => new PrimitiveTypeReference(prim.Id),
            OptionalType opt => new OptionalTypeReference(opt.Inner.ToAst()),
            CollectionType col => new CollectionTypeReference(
                col.Element.ToAst(),
                col.Kind.ToAstKind()
            ),
            MapType map => new MapTypeReference(
                map.Key.ToAst(),
                map.Value.ToAst()
            ),
            ReferenceType refType => new NamedTypeReference(refType.TypeName),
            EnumType enumType => new NamedTypeReference(
                enumType.EnumName,
                TypeArguments: enumType.Values
                    .Select(v => (Node)new Constant(v))
                    .ToList()
            ),
            TupleType tuple => new NamedTypeReference(
                "Tuple",
                TypeArguments: tuple.Elements
                    .Select(e => e.ToAst())
                    .ToList()
            ),
            UnionType union => new NamedTypeReference(
                "Union",
                TypeArguments: union.Cases
                    .Select(v => v.ToAst())
                    .ToList()
            ),
            _ => throw new NotSupportedException($"Unsupported TypeExpression: {typeExpr.GetType().Name}")
        };
    }

    /// <summary>
    /// Converts DataModeling CollectionKind to AST CollectionKind.
    /// </summary>
    public static AstCollectionKind ToAstKind(this DataModelCollectionKind kind)
    {
        return kind switch {
            DataModelCollectionKind.Array => AstCollectionKind.Array,
            DataModelCollectionKind.List => AstCollectionKind.List,
            DataModelCollectionKind.Set => AstCollectionKind.Set,
            _ => AstCollectionKind.List
        };
    }
}