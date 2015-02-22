using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Poly.Script.Libraries {
    using Data;
    using Nodes;

    class String : Library {
        public String() {
            RegisterTypeLibrary(typeof(string), this);

            Add("Substring", Substring);
        }

        public static Function Substring = Function.Create("Substring", (string This, string Left, string Right) => {
            return This.Substring(Left, Right);
        });
    }
}
