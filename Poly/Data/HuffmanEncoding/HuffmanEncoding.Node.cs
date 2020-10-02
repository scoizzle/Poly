using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Poly.Data {
    public partial class HuffmanEncoding<TPriority, TValue> {
        private class Node {
            public TPriority Priority { get; set; }
            public TValue Value { get; set; }

            public Node Parent { get; set; }
            public Node Left { get; set; } 
            public Node Right { get; set; }

            public bool[] Path { get; private set; }

            public bool IsRoot => Parent == null;
            public bool IsLeaf => Left == null && Right == null;

            public void UpdatePath() {
                if (!IsRoot) throw new InvalidOperationException("Path update must be initiated from root element.");

                Path = Array.Empty<bool>();

                if (Left != null)
                    Left.UpdatePath(Path, false);

                if (Right != null)
                    Right.UpdatePath(Path, true);
            }

            private void UpdatePath(bool[] parentPath, bool isRight)
            {
                Path = new bool[parentPath.Length + 1];

                Array.Copy(parentPath, Path, parentPath.Length);
                Path[parentPath.Length] = isRight;

                if (Left != null)
                    Left.UpdatePath(Path, false);

                if (Right != null)
                    Right.UpdatePath(Path, true);
            }
        }
    }   
}