using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Poly.DataModeling;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(DataModel))]
[JsonSerializable(typeof(DataType))]
[JsonSerializable(typeof(DataProperty))]
[JsonSerializable(typeof(Int32Property))]
[JsonSerializable(typeof(Int64Property))]
[JsonSerializable(typeof(StringProperty))]
[JsonSerializable(typeof(DecimalProperty))]
[JsonSerializable(typeof(DoubleProperty))]
[JsonSerializable(typeof(BooleanProperty))]
[JsonSerializable(typeof(DateTimeProperty))]
[JsonSerializable(typeof(DateOnlyProperty))]
[JsonSerializable(typeof(TimeOnlyProperty))]
[JsonSerializable(typeof(GuidProperty))]
[JsonSerializable(typeof(EnumProperty))]
[JsonSerializable(typeof(ByteArrayProperty))]
[JsonSerializable(typeof(JsonProperty))]
[JsonSerializable(typeof(Relationship))]
[JsonSerializable(typeof(OneToOneRelationship))]
[JsonSerializable(typeof(OneToManyRelationship))]
[JsonSerializable(typeof(ManyToOneRelationship))]
[JsonSerializable(typeof(ManyToManyRelationship))]
[JsonSerializable(typeof(InheritanceRelationship))]
[JsonSerializable(typeof(AssociationRelationship))]
internal partial class SourceGenerationContext : JsonSerializerContext;


public sealed class DataModelPropertyPolymorphicJsonTypeResolver : DefaultJsonTypeInfoResolver {
    public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options) {
        JsonTypeInfo jsonTypeInfo = base.GetTypeInfo(type, options);

        Type basePointType = typeof(DataProperty);
        if (jsonTypeInfo.Type == basePointType) {
            jsonTypeInfo.PolymorphismOptions = new JsonPolymorphismOptions {
                TypeDiscriminatorPropertyName = "Type",
                IgnoreUnrecognizedTypeDiscriminators = true,
                UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
                DerivedTypes =
                {
                    new JsonDerivedType(typeof(Int32Property), "int32"),
                    new JsonDerivedType(typeof(Int64Property), "int64"),
                    new JsonDerivedType(typeof(StringProperty), "string"),
                    new JsonDerivedType(typeof(DecimalProperty), "decimal"),
                    new JsonDerivedType(typeof(DoubleProperty), "double"),
                    new JsonDerivedType(typeof(BooleanProperty), "bool"),
                    new JsonDerivedType(typeof(DateTimeProperty), "datetime"),
                    new JsonDerivedType(typeof(DateOnlyProperty), "date"),
                    new JsonDerivedType(typeof(TimeOnlyProperty), "time"),
                    new JsonDerivedType(typeof(GuidProperty), "guid"),
                    new JsonDerivedType(typeof(EnumProperty), "enum"),
                    new JsonDerivedType(typeof(ByteArrayProperty), "bytes"),
                    new JsonDerivedType(typeof(JsonProperty), "json")
                }
            };
        }

        Type relationshipBaseType = typeof(Relationship);
        if (jsonTypeInfo.Type == relationshipBaseType) {
            jsonTypeInfo.PolymorphismOptions = new JsonPolymorphismOptions {
                TypeDiscriminatorPropertyName = "Type",
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

        return jsonTypeInfo;
    }

    public static DataModelPropertyPolymorphicJsonTypeResolver Shared { get; } = new DataModelPropertyPolymorphicJsonTypeResolver();
}