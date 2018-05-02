using System;
using System.Collections.Generic;
using System.Linq;

namespace Poly.Collections {
    public class PriorityQueue<TKey, TValue> {
        SortedDictionary<TKey, Queue<TValue>> dictionary;

        public PriorityQueue() {
            dictionary = new SortedDictionary<TKey, Queue<TValue>>();
        }

        public int Count { get; private set; }

        public void Enqueue(TKey key, TValue value) {
            if (!dictionary.TryGetValue(key, out Queue<TValue> queue))
                dictionary[key] = queue = new Queue<TValue>();

            Count++;
            queue.Enqueue(value);
        }

        public TValue Dequeue() {
            var element = dictionary.First();
            var queue = element.Value;

            if (queue.Count == 1)
                dictionary.Remove(element.Key);

            Count--;
            return queue.Dequeue();
        }
    }
}