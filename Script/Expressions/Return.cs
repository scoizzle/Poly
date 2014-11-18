using System;

namespace Poly.Script.Expressions {
    using Nodes;

    class Return : Expression {
        public Node Value = null;

        public override object Evaluate(Data.jsObject Context) {
            if (Value != null)
                return Value.Evaluate(Context);

            return null;
        }

        public static new Node Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare("return", Index)) {
                var Delta = Index + 6;
                Text.ConsumeWhitespace(ref Delta);

                var Ret = new Return();

                if (!Text.Compare(";", Delta)) {
                    Ret.Value = Engine.Parse(Text, ref Delta, LastIndex);
                }

                Index = Delta;
                return Ret;
            }
            return null;
        }

        public override string ToString() {
            return string.Join(" ", "return", base.ToString());
        }
    }
}
