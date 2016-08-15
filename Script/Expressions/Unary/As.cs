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

        public static Node Parse(Engine Engine, StringIterator It, Node Left) {
            var Start = It.Index;

            if (It.Consume(char.IsLetterOrDigit)) {
                var Name = It.Substring(Start, It.Index - Start);

                if (Engine.ReferencedTypes.ContainsKey(Name)) {
                    return new As(Left, Engine.ReferencedTypes[Name]);
                }
            }

            It.Index = Start;
            return null;
        }

        public override string ToString() {
            return Left.ToString() + " as " + Type.Name;
        }
    }
}
