using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Nodes {
    public class Value : Node {
        public override object Evaluate(Data.jsObject Context) {
            return this;
        }
    }
}
