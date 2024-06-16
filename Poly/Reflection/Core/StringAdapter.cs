using Poly.Serialization;

namespace Poly.Reflection;

internal sealed class StringAdapter : GenericReferenceTypeAdapterBase<string>
{
    public override bool Deserialize(IDataReader reader, [NotNullWhen(true)] out string? value)
    {
        return reader.String(out value);
    }

    public override bool Serialize(IDataWriter writer, string? value) => value switch
    {
        null => writer.Null(),
        _ => writer.String(value)
    };
}