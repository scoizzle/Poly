using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security;

using Poly.Data;

namespace Poly.Script.Libraries {
    using Nodes;
    using Types;

    public class Html : Library {
           
        public Html() {
            RegisterStaticObject("Html", this);

            Add(Escape);
        }

        public static Function Escape = Function.Create("Escape", (string Input) => {
            if (string.IsNullOrEmpty(Input))
                return string.Empty;

            return SecurityElement.Escape(Input);
        }, "Input");
    }
}
