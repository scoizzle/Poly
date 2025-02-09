using Poly.Serialization;

namespace Poly.Reflection;

internal sealed class TimeSpanAdapter : GenericValueTypeAdapterBase<TimeSpan>
{
    public override Delegate<TimeSpan>.TryCreateInstance TryInstantiate { get; } =
        static (out TimeSpan instance) =>
        {
            instance = default;
            return true;
        };

    public override Delegate<TimeSpan>.TrySerialize TrySerialize { get; } =
        static (IDataWriter writer, TimeSpan value) => writer.TimeSpan(value);

    public override Delegate<TimeSpan>.TryDeserialize TryDeserialize { get; } =
        static (IDataReader reader, out TimeSpan value) => reader.TimeSpan(out value);

    public TimeSpanAdapter()
    {
    }

    public override bool Serialize<TWriter>(TWriter writer, TimeSpan value)
    {
        return writer.TimeSpan(value);
    }

    public override bool Deserialize<TReader>(TReader reader, out TimeSpan value)
    {
        return reader.TimeSpan(out value);
    }
}