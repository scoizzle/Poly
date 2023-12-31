using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class String : ISystemTypeInterface<string>
{
    public String() {
        Type = typeof(string);
        SerializeObject = new SerializeDelegate<string>(Serialize).ToObjectDelegate();
        DeserializeObject = new DeserializeDelegate<string>(Deserialize).ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }
    
    public bool Deserialize<TReader>(TReader reader, [NotNullWhen(true)] out string? value) where TReader : class, IDataReader
	{
		using var _ = Instrumentation.AddEvent();

		return reader.String(out value);
	}
        
    public bool Serialize<TWriter>(TWriter writer, string value) where TWriter : class, IDataWriter
	{
		using var _ = Instrumentation.AddEvent();

		return writer.String(value);
	}
}