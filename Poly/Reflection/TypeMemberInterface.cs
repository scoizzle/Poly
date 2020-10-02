using System;

namespace Poly.Reflection
{
    using Serialization;

    public interface TypeMemberInterface {
        string Name { get; }
        TypeInterface TypeInterface { get; }
        
        bool CanRead { get; }        
        Func<object, object> Get { get; }

        bool CanWrite { get; }
        Action<object, object> Set { get; }

        SerializeDelegate Serialize { get; }

        DeserializeDelegate Deserialize { get; }
    }
}