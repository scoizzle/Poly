using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Nodes {
    public class Expression : Node {
        public static readonly Node NoOperation,
                                    Break,
                                    Continue;

        public static readonly StaticValue Null,
                                           True,
                                           False;

        static Expression() {
            NoOperation = new Node();
            Break = new Node();
            Continue = new Node();
            Null = new StaticValue(null);
            True = new StaticValue(true);
            False = new StaticValue(false);
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

        public static Node Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
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
