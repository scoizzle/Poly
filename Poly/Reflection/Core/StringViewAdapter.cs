using Poly.Serialization;

namespace Poly.Reflection;

internal sealed class StringViewAdapter : GenericValueTypeAdapterBase<StringView>
{
    public override bool Deserialize(IDataReader reader, [NotNullWhen(true)] out StringView value)
    {
        return reader.StringView(out value);
    }

    public override bool Serialize(IDataWriter writer, StringView value)
    {
        return writer.StringView(value);
    }
}