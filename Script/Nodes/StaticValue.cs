using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace Poly.Script.Nodes {
    public class StaticValue : Value {
        private object Value;

        public StaticValue(object Value) {
            this.Value = Value;
        }

        public override object Evaluate(Data.jsObject Context) {
            return Value;
        }

        public static StaticValue New(object Val) {
            return new StaticValue(Val);
        }
    }
}
