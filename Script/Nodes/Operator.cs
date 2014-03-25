using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Node {
    public class Operator : Node {
        public object Left = null, Right = null;

        public object GetLeft(Data.jsObject Context) {
            var N = Left as Node;

            if (N != null) {
                return N.Evaluate(Context);
            }

            return Left;
        }

        public object GetRight(Data.jsObject Context) {
            var N = Right as Node;

            if (N != null) {
                return N.Evaluate(Context);
            }

            return Right;
        }
    }
}
