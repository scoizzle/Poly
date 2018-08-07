using System;
using System.Collections.Generic;

namespace Poly.Data {
    public partial class HuffmanEncoding<TPriority, TValue> {
        public IEnumerable<TValue> Decode(IEnumerable<bool> encoded) {
            var current = Trunk.Root;

            foreach (var go_right in encoded) {
                current = go_right ? current.Right : current.Left;
                
                if (current == null)
                    break;

                if (current.IsLeaf) {
                    yield return current.Value;

                    current = Trunk.Root;
                }
            }
        }
    }
}

// 11110001 11100011 11000010 11100101 11110010 00111010 01101011 10100000 10101011 10010000 11110100 10000000