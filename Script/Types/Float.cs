using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Types {
    using Nodes;

    public class Float : Value {
        public double Value;

        public Float(double Val) {
            this.Value = Val;
        }

        public override object Evaluate(Data.jsObject Context) {
            return Value;
        }

        public override string ToString() {
            return Value.ToString();
        }

        public static Node Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            double Value = 0;

            if (Text.ToDouble(ref Index, LastIndex, ref Value))
                return new Float(Value);

            return null;
        }
    }
}
