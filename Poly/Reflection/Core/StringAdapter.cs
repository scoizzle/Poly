using Poly.Serialization;

namespace Poly.Reflection;

internal sealed class StringAdapter : GenericTypeAdapterBase<string>
{
    public override Delegate<string>.TryCreateInstance TryInstantiate { get; } =
        static ([NotNullWhen(returnValue: true)] out string? instance) =>
        {
            instance = string.Empty;
            return true;
        };

    public override Delegate<string>.TryDeserialize TryDeserialize { get; } =
        static (IDataReader reader, [NotNullWhen(returnValue: true)] out string? value) => reader.String(out value);

    public override Delegate<string>.TrySerialize TrySerialize { get; } =
        static (IDataWriter writer, string? value) => value switch
        {
            null => writer.Null(),
            { } => writer.String(value)
        };

    public override bool TryCreateInstance([NotNullWhen(true)] out string? instance)
    {
        instance = string.Empty;
        return true;
    }

    public override bool Deserialize(IDataReader reader, [NotNullWhen(true)] out object? value)
    {
        var result = Deserialize(reader, out string? typedValue);
        value = typedValue;
        return result;
    }

    public override bool Serialize<TWriter>(TWriter writer, string? value) => value switch
    {
        null => writer.Null(),
        _ => writer.String(value)
    };

    public override bool Serialize(IDataWriter writer, object? value) => value switch
    {
        null => writer.Null(),
        string typedValue => Serialize(writer, typedValue),
        _ => throw new NotSupportedException($"Serializing {value.GetType().Name} as {nameof(String)} is not supported.")
    };

    public override bool Deserialize<TReader>(TReader reader, [NotNullWhen(true)] out string? value)
    {
        return reader.String(out value);
    }
}