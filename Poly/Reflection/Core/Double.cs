using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class Float64 : ISystemTypeInterface<double>
{
    public Float64() {
        Type = typeof(double);
        SerializeObject = new SerializeDelegate<double>(Serialize).ToObjectDelegate();
        DeserializeObject = new DeserializeDelegate<double>(Deserialize).ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public bool Deserialize<TReader>(TReader reader, out double value) where TReader : class, IDataReader
	{
		using var _ = Instrumentation.AddEvent();

		return reader.Float64(out value);
	}
        
    public bool Serialize<TWriter>(TWriter writer, double value) where TWriter : class, IDataWriter
	{
		using var _ = Instrumentation.AddEvent();

		return writer.Number(value);
	}
}