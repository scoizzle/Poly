using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class Int8 : ISystemTypeInterface<sbyte>
{
    public Int8() {
        Type = typeof(sbyte);
        SerializeObject = new SerializeDelegate<sbyte>(Serialize).ToObjectDelegate();
        DeserializeObject = new DeserializeDelegate<sbyte>(Deserialize).ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public bool Deserialize<TReader>(TReader reader, out sbyte value) where TReader : class, IDataReader
	{
		using var _ = Instrumentation.AddEvent();

		return reader.Int8(out value);
	}
        
    public bool Serialize<TWriter>(TWriter writer, sbyte value) where TWriter : class, IDataWriter
	{
		using var _ = Instrumentation.AddEvent();

		return writer.Number(value);
	}
}