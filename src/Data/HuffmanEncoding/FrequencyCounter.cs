using System;
using System.Collections.Generic;

namespace Poly.Data {
    public partial class HuffmanEncoding<TPriority, TValue> {
        public class FrequencyCounter {
            internal Dictionary<TValue, TPriority> Counts;

            public FrequencyCounter() {
                Counts = new Dictionary<TValue, TPriority>();
            }

            public FrequencyCounter(IEnumerable<TValue> values) : this() {
                AddRange(values);
            }

            public void Add(TValue value) {
                if (!Counts.TryGetValue(value, out TPriority priority))
                    priority = default;

                Counts[value] = IncPriority(priority);
            }

            public void AddRange(IEnumerable<TValue> values) {
                foreach (var val in values)
                    Add(val);
            }
        }
    }
}