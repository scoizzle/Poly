using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Expressions {
    using Nodes;
    using Types;

    public class NotNull : Operator {
        public NotNull(Node Right) {
            this.Right = Right;
        }

        public override object Evaluate(Data.jsObject Context) {
            return !Bool.EvaluateNode(Right, Context);
        }

        public static Node Parse(Engine Engine, StringIterator It, Node Left) {
            return new NotNull(Engine.ParseValue(It));
        }

        public override string ToString() {
            return "!" + Right.ToString();
        }
    }
}
