using Poly.Serialization;

namespace Poly.Reflection;

public class SpanableReferenceTypeAdapter<T> : GenericReferenceTypeAdapterBase<T> where T : class, ISpanParsable<T>, ISpanFormattable, new()
{
    public override Delegate<T>.TryCreateInstance TryInstantiate { get; } =
        static ([NotNullWhen(returnValue: true)] out T? instance) =>
        {
            instance = new();
            return true;
        };

    public override Delegate<T>.TryDeserialize TryDeserialize { get; } =
        static (IDataReader reader, [NotNullWhen(returnValue: true)] out T? value) =>
        {
            return reader.Read(out value);
        };

    public override Delegate<T>.TrySerialize TrySerialize { get; } =
        static (IDataWriter writer, T? value) => value switch
        {
            { } => writer.Write(value),
            null => writer.Null()
        };

    public override bool TryCreateInstance([NotNullWhen(true)] out T? instance)
    {
        instance = new T();
        return true;
    }

    public override bool Serialize<TWriter>(TWriter writer, T? value) => value switch
    {
        null => writer.Null(),
        _ => writer.Write(value)
    };

    public override bool Deserialize<TReader>(TReader reader, [NotNullWhen(returnValue: true)] out T? value)
    {
        return reader.Read(out value);
    }
}