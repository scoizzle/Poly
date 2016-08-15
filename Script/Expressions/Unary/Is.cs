using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Poly.Data;

namespace Poly.Script.Expressions {
    using Nodes;
    using Types;

    public class Is : Operator {
        public Is(Node Left,  Node Right) {
            this.Left = Left;
            this.Right = Right;
        }

        public override object Evaluate(jsObject Context) {
            var Left = this.Left.Evaluate(Context);

            if (Left is Class) {
                var Class = Left as Class;

                do {
                    if (Class == Right) {
                        return true;
                    }

                    Class = Class.Base;
                }
                while (Class != null);
            }
            else if (Left is Event.Handler) {
                var Func = Left as Event.Handler;

                if (Right is StaticValue && (Right as StaticValue).Value as string == "html") {
                    return Func.Method.DeclaringType == typeof(Html.Function);
                }
            }

            return false;
        }

        public static Node Parse(Engine Engine, StringIterator It, Node Left) {
            var Start = It.Index;

            if (It.Consume(char.IsLetterOrDigit)) {
                var Name = It.Substring(Start, It.Index - Start);

                if (Engine.Types.ContainsKey(Name)) {
                    return new Is(Left, Engine.Types[Name]);
                }
            }
            return null;
        }

        public override string ToString() {
            return Left.ToString() + " || " + Right.ToString();
        }
    }
}
