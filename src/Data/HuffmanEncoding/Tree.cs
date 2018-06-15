using System;
using System.Collections.Generic;
using System.Linq;

namespace Poly.Data {
    using Collections;

    public partial class HuffmanEncoding<TPriority, TValue> {
        public class Tree {
            public Node Root;
            public Dictionary<TValue, Node> Dictionary;

            public Tree() {
                Dictionary = new Dictionary<TValue, Node>();
                Root = new Node();
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

            public Tree(IEnumerable<(IEnumerable<bool> Path, TValue Value)> leafs) : this() {
                foreach (var leaf in leafs)
                    AddLeaf(leaf.Path, leaf.Value);
            }
            
            public void AddLeaf(IEnumerable<bool> path, TValue value) {
                if (Dictionary.ContainsKey(value))
                    throw new ArgumentException("An element with the same key already exists in the Tree");
                    
                var current = Root;
                var route = path.GetEnumerator();

                while (route.MoveNext()) {
                    if (route.Current) {
                        if (current.Right == null) 
                            current.Right = new Node { Parent = current, IsRight = true };                        

                        current = current.Right;
                    }
                    else {
                        if (current.Left == null) 
                            current.Left = new Node { Parent = current };                        

                        current = current.Left;
                    }
                }

                current.Value = value;
                Dictionary[value] = current;
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