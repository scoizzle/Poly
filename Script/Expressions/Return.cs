using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Node {
    public class Return : Expression {
        public object Value = null;

        public override object Evaluate(Data.jsObject Context) {
            if (Value == null)
                return this;

            return GetValue(Value, Context);
        }

        public static new Expression Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare("return", Index)) {
                var Delta = Index + 6;
                ConsumeWhitespace(Text, ref Delta);

                var Ret = new Return();

                if (!Text.Compare(";", Delta)) {
                    Ret.Value = Engine.Parse(Text, ref Delta, LastIndex);
                }

                Index = Delta;
                return Ret;
            }
            return null;
        }
    }
}
