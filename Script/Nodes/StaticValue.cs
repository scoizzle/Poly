using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace Poly.Script.Nodes {
    public class StaticValue : Value {
        public object Value;

        public StaticValue(object Value) {
            this.Value = Value;
        }

        public override object Evaluate(Data.jsObject Context) {
            return Value;
        }

        public override string ToString() {
            if (Value != null)
                return Value.ToString();
            return string.Empty;
        }
    }
}
