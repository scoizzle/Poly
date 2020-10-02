using System;
using System.Collections.Generic;

namespace Poly.Collections
{
    public class PriorityQueue<TPriority, TValue> where TPriority : IComparable<TPriority>
    {
        readonly SortedDictionary<TPriority, Queue<TValue>> dictionary;

        public PriorityQueue(IComparer<TPriority> comparer = default)
            => dictionary = new SortedDictionary<TPriority, Queue<TValue>>(comparer);

        public int Count { get; private set; }

        public void Enqueue(TPriority key, TValue value)
        {
            if (!dictionary.TryGetValue(key, out Queue<TValue> queue))
                dictionary[key] = queue = new Queue<TValue>();

            queue.Enqueue(value);
            Count++;
        }

        public bool TryDequeue(out TPriority priority, out TValue value)
        {
            if (Count == 0) goto failure;

            foreach (var pair in dictionary)
            {
                if (pair.Value.Count == 0)
                    continue;

                Count--;

                priority = pair.Key;
                value = pair.Value.Dequeue();
                return true;
            }

        failure:
            priority = default;
            value = default;
            return false;
        }
    }
}