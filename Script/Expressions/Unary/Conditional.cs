using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Script.Expressions {
    using Nodes;
    using Types;

    public class Conditional : Operator {
        Node Boolean;

        public Conditional(Node Bool, Node Left, Node Right) {
            Boolean = Bool;

            this.Left = Left == null ? Expression.Null : Left;
            this.Right = Right == null ? Expression.Null : Right;
        }

        public override object Evaluate(jsObject Context) {
            return Bool.EvaluateNode(Boolean, Context) ? 
                Left.Evaluate(Context) :
                Right.Evaluate(Context);
        }

        public static Node Parse(Engine Engine, StringIterator It, Node Left) {
            var L = Engine.ParseValue(It);

            It.ConsumeWhitespace();
            if (It.Consume(':')) {
                It.ConsumeWhitespace();

                var R = Engine.ParseValue(It);

                return new Conditional(Left, L, R);
            }

            return null;
        }

        public override string ToString() {
            return string.Format("{0} ? {1} : {2}", Boolean, Left, Right);
        }
    }
}
