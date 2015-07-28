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
            this.Boolean = Bool;

            this.Left = Left == null ? Expression.Null : Left;
            this.Right = Right == null ? Expression.Null : Right;
        }

        public override object Evaluate(jsObject Context) {
            return Bool.EvaluateNode(this.Boolean, Context) ? 
                Left.Evaluate(Context) :
                Right.Evaluate(Context);
        }

        public static Operator Parse(Engine Engine, string Text, ref int Index, int LastIndex, string Left) {
            if (Text.Compare("?", Index)) {
                Index += 1;
                ConsumeWhitespace(Text, ref Index);

                var L = Engine.Parse(Text, ref Index, LastIndex);
                ConsumeWhitespace(Text, ref Index);

                if (Text.Compare(':', Index)) {
                    var R = Engine.Parse(Text, ref Index, LastIndex);

                    return new Conditional(Engine.Parse(Left, 0), L, R);
                }
            }

            return null;
        }

        public override string ToString() {
            return string.Format("{0} ? {1} : {2}", Boolean, Left, Right);
        }
    }
}
