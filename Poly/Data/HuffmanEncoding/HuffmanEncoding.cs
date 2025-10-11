using System.Collections.Frozen;

namespace Poly.Data;

public static partial class HuffmanEncoding<TPriority, TValue>
    where TValue : IComparable<TValue>
    where TPriority : IBinaryInteger<TPriority>
{
    public sealed class Encoder(Tree huffmanEncodingTree)
    {
        private readonly FrozenDictionary<TreeNodeLookupKey, TValue> m_PathValueLookupTable = huffmanEncodingTree
            .Nodes
            .ToFrozenDictionary(
                keySelector: e => e.Value.EncodingPath,
                elementSelector: e => e.Value.Value!
            );

        public Encoder(IEnumerable<TValue> values) : this(new Tree(values))
        {

        }

        public Tree SourceTree => huffmanEncodingTree;

        public int MinKeyLength { get; } = huffmanEncodingTree.Nodes.Min(e => e.Value.Depth);
        public int MaxKeyLength { get; } = huffmanEncodingTree.Nodes.Max(e => e.Value.Depth);

        public IEnumerable<bool> Encode(TValue value) => huffmanEncodingTree.GetNodePath(value).Decode();

        public IEnumerable<bool> EncodeSet(IEnumerable<TValue> values) => values.SelectMany(Encode);

        public TValue Decode(IEnumerable<bool> path)
        {
            var lookupKey = TreeNodeLookupKey.From(path);

            if (!m_PathValueLookupTable.TryGetValue(lookupKey, out var value))
                throw new KeyNotFoundException(nameof(path));

            return value;
        }

        public IEnumerable<TValue> DecodeSet(IEnumerable<bool> paths)
        {
            var minLength = MinKeyLength;
            var keyBuilder = new TreeNodeLookupKeyBuilder();

            foreach (var step in paths)
            {
                keyBuilder.Add(step);

                if (keyBuilder.Length < minLength)
                    continue;

                TreeNodeLookupKey key = keyBuilder.Build();

                if (m_PathValueLookupTable.TryGetValue(key, out var value))
                {
                    yield return value;
                    keyBuilder.Reset();
                }
            }
        }
    }


    public sealed class Tree
    {
        public Tree(IEnumerable<TValue> values)
        {
            Dictionary<TValue, TreeNode> nodes = CalculateNodeFrequencies(this, values);
            PriorityQueue<TreeNode, TPriority> queue = EnqueueNodes(nodes);
            TreeNode rootNode = BuildHuffmanEncodingTree(queue);
            AssignNodePaths(rootNode);

            RootNode = rootNode;
            Nodes = nodes.ToFrozenDictionary();
        }

        public TreeNode RootNode { get; internal set; }
        public FrozenDictionary<TValue, TreeNode> Nodes { get; internal set; }

        public TreeNodeLookupKey GetNodePath(TValue value)
        {
            if (!Nodes.TryGetValue(value, out var node))
                throw new KeyNotFoundException(nameof(value));

            return node.EncodingPath;
        }

        public TValue? GetValueFromPath(IEnumerable<bool> path)
        {
            ArgumentNullException.ThrowIfNull(RootNode);
            TreeNode node = RootNode;

            foreach (var step in path)
            {
                var next = step
                    ? node.RightChild
                    : node.LeftChild;

                if (next is null)
                    throw new KeyNotFoundException(nameof(path));

                node = next;
            }

            return node.Value;
        }

        public IEnumerable<TValue> GetValuesFromPaths(IEnumerable<bool> paths)
        {
            ArgumentNullException.ThrowIfNull(RootNode);
            TreeNode node = RootNode;

            foreach (var step in paths)
            {
                var next = step
                    ? node.RightChild
                    : node.LeftChild;

                if (next is null)
                    throw new KeyNotFoundException(nameof(paths));

                if (next.IsLeafNode)
                {
                    yield return next.Value!;
                    node = RootNode;
                    continue;
                }

                node = next;
            }
        }

        public static TreeNodeLookupKey CalculateNodePath(TreeNode node)
        {
            int numberOfNavigations = node.Depth;
            TreeNodeLookupKeyBuilder builder = new();
            TreeNode next = node;

            while (--numberOfNavigations >= 0)
            {
                var parent = next.Parent;
                Guard.IsNotNull(parent);
                var isRightChild = parent.RightChild == next;
                builder.Add(value: isRightChild);
                next = parent;
            }

            return builder.Build();
        }

        private static void AssignNodePaths(TreeNode rootNode)
        {
            TreeNodeLookupKeyBuilder builder = new();
            BuildAndAssignPath(rootNode, builder);

            static void BuildAndAssignPath(TreeNode node, TreeNodeLookupKeyBuilder keyBuilder)
            {
                node.EncodingPath = keyBuilder.Build();

                if (node.LeftChild is TreeNode leftChild)
                {
                    keyBuilder.Add(false);
                    BuildAndAssignPath(leftChild, keyBuilder);
                    keyBuilder.Remove();
                }

                if (node.RightChild is TreeNode rightChild)
                {
                    keyBuilder.Add(true);
                    BuildAndAssignPath(rightChild, keyBuilder);
                    keyBuilder.Remove();
                }
            }
        }

        private static TreeNode BuildHuffmanEncodingTree(PriorityQueue<TreeNode, TPriority> queue)
        {
            TreeNode rootNode = default!;

            while (queue.Count > 1)
            {
                rootNode = queue.Dequeue() + queue.Dequeue();
                queue.Enqueue(rootNode, rootNode.Priority);
            }

            return rootNode;
        }

        private static PriorityQueue<TreeNode, TPriority> EnqueueNodes(Dictionary<TValue, TreeNode> nodes)
        {
            var queue = new PriorityQueue<TreeNode, TPriority>();

            foreach (var node in nodes.Values)
            {
                queue.Enqueue(node, node.Priority);
            }

            return queue;
        }

        private static Dictionary<TValue, TreeNode> CalculateNodeFrequencies(Tree tree, IEnumerable<TValue> items)
        {
            var nodes = new Dictionary<TValue, TreeNode>();

            foreach (var item in items)
            {
                if (!nodes.TryGetValue(item, out var node))
                {
                    nodes.Add(item, new()
                    {
                        Tree = tree,
                        Value = item,
                        Priority = TPriority.One
                    });
                    continue;
                }

                node.Priority++;
            }

            return nodes;
        }
    }


    [DebuggerDisplay("{Value}, {PathBinaryString}")]
    public sealed class TreeNode
    {
        public required Tree Tree { get; init; }
        public required TPriority Priority { get; set; }
        public TValue? Value { get; set; }
        public TreeNode? Parent { get; set; }
        public TreeNode? LeftChild { get; set; }
        public TreeNode? RightChild { get; set; }

        public TreeNodeLookupKey EncodingPath { get; internal set; }
        public int Depth => EncodingPath.BitLength;

        private string PathBinaryString => EncodingPath.DebuggerDisplay;

        public bool IsRootNode => Tree.RootNode == this;
        public bool IsLeafNode => LeftChild is null && RightChild is null;

        public static TreeNode operator +(TreeNode left, TreeNode right)
        {
            Guard.IsNotNull(left);
            Guard.IsNotNull(right);
            Guard.IsNull(left.Parent);
            Guard.IsNull(right.Parent);
            Guard.IsReferenceEqualTo(left.Tree, right.Tree);

            var node = new TreeNode()
            {
                Tree = left.Tree,
                Priority = left.Priority + right.Priority,
                LeftChild = left,
                RightChild = right
            };

            left.Parent = node;
            right.Parent = node;
            return node;
        }
    }

    [DebuggerDisplay("{DebuggerDisplay}")]
    public readonly record struct TreeNodeLookupKey(TPriority Value, int BitLength) : IEnumerable<bool>
    {
        internal string DebuggerDisplay => Value
            .ToString("B", null)
            .PadLeft(BitLength, '0');

        public static TreeNodeLookupKey From(IEnumerable<bool> path)
        {
            TreeNodeLookupKeyBuilder builder = new();
            foreach (var step in path)
                builder.Add(step);
            return builder.Build();
        }

        public IEnumerable<bool> Decode()
        {
            var index = 0;
            var length = BitLength;
            var value = Value;
            var mask = TPriority.One;

            for (; index < length; index++, mask <<= TPriority.One)
            {
                var theBitIsSet = (value & mask) == mask;
                yield return theBitIsSet;
            }
        }

        public IEnumerator<bool> GetEnumerator() => Decode().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => Decode().GetEnumerator();
    }

    public struct TreeNodeLookupKeyBuilder()
    {
        private TPriority m_EncodedKey = TPriority.Zero;
        private TPriority m_EncodingMask = TPriority.One;
        private int m_BitLength = 0;

        public int Length => m_BitLength;

        public TreeNodeLookupKey Build() => new TreeNodeLookupKey(m_EncodedKey, m_BitLength);

        public void Add(bool value)
        {
            if (value)
                m_EncodedKey |= m_EncodingMask;

            m_EncodingMask <<= TPriority.One;
            m_BitLength++;
        }

        public void Remove()
        {
            m_EncodingMask >>= TPriority.One;
            m_EncodedKey &= ~m_EncodingMask;
            m_BitLength--;
        }

        public void Reset()
        {
            m_EncodedKey = TPriority.Zero;
            m_EncodingMask = TPriority.One;
            m_BitLength = 0;
        }
    }
}