using System;

using Poly.Data;

namespace Poly.Script.Libraries {
    using Nodes;
    using Types;

    public class Html : Library {
           
        public Html() {
            RegisterStaticObject("Html", this);

            Add(Escape);
            Add(Descape);
        }

        public static Function Escape = Function.Create("Escape", (string Input) => {
            if (string.IsNullOrEmpty(Input))
                return string.Empty;

            return Input.HtmlEscape();
        });

        public static Function Descape = Function.Create("Descape", (string Input) => {
            if (string.IsNullOrEmpty(Input))
                return string.Empty;

            return Input.HtmlDescape();
        });
    }
}
