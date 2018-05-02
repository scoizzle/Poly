using System;
using System.Collections.Generic;
using System.Linq;

namespace Poly.Data {
    using Collections;

    public partial class HuffmanEncoding<TPriority, TValue> {
        public class Tree {
            public Node Root;
            public Dictionary<TValue, Node> Dictionary;

            private Tree() {
                Dictionary = new Dictionary<TValue, Node>();
            }


            public Tree(Dictionary<TValue, TPriority> counts) : this() {
                var queue = new PriorityQueue<TPriority, Node>();

                foreach (var pair in counts) {
                    var node = new Node { Priority = pair.Value, Value = pair.Key };

                    queue.Enqueue(pair.Value, node);
                    Dictionary.Add(pair.Key, node);
                }

                Build(queue);
            }

            public Tree(Dictionary<TPriority, TValue> counts) : this() {
                var queue = new PriorityQueue<TPriority, Node>();

                foreach (var pair in counts) {
                    var node = new Node { Priority = pair.Key, Value = pair.Value };

                    queue.Enqueue(pair.Key, node);
                    Dictionary.Add(pair.Value, node);
                }

                Build(queue);
            }

            private void Build(PriorityQueue<TPriority, Node> queue) {
                while (queue.Count > 1) {
                    var left = queue.Dequeue();
                    var right = queue.Dequeue();

                    var priority = AddPriority(left.Priority, right.Priority);

                    var center = new Node { Priority = priority, Left = left, Right = right };
                    left.Parent = right.Parent = center;
                    
                    right.IsRight = true;

                    queue.Enqueue(priority, center);
                }

                Root = queue.Dequeue();
            }
        }
    }
}