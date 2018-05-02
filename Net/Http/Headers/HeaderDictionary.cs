using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace Poly.Net.Http {
    public class HeaderDictionary : Dictionary<string, Header> {
        public HeaderDictionary() : base(StringComparer.OrdinalIgnoreCase) { }

        public T Add<T>(T header) where T : Header {
            Add(header.Key, header);
            return header;
        }

        public Header Add(string key) {
            var header = new Header(key);
            Add(key, header);
            return header;
        }

        public Header GetOrAdd(string key) {
            if (!TryGetValue(key, out Header header)) {
                header = new Header(key);
                Add(key, header);
            }

            return header;
        }

        public IEnumerable<string> Serialize(string key) =>
            GetOrAdd(key).Serialize();

        public void Deserialize(string key, string value) =>
            GetOrAdd(key).Deserialize(value);

        public void Reset() {
            foreach (var header in this.Values)
                header.Reset();
        }
    }
}