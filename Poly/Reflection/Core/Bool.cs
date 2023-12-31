using Poly.Serialization;

namespace Poly.Reflection.Core;    

internal class Boolean : ISystemTypeInterface<bool>
{
    public Boolean() {
        Type = typeof(bool);
        SerializeObject = new SerializeDelegate<bool>(Serialize).ToObjectDelegate();
        DeserializeObject = new DeserializeDelegate<bool>(Deserialize).ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public bool Deserialize<TReader>(TReader reader, out bool value) where TReader : class, IDataReader
	{
		using var _ = Instrumentation.AddEvent();

		return reader.Boolean(out value);
	}
        
    public bool Serialize<TWriter>(TWriter writer, bool value) where TWriter : class, IDataWriter
	{
		using var _ = Instrumentation.AddEvent();

		return writer.Boolean(value);
	}
}