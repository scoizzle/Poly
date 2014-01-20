using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace Poly.Script.Node {
    public class Value : Node {
        public virtual object GetValue() {
            return null;
        }

        public override object Evaluate(Data.jsObject Context) {
            return this;
        }
    }
}
