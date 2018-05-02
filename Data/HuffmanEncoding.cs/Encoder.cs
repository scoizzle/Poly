using System;
using System.Collections.Generic;

namespace Poly.Data {
    public partial class HuffmanEncoding<TPriority, TValue> {
        public IEnumerable<bool> Encode(TValue value) {
            var valid_value = Trunk.Dictionary.TryGetValue(value, out Node current);

            if (!valid_value)
                throw new ArgumentException("Invalid value to encode");

            var reverseEncoding = new List<bool>();
            while (!current.IsRoot) {
                reverseEncoding.Add(current.IsRight);
                current = current.Parent;
            }

            reverseEncoding.Reverse();
            return reverseEncoding;
        }

        public IEnumerable<IEnumerable<bool>> Encode(IEnumerable<TValue> values) {
            foreach (var val in values)
                yield return Encode(val);
        }
    }
}