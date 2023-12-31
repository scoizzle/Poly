using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class Decimal : ISystemTypeInterface<decimal>
{
    public Decimal() {
        Type = typeof(decimal);
        SerializeObject = new SerializeDelegate<decimal>(Serialize).ToObjectDelegate();
        DeserializeObject = new DeserializeDelegate<decimal>(Deserialize).ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public bool Deserialize<TReader>(TReader reader, out decimal value) where TReader : class, IDataReader
	{
		using var _ = Instrumentation.AddEvent();

		return reader.Decimal(out value);
	}
        
    public bool Serialize<TWriter>(TWriter writer, decimal value) where TWriter : class, IDataWriter
	{
		using var _ = Instrumentation.AddEvent();

		return writer.Number(value);
	}
}