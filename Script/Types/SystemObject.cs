using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Node {
    public class SystemObject : DataType<object> {
        public object Value = null;

        public SystemObject(object Obj) {
            this.Value = Obj;
        }

        public override string ToString() {
            if (Value != null)
                return Value.ToString();

            return "";
        }
    }
}
