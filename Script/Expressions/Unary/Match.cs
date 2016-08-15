using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Poly.Data;

namespace Poly.Script.Expressions {
    using Nodes;
    public class Match : Operator {
        public Match(Node Left,  Node Right) {
            this.Left = Left;
            this.Right = Right;
        }

        public override object Execute(object Left, object Right) {
            if (Left == null || Right == null)
                return null;

            var L = Left.ToString();
            var R = Right.ToString();

            return L.Match(R);
        }

        public static Node Parse(Engine Engine, StringIterator It, Node Left) {
            return new Match(Left, Engine.ParseValue(It));
        }

        public override string ToString() {
            return Left.ToString() + " ~ " + Right.ToString();
        }
    }
}
