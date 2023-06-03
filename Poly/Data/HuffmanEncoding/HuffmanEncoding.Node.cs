using System;
using System.Collections.Generic;

namespace Poly.Data
{
    public partial class HuffmanEncoding<TPriority, TValue> {
        private class Node {
            public TPriority Priority { get; set; }
            public TValue Value { get; set; }

            public Node Parent { get; set; }
            public Node Left { get; set; } 
            public Node Right { get; set; }

            public bool[] Path { get; private set; }

            public IEnumerable<bool> PathEnum { get; private set; }

            public long EncodedPath { get; private set; }
            public byte EncodedPathSize { get; private set; }

            public bool IsRoot => Parent == null;
            public bool IsLeaf => Left == null && Right == null;

            public void UpdatePath() {
                if (!IsRoot) throw new InvalidOperationException("Path update must be initiated from root element.");

                EncodedPath = 0;
                EncodedPathSize = 0;

                Left?.UpdatePath(EncodedPath, EncodedPathSize, false);
                Right?.UpdatePath(EncodedPath, EncodedPathSize, true);

                Path = Array.Empty<bool>();

                Left?.UpdatePath(Path, false);
                Right?.UpdatePath(Path, true);
            }

            private void UpdatePath(bool[] parentPath, bool isRight)
            {
                Path = new bool[parentPath.Length + 1];

                Array.Copy(parentPath, Path, parentPath.Length);
                Path[parentPath.Length] = isRight;

                Left?.UpdatePath(Path, false);
                Right?.UpdatePath(Path, true);
            }

            private void UpdatePath(long parentPath, byte parentPathSize, bool isRight)
            {
                if (isRight) {
                    EncodedPath = parentPath | (0b1000_0000_0000_0000_0000_0000_0000_0000_0000_0000_0000_0000_0000_0000_0000 >> parentPathSize);
                }
                else {
                    EncodedPath = parentPath;
                }

                EncodedPathSize = (byte)(parentPathSize + 1);

                PathEnum = GetPathEnumerator(EncodedPath, EncodedPathSize);

                Left?.UpdatePath(EncodedPath, EncodedPathSize, false);
                Right?.UpdatePath(EncodedPath, EncodedPathSize, true);
            }

            private static IEnumerable<bool> GetPathEnumerator(long path, byte depth) {
                var mask = 0b1000_0000_0000_0000_0000_0000_0000_0000_0000_0000_0000_0000_0000_0000_0000;

                for (var i = 0; i < depth; i++) {
                    yield return (path & mask) != 0;

                    mask >>= 1;
                }
            }
        }
    }   
}