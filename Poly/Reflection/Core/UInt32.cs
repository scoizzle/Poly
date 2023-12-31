using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class UInt32 : ISystemTypeInterface<uint>
{
    public UInt32() {
        Type = typeof(uint);
        SerializeObject = new SerializeDelegate<uint>(Serialize).ToObjectDelegate();
        DeserializeObject = new DeserializeDelegate<uint>(Deserialize).ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public bool Deserialize<TReader>(TReader reader, out uint value) where TReader : class, IDataReader
	{
		using var _ = Instrumentation.AddEvent();

		return reader.UInt32(out value);
	}
        
    public bool Serialize<TWriter>(TWriter writer, uint value) where TWriter : class, IDataWriter
	{
		using var _ = Instrumentation.AddEvent();

		return writer.Number(value);
	}
}