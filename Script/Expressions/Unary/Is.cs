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

            return false;
        }

        public static Operator Parse(Engine Engine, string Text, ref int Index, int LastIndex, string Left) {
            if (Text.Compare("is", Index)) {
                Index += 2;
                ConsumeWhitespace(Text, ref Index);

                var End = Index;
                ConsumeValidName(Text, ref End);
                
                var Name = Text.Substring(Index, End - Index);

                if (Engine.Types.ContainsKey(Name)) {
                    return new Is(
                        Engine.Parse(Left, 0),
                        Engine.Types[Name]
                    );
                }

                return null;
            }

            return null;
        }

        public override string ToString() {
            return Left.ToString() + " || " + Right.ToString();
        }
    }
}
