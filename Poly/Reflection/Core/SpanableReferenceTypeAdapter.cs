using Poly.Serialization;

namespace Poly.Reflection;

public class SpanableReferenceTypeAdapter<T> : GenericReferenceTypeAdapterBase<T> where T : class, ISpanParsable<T>, ISpanFormattable
{
    public override bool Deserialize(IDataReader reader, [NotNullWhen(returnValue: true)] out T? value)
    {
        return reader.Read(out value);
    }

    public override bool Serialize(IDataWriter writer, T? value) => value switch
    {
        null => writer.Null(),
        _ => writer.Write(value)
    };
}