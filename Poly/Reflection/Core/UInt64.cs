using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class UInt64 : ISystemTypeInterface<ulong>
{
    public UInt64() {
        Type = typeof(ulong);
        SerializeObject = new SerializeDelegate<ulong>(Serialize).ToObjectDelegate();
        DeserializeObject = new DeserializeDelegate<ulong>(Deserialize).ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public bool Deserialize<TReader>(TReader reader, out ulong value) where TReader : class, IDataReader
    {
        using var _ = Instrumentation.AddEvent();

        return reader.UInt64(out value);
    }
        
    public bool Serialize<TWriter>(TWriter writer, ulong value) where TWriter : class, IDataWriter 
    {
        using var _ = Instrumentation.AddEvent();
        
        return writer.Number(value);
    }
}