using Poly.Serialization;

namespace Poly.Reflection;

public class SpanableValueTypeAdapter<T> : GenericValueTypeAdapterBase<T> where T : struct, ISpanParsable<T>, ISpanFormattable
{
    public override Delegate<T>.TryCreateInstance TryInstantiate { get; } =
        static ([NotNullWhen(returnValue: true)] out T instance) =>
        {
            instance = default;
            return true;
        };

    public override Delegate<T>.TryDeserialize TryDeserialize { get; } =
        static (IDataReader reader, [NotNullWhen(returnValue: true)] out T value) =>
        {
            return reader.Read(out value);
        };

    public override Delegate<T>.TrySerialize TrySerialize { get; } =
        static (IDataWriter writer, T value) =>
        {
            return writer.Write(value);
        };

    public override bool Serialize<TWriter>(TWriter writer, T value)
    {
        return writer.Write(value);
    }

    public override bool Deserialize<TReader>(TReader reader, [NotNullWhen(returnValue: true)] out T value)
    {
        return reader.Read(out value);
    }
}