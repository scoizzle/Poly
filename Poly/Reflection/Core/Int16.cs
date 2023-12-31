using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class Int16 : ISystemTypeInterface<short>
{
    public Int16() {
        Type = typeof(short);
        SerializeObject = new SerializeDelegate<short>(Serialize).ToObjectDelegate();
        DeserializeObject = new DeserializeDelegate<short>(Deserialize).ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public bool Deserialize<TReader>(TReader reader, out short value) where TReader : class, IDataReader
	{
		using var _ = Instrumentation.AddEvent();

		return reader.Int16(out value);
	}
        
    public bool Serialize<TWriter>(TWriter writer, short value) where TWriter : class, IDataWriter
	{
		using var _ = Instrumentation.AddEvent();

		return writer.Number(value);
	}
}