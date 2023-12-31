using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class DateTime : ISystemTypeInterface<System.DateTime>
{
    public DateTime() {
        Type = typeof(System.DateTime);
        SerializeObject = new SerializeDelegate<System.DateTime>(Serialize).ToObjectDelegate();
        DeserializeObject = new DeserializeDelegate<System.DateTime>(Deserialize).ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public bool Deserialize<TReader>(TReader reader, out System.DateTime value) where TReader : class, IDataReader
	{
		using var _ = Instrumentation.AddEvent();

		return reader.DateTime(out value);
	}
        
    public bool Serialize<TWriter>(TWriter writer, System.DateTime value) where TWriter : class, IDataWriter
	{
		using var _ = Instrumentation.AddEvent();

		return writer.DateTime(value);
	}
}