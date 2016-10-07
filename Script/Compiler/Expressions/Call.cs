using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Poly.Script.Compiler.Expressions {
    using Nodes;
    using Parser;

    public class Call : Value {
        public Call() {
            Type = Context.TypeInformation.GetInformation(typeof(void));
        }

        new public static Node Parse(Context Context) {

            return null;
        }
    }
}
