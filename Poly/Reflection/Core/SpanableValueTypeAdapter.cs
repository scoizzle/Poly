using Poly.Serialization;

namespace Poly.Reflection;

public class SpanableValueTypeAdapter<T> : GenericValueTypeAdapterBase<T> where T : struct, ISpanParsable<T>, ISpanFormattable
{
    public override bool Deserialize(IDataReader reader, [NotNullWhen(returnValue: true)] out T value)
    {
        return reader.Read(out value);
    }

    public override bool Serialize(IDataWriter writer, T value)
    {
        return writer.Write(value);
    }
}