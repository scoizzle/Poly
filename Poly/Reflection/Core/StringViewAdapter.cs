using Poly.Serialization;

namespace Poly.Reflection;

internal sealed class StringViewAdapter : GenericValueTypeAdapterBase<StringView>
{
    public override Delegate<StringView>.TryCreateInstance TryInstantiate { get; } =
        static (out StringView instance) =>
        {
            instance = StringView.Empty;
            return true;
        };

    public override Delegate<StringView>.TrySerialize TrySerialize { get; } =
        static (IDataWriter writer, StringView value) => writer.StringView(value);
    public override Delegate<StringView>.TryDeserialize TryDeserialize { get; } =
        static (IDataReader reader, out StringView value) => reader.StringView(out value);

    public override bool Serialize<TWriter>(TWriter writer, StringView value)
    {
        return writer.StringView(value);
    }

    public override bool Deserialize<TReader>(TReader reader, [NotNullWhen(true)] out StringView value)
    {
        return reader.StringView(out value);
    }
}