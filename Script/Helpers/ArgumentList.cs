using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Helper {
    using Data;

    public class ArgumentList : jsObject {
        public override string ToString() {
            return string.Join(", ", Values);
        }
    }
}
