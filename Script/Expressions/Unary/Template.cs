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

        public static Operator Parse(Engine Engine, string Text, ref int Index, int LastIndex, string Left) {
            if (Text.Compare("|", Index)) {
                Index += 1;
                ConsumeWhitespace(Text, ref Index);

                var Var = string.IsNullOrEmpty(Left) ?
                    Node.ContextAccess :
                    Variable.Parse(Engine, Left, 0);

                return new Template(
                    Var,
                    Engine.Parse(Text, ref Index, LastIndex)   
                );
            }
            return null;
        }

        public override string ToString() {
            return Left.ToString() + " | " + Right.ToString();
        }
    }
}
