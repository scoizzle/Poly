using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Expressions {
    using Nodes;
    using Types;

    public class And : Operator {
        public And(Node Left,  Node Right) {
            this.Left = Left;
            this.Right = Right;
        }

        public override object Evaluate(Data.jsObject Context) {
            return Bool.EvaluateNode(this.Left, Context) && Bool.EvaluateNode(this.Right, Context);
        }

        public static Node Parse(Engine Engine, StringIterator It, Node Left) {
            return new And(Left, Engine.ParseOperation(It));
        }

        public override string ToString() {
            return Left.ToString() + " && " + Right.ToString();
        }
    }
}
