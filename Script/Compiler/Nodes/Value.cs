using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Poly.Script.Compiler.Nodes {
    using Parser;

    public class Value : Node {
        public Context.TypeInformation Type;
    }
}
