using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Poly.Collections;

namespace Poly.Data {
    public partial class HuffmanEncoding<TPriority, TValue> {
        private class Tree {
            readonly Node root;
            readonly Dictionary<TValue, Node> nodes;

            public Tree(Node rootNode, Dictionary<TValue, Node> nodeDictionary) {
                root = rootNode;
                nodes = nodeDictionary;
            }

            public IEnumerable<bool> Encode(TValue value) {
                var valid_value = nodes.TryGetValue(value, out Node current);

                if (!valid_value)
                    throw new ArgumentException(nameof(value));

                return current.Path;
            }
            
            public IEnumerable<TValue> Decode(IEnumerable<bool> encoded) {
                var current = root;

                foreach (var go_right in encoded) {
                    current = go_right ? current.Right : current.Left;
                    
                    if (current == null)
                        yield break;

                    if (current.IsLeaf) {
                        yield return current.Value;

                        current = root;
                    }
                }
            }
        }

        Dictionary<TValue, TPriority> CountFrequencies(IEnumerable<TValue> dataset) {
            var frequencies = new Dictionary<TValue, TPriority>();

            foreach (var value in dataset) {
                if (!frequencies.TryGetValue(value, out var priority))
                    priority = default;

                frequencies[value] = Increment(priority);
            }

            return frequencies;
        }

        Tree BuildTree(Dictionary<TValue, TPriority> frequencies) {
            var parent = default(Node);
            var queue = new PriorityQueue<TPriority, Node>();
            var nodes = new Dictionary<TValue, Node>();

            foreach (var pair in frequencies) {
                var node = new Node { Priority = pair.Value, Value = pair.Key };

                queue.Enqueue(pair.Value, node);
                nodes.Add(pair.Key, node);
            }

            while (queue.Count > 1) {
                queue.TryDequeue(out var leftPriority, out var left);
                queue.TryDequeue(out var rightPriority, out var right);

                parent = new Node {
                    Priority = Add(leftPriority, rightPriority),
                    Left = left,
                    Right = right
                };

                left.Parent = parent;
                right.Parent = parent;

                queue.Enqueue(parent.Priority, parent);
            }

            parent.UpdatePath();
            return new Tree(parent, nodes);
        }
    }
}