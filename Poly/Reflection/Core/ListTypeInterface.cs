using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class ListTypeInterface<TList, TElement, TElementInterface> : ISystemTypeInterface<TList> 
    where TList : IList<TElement> 
    where TElementInterface : ISystemTypeInterface<TElement>
{
    public ListTypeInterface()
    {
        Type = typeof(TList);
        Serialize = GetSerializationDelegate<IDataWriter>().ToGenericDelegate<IDataWriter, TList>();
        Deserialize = GetDeserializationDelegate<IDataReader>().ToGenericDelegate<IDataReader, TList>();
        SerializeObject = Serialize.ToObjectDelegate();
        DeserializeObject = Deserialize.ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public SerializeDelegate<TList> Serialize { get; }

    public DeserializeDelegate<TList> Deserialize { get; }

    public static DeserializeDelegate<TReader, TList> GetDeserializationDelegate<TReader>() where TReader : class, IDataReader
    {
        var deserializeElement = TElementInterface.GetDeserializationDelegate<TReader>();

        return (TReader reader, [NotNullWhen(true)] out TList? list) =>
        {
            Guard.IsNotNull(reader);

            if (reader.BeginArray(out _))
            {
                var instance = Activator.CreateInstance(typeof(TList));
                Guard.IsNotNull(instance);

                list = (TList)instance;

                while (!reader.IsDone)
                {
                    if (!deserializeElement(reader, out var value))
                        return false;

                    list.Add(value);

                    if (!reader.EndValue())
                        break;
                }

                return reader.EndArray();
            }

            list = default;
            return reader.Null();
        };
    }

    public static SerializeDelegate<TWriter, TList> GetSerializationDelegate<TWriter>() where TWriter : class, IDataWriter
    { 
        var serializeElement = TElementInterface.GetSerializationDelegate<TWriter>();

        return (TWriter writer, TList list) =>
        {
            Guard.IsNotNull(writer);
            
            if (list is null) return writer.Null();

            if (!writer.BeginArray()) return false;

            foreach (var element in list)
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