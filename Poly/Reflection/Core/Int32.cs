using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class Int32 : ISystemTypeInterface<int>
{
    public Int32() {
        Type = typeof(int);
        SerializeObject = new SerializeDelegate<int>(Serialize).ToObjectDelegate();
        DeserializeObject = new DeserializeDelegate<int>(Deserialize).ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public bool Deserialize<TReader>(TReader reader, out int value) where TReader : class, IDataReader
	{
		using var _ = Instrumentation.AddEvent();

		return reader.Int32(out value);
	}
        
    public bool Serialize<TWriter>(TWriter writer, int value) where TWriter : class, IDataWriter
	{
		using var _ = Instrumentation.AddEvent();

		return writer.Number(value);
	}
}