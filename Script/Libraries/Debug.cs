using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;

namespace Poly.Script.Libraries {
    using Data;
    using Node;

    public class Debug : Library {
        public Debug() {
            RegisterStaticObject("Debug", this);

            Add(Break);
        }

        public static SystemFunction Break = new SystemFunction("Break", (Args) => {
            System.Diagnostics.Debugger.Break();
            return null;
        });
    }
}
