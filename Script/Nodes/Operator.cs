using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Nodes {
    public class Operator : Node {
        public Node Left = null, Right = null;

        public override object Evaluate(Data.jsObject Context) {
            object L, R;

            if (Left != null)
                L = Left.Evaluate(Context);
            else
                L = null;

            if (Right != null)
                R = Right.Evaluate(Context);
            else
                R = null;

            return Execute(L, R);
        }

        public virtual object Execute(object Left, object Right) {
            return null;
        }
    }
}
