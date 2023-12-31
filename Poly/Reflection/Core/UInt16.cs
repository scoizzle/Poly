using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class UInt16 : ISystemTypeInterface<ushort>
{
    public UInt16() {
        Type = typeof(ushort);
        SerializeObject = new SerializeDelegate<ushort>(Serialize).ToObjectDelegate();
        DeserializeObject = new DeserializeDelegate<ushort>(Deserialize).ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }
    public bool Deserialize<TReader>(TReader reader, out ushort value) where TReader : class, IDataReader
	{
		using var _ = Instrumentation.AddEvent();

		return reader.UInt16(out value);
	}
        
    public bool Serialize<TWriter>(TWriter writer, ushort value) where TWriter : class, IDataWriter
	{
		using var _ = Instrumentation.AddEvent();

		return writer.Number(value);
	}
}