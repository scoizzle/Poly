using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using Poly.DataModeling.Builders;
using Poly.DataModeling.Mutations;
using Poly.DataModeling.TypeExpressions;
using Poly.Validation;

namespace Poly.DataModeling;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(DataModel))]
[JsonSerializable(typeof(DataType))]
[JsonSerializable(typeof(DataProperty))]
// TypeExpression hierarchy
[JsonSerializable(typeof(TypeExpression))]
[JsonSerializable(typeof(PrimitiveType))]
[JsonSerializable(typeof(OptionalType))]
[JsonSerializable(typeof(CollectionType))]
[JsonSerializable(typeof(MapType))]
[JsonSerializable(typeof(ReferenceType))]
[JsonSerializable(typeof(UnionType))]
[JsonSerializable(typeof(TupleType))]
[JsonSerializable(typeof(EnumType))]
// Relationships
[JsonSerializable(typeof(Relationship))]
[JsonSerializable(typeof(OneToOneRelationship))]
[JsonSerializable(typeof(OneToManyRelationship))]
[JsonSerializable(typeof(ManyToOneRelationship))]
[JsonSerializable(typeof(ManyToManyRelationship))]
[JsonSerializable(typeof(InheritanceRelationship))]
[JsonSerializable(typeof(AssociationRelationship))]
// Constraint hierarchy
[JsonSerializable(typeof(Constraint))]
[JsonSerializable(typeof(RangeConstraint))]
[JsonSerializable(typeof(NotNullConstraint))]
[JsonSerializable(typeof(LengthConstraint))]
[JsonSerializable(typeof(Validation.Constraints.EqualityConstraint))]
[JsonSerializable(typeof(ValueSourceComparisonConstraint))]
// ValueSource hierarchy
[JsonSerializable(typeof(ValueSource))]
[JsonSerializable(typeof(ConstantValue))]
[JsonSerializable(typeof(ParameterValue))]
[JsonSerializable(typeof(PropertyValue))]
internal partial class SourceGenerationContext : JsonSerializerContext;


public sealed class DataModelPropertyPolymorphicJsonTypeResolver : DefaultJsonTypeInfoResolver {
    public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        JsonTypeInfo jsonTypeInfo = base.GetTypeInfo(type, options);

        // TypeExpression polymorphism
        if (jsonTypeInfo.Type == typeof(TypeExpression)) {
            jsonTypeInfo.PolymorphismOptions = new JsonPolymorphismOptions {
                TypeDiscriminatorPropertyName = "$type",
                IgnoreUnrecognizedTypeDiscriminators = true,
                UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
                DerivedTypes =
                {
                    new JsonDerivedType(typeof(PrimitiveType), "Primitive"),
                    new JsonDerivedType(typeof(OptionalType), "Optional"),
                    new JsonDerivedType(typeof(CollectionType), "Collection"),
                    new JsonDerivedType(typeof(MapType), "Map"),
                    new JsonDerivedType(typeof(ReferenceType), "Reference"),
                    new JsonDerivedType(typeof(UnionType), "Union"),
                    new JsonDerivedType(typeof(TupleType), "Tuple"),
                    new JsonDerivedType(typeof(EnumType), "Enum")
                }
            };
        }

        // Relationship polymorphism
        if (jsonTypeInfo.Type == typeof(Relationship)) {
            jsonTypeInfo.PolymorphismOptions = new JsonPolymorphismOptions {
                TypeDiscriminatorPropertyName = "$type",
                IgnoreUnrecognizedTypeDiscriminators = true,
                UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
                DerivedTypes =
                {
                    new JsonDerivedType(typeof(OneToOneRelationship), "OneToOne"),
                    new JsonDerivedType(typeof(OneToManyRelationship), "OneToMany"),
                    new JsonDerivedType(typeof(ManyToOneRelationship), "ManyToOne"),
                    new JsonDerivedType(typeof(ManyToManyRelationship), "ManyToMany"),
                    new JsonDerivedType(typeof(InheritanceRelationship), "Inheritance"),
                    new JsonDerivedType(typeof(AssociationRelationship), "Association")
                }
            };
        }

        // Constraint polymorphism
        if (jsonTypeInfo.Type == typeof(Constraint)) {
            jsonTypeInfo.PolymorphismOptions = new JsonPolymorphismOptions {
                TypeDiscriminatorPropertyName = "Type",
                IgnoreUnrecognizedTypeDiscriminators = true,
                UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
                DerivedTypes =
                {
                    new JsonDerivedType(typeof(RangeConstraint), "Range"),
                    new JsonDerivedType(typeof(NotNullConstraint), "NotNull"),
                    new JsonDerivedType(typeof(LengthConstraint), "Length"),
                    new JsonDerivedType(typeof(Validation.Constraints.EqualityConstraint), "Equality"),
                    new JsonDerivedType(typeof(ValueSourceComparisonConstraint), "ValueSourceComparison")
                }
            };
        }

        // ValueSource polymorphism
        if (jsonTypeInfo.Type == typeof(ValueSource)) {
            jsonTypeInfo.PolymorphismOptions = new JsonPolymorphismOptions {
                TypeDiscriminatorPropertyName = "$type",
                IgnoreUnrecognizedTypeDiscriminators = true,
                UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
                DerivedTypes =
                {
                    new JsonDerivedType(typeof(ConstantValue), "Constant"),
                    new JsonDerivedType(typeof(ParameterValue), "Parameter"),
                    new JsonDerivedType(typeof(PropertyValue), "Property")
                }
            };
        }

        return jsonTypeInfo;
    }

    public static DataModelPropertyPolymorphicJsonTypeResolver Shared { get; } = new DataModelPropertyPolymorphicJsonTypeResolver();
}