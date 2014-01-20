using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Helper {
    public class ArgumentList : Dictionary<string, object> {
        public override string ToString() {
            return string.Join(", ", Values);
        }
    }
}
