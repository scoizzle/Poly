using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class Char : ISystemTypeInterface<char>
{
    public Char() {
        Type = typeof(char);
        SerializeObject = new SerializeDelegate<char>(Serialize).ToObjectDelegate();
        DeserializeObject = new DeserializeDelegate<char>(Deserialize).ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public bool Deserialize<TReader>(TReader reader, out char value) where TReader : class, IDataReader
	{
		using var _ = Instrumentation.AddEvent();

		return reader.Char(out value);
	}
        
    public bool Serialize<TWriter>(TWriter writer, char value) where TWriter : class, IDataWriter
	{
		using var _ = Instrumentation.AddEvent();

		return writer.Char(value);
	}
}