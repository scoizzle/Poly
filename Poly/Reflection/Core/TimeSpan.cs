using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class TimeSpan : ISystemTypeInterface<System.TimeSpan>
{
    public TimeSpan() {
        Type = typeof(System.TimeSpan);
        SerializeObject = new SerializeDelegate<System.TimeSpan>(Serialize).ToObjectDelegate();
        DeserializeObject = new DeserializeDelegate<System.TimeSpan>(Deserialize).ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public bool Deserialize<TReader>(TReader reader, out System.TimeSpan value) where TReader : class, IDataReader
	{
		using var _ = Instrumentation.AddEvent();

		return reader.TimeSpan(out value);
	}
        
    public bool Serialize<TWriter>(TWriter writer, System.TimeSpan value) where TWriter : class, IDataWriter
	{
		using var _ = Instrumentation.AddEvent();

		return writer.TimeSpan(value);
	}
}