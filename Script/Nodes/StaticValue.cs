using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace Poly.Script.Node {
    public class StaticValue : Value {
        private object Value = null;

        public override object Evaluate(Data.jsObject Context) {
            return Value;
        }

        public static StaticValue New(object Val) {
            return new StaticValue() { Value = Val };
        }
    }
}
