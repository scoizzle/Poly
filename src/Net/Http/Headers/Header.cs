using System.Collections.Generic;
using System.Linq;

namespace Poly.Net.Http
{
    public class Header {
        public Header(string key) {
            Key = key;
        }

        public string Key { get; }
        
        public string Value { get; set; }

        public virtual IEnumerable<string> Serialize() =>
            Value == null ? 
                Enumerable.Empty<string>() :
                new [] { Value };
        
        public virtual void Deserialize(StringIterator value) =>
            Value = string.IsNullOrEmpty(Value) ? value.ToString() : string.Join(", ", Value, value);

        public virtual void Reset() =>
            Value = default;
    }
}