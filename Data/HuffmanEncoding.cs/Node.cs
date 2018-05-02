namespace Poly.Data {
    public partial class HuffmanEncoding<TPriority, TValue> {
        public class Node {
            public TPriority Priority;
            public TValue Value;

            public Node Parent, Left, Right;

            public bool IsRight;
            public bool IsLeaf => Left == null && Right == null;
            public bool IsRoot => Parent == null;
        }   
    }
}