using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Poly.Data;

namespace Poly.Script.Expressions {
    using Nodes;
    public class Template : Operator {
        public Template(Node Left,  Node Right) {
            this.Left = Left;
            this.Right = Right;
        }

        public override object Execute(object Left, object Right) {
            if (Left == null || Right == null)
                return null;

            var L = Left as jsObject;

            if (L == null)
                return null;
            
            var R = Right.ToString();

            return L.Template(R);
        }

        public static Node Parse(Engine Engine, StringIterator It, Node Left) {
            return new Template(
                Left == null ? ContextAccess : Left, 
                Engine.ParseValue(It)
            );
        }

        public override string ToString() {
            return Left.ToString() + " | " + Right.ToString();
        }
    }
}
