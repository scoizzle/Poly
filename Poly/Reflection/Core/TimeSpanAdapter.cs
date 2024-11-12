using Poly.Serialization;

namespace Poly.Reflection;

internal sealed class TimeSpanAdapter : GenericValueTypeAdapterBase<TimeSpan>
{
    public override bool Serialize<TWriter>(TWriter writer, TimeSpan value)
    {
        return writer.TimeSpan(value);
    }

    public override bool Deserialize<TReader>(TReader reader, out TimeSpan value)
    {
        return reader.TimeSpan(out value);
    }
}