using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Node {
    public class Expression : Node {
        public static readonly Expression NoOp = new Expression();
        public static readonly Expression Break = new Expression();
        public static readonly Expression Continue = new Expression();
        new public static readonly Value Null = StaticValue.New(null);

        public override string ToString() {
            return "{" + string.Join("; ", Values) + "}";
        }

        public static bool Parse(Engine Engine, string Text, ref int Index, int LastIndex, Node Storage) {
            if (Text.Compare("{", Index)) {
                var Open = Index + 1;
                var Close = Index;

                ConsumeExpression(Text, ref Close);

                if (Engine.Parse(Text, ref Open, Close - 1, Storage) != null) {
                    Index = Close;
                    return true;
                }
            }
            return false;
        }

        public static new Node Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare("{", Index)) {
                var Exp = new Expression();
                var Open = Index + 1;
                var Close = Index;

                ConsumeExpression(Text, ref Close);

                if (Engine.Parse(Text, ref Open, Close - 1, Exp) == null)
                    return null;

                Index = Close;
                return Exp;
            }
            else if (Text.Compare("break", Index)) {
                Index += 5;
                return Break;
            }
            else if (Text.Compare("continue", Index)) {
                Index += 8;
                return Continue;
            }
            else if (Text.Compare("null", Index)) {
                Index += 4;
                return Null;
            }

            return null;
        }
    }
}
