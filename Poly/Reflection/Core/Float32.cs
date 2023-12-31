using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class Float32 : ISystemTypeInterface<float>
{
    public Float32() {
        Type = typeof(float);
        SerializeObject = new SerializeDelegate<float>(Serialize).ToObjectDelegate();
        DeserializeObject = new DeserializeDelegate<float>(Deserialize).ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public bool Deserialize<TReader>(TReader reader, out float value) where TReader : class, IDataReader
	{
		using var _ = Instrumentation.AddEvent();

		return reader.Float32(out value);
	}
        
    public bool Serialize<TWriter>(TWriter writer, float value) where TWriter : class, IDataWriter
	{
		using var _ = Instrumentation.AddEvent();

		return writer.Number(value);
	}
}