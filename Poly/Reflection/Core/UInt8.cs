using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class UInt8 : ISystemTypeInterface<byte>
{
    public UInt8() {
        Type = typeof(byte);
        SerializeObject = new SerializeDelegate<byte>(Serialize).ToObjectDelegate();
        DeserializeObject = new DeserializeDelegate<byte>(Deserialize).ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public bool Deserialize<TReader>(TReader reader, out byte value) where TReader : class, IDataReader
	{
		using var _ = Instrumentation.AddEvent();

		return reader.UInt8(out value);
	}
        
    public bool Serialize<TWriter>(TWriter writer, byte value) where TWriter : class, IDataWriter
	{
		using var _ = Instrumentation.AddEvent();

		return writer.Number(value);
	}
}