using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class Int64 : ISystemTypeInterface<long>
{
    public Int64() {
        Type = typeof(long);
        SerializeObject = new SerializeDelegate<long>(Serialize).ToObjectDelegate();
        DeserializeObject = new DeserializeDelegate<long>(Deserialize).ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }
    public bool Deserialize<TReader>(TReader reader, out long value) where TReader : class, IDataReader
	{
		using var _ = Instrumentation.AddEvent();

		return reader.Int64(out value);
	}
        
    public bool Serialize<TWriter>(TWriter writer, long value) where TWriter : class, IDataWriter
	{
		using var _ = Instrumentation.AddEvent();

		return writer.Number(value);
	}
}