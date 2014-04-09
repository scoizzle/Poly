using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Node {
    public class Operator : Node {
        public object Left = null, Right = null;

        public object GetLeft(Data.jsObject Context) {
            return GetValue(Left, Context);
        }

        public object GetRight(Data.jsObject Context) {
            return GetValue(Right, Context);
        }
    }
}
