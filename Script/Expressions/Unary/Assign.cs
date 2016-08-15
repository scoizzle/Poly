using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Expressions {
    using Nodes;

    public class Assign : Operator {
        public Assign(Variable Left,  Node Right) {
            this.Left = Left;
            this.Right = Right;
        }

        public override object Evaluate(Data.jsObject Context) {
			object Val = Right?.Evaluate (Context);

            (Left as Variable).Assign(Context, Val);
            return Val;
        }

        public override string ToString() {
            return string.Format("{0} = {1}", Left, Right);
        }

        public static Node Parse(Engine Engine, StringIterator It, Node Left) {
            if (Left is Variable) {
                return new Assign(Left as Variable, Engine.ParseOperation(It));
            }
            return null;
        }
    }
}
