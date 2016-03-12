using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Poly.Data;

namespace Poly.Script.Expressions {
    using Nodes;
    using Types;

    public class As : Operator {
        Type Type;
        public As(Node Left, Type T) {
            this.Left = Left;
            this.Type = T;
        }

        public override object Evaluate(jsObject Context) {
            try {
                return Convert.ChangeType(Left.Evaluate(Context), Type);
            }
            catch { }
            return null;
        }

        public static Operator Parse(Engine Engine, string Text, ref int Index, int LastIndex, string Left) {
            if (Text.Compare("as", Index)) {
                Index += 2;
                ConsumeWhitespace(Text, ref Index);

                var End = Index;
                ConsumeValidName(Text, ref End);
                
                var Name = Text.Substring(Index, End - Index);

                if (Engine.ReferencedTypes.ContainsKey(Name)) {
                    return new As(
                        Engine.Parse(Left, 0),
                        Engine.ReferencedTypes[Name]
                    );
                }
            }

            return null;
        }

        public override string ToString() {
            return Left.ToString() + " as " + Type.Name;
        }
    }
}
