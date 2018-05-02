using System;
using System.Collections.Generic;

namespace Poly.Data {
    public partial class HuffmanEncoding<TPriority, TValue> {
        public IEnumerable<TValue> Decode(IEnumerable<bool> encoded) {
            var current = Trunk.Root;

            foreach (var go_right in encoded) {
                current = (go_right ? current.Right : current.Left) ?? default;
                
                if (current.IsLeaf) {
                    yield return current.Value;

                    current = Trunk.Root;
                }
            }
        }
    }
}