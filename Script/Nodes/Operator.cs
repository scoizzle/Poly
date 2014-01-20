using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Node {
    public class Operator : Node {
        public object Left = null, Right = null;

        public object GetLeft(Data.jsObject Context) {
            if (Left is Node) {
                return (Left as Node).Evaluate(Context);
            }
            return Left;
        }

        public object GetRight(Data.jsObject Context) {
            if (Right is Node) {
                return (Right as Node).Evaluate(Context);
            }
            return Right;
        }
    }
}
