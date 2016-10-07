using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection.Emit;

namespace Poly.Script.Compiler.Nodes {
    using Parser;

    public class Node {
        public static Node NoOperation;

        static Node() {
            NoOperation = new Node();
        }

        public Node[] Elements;

        public virtual void Emit(ILGenerator IL) { }

        public static Node Parse(Context Context) { return null; }
    }
}
