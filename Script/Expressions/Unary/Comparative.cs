using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Script.Expressions {
    using Nodes;
    using Types;

    public class Comparative : Operator {
        public Comparative(Node Left, Node Right) {
            this.Left = Left;
            this.Right = Right;
        }

        public override object Evaluate(jsObject Context) {
            return Left.Evaluate(Context) ?? Right.Evaluate(Context);
        }

        public static Node Parse(Engine Engine, StringIterator It, Node Left) {
            return new Comparative(Left, Engine.ParseValue(It));
        }

        public override string ToString() {
            return string.Format("{0} ?? {1}", Left, Right);
        }
    }
}
