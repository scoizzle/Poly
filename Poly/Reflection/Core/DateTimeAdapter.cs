using Poly.Serialization;

namespace Poly.Reflection;

internal sealed class DateTimeAdapter : GenericValueTypeAdapterBase<DateTime>
{
    public override bool Serialize<TWriter>(TWriter writer, DateTime value)
    {
        return writer.DateTime(value);
    }
    
    public override bool Deserialize<TReader>(TReader reader, out DateTime value)
    {
        return reader.DateTime(out value);
    }
}