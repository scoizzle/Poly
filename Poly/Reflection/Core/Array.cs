using Poly.Serialization;

namespace Poly.Reflection.Core;
internal class Array<TElement, TElementInterface> : ISystemTypeInterface<TElement[]>
    where TElementInterface : ISystemTypeInterface<TElement>
{
    public Array()
    {
        Type = typeof(TElement[]);
        Serialize = GetSerializationDelegate<IDataWriter>().ToGenericDelegate<IDataWriter, TElement[]>();
        Deserialize = GetDeserializationDelegate<IDataReader>().ToGenericDelegate<IDataReader, TElement[]>();
        SerializeObject = Serialize.ToObjectDelegate();
        DeserializeObject = Deserialize.ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public SerializeDelegate<TElement[]> Serialize { get; }

    public DeserializeDelegate<TElement[]> Deserialize { get; }

    public static DeserializeDelegate<TReader, TElement[]> GetDeserializationDelegate<TReader>() where TReader : class, IDataReader
    {
        var deserializeElement = TElementInterface.GetDeserializationDelegate<TReader>();

        return (TReader reader, [NotNullWhen(true)] out TElement[]? value) =>
        {
            Guard.IsNotNull(reader);

            if (reader.BeginArray(out var numberOfMembers))
            {
                if (numberOfMembers.HasValue)
                {
                    value = new TElement[numberOfMembers.Value];

                    var memberNumber = 0;

                    while (!reader.IsDone && memberNumber < numberOfMembers)
                    {
                        ref var pos = ref value[memberNumber++];

                        if (!deserializeElement(reader, out pos))
                        {
                            value = default;
                            return false;
                        }

                        if (!reader.EndValue())
                            break;
                    }
                    
                    return true;
                }
                else {
                    var list = new List<TElement>();

                    while (!reader.IsDone)
                    {
                        if (!deserializeElement(reader, out var element))
                        {
                            value = default;
                            return false;
                        }

                        list.Add(element);

                        if (!reader.EndValue())
                            break;
                    }

                    if (reader.EndArray())
                    {
                        value = new TElement[list.Count];
                        list.CopyTo(0, value, 0, list.Count);
                        return true;
                    }
                }
            }

            value = default;
            return reader.Null();
        };
    }

    public static SerializeDelegate<TWriter, TElement[]> GetSerializationDelegate<TWriter>() where TWriter : class, IDataWriter
    { 
        var serializeElement = TElementInterface.GetSerializationDelegate<TWriter>();

        return (TWriter writer, TElement[] value) =>
        {
            Guard.IsNotNull(writer);
            
            if (value is null) return writer.Null();

            if (!writer.BeginArray()) return false;

            foreach (var element in value)
            {
                if (!serializeElement(writer, element))
                    return false;

                if (!writer.EndValue())
                    break;
            }

            return writer.EndArray();
        };
    }
}