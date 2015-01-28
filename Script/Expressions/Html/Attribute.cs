using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Expressions.Html {
    using Nodes;
    public class Attribute : Element {
        public string Name;
        public Nodes.Node Value;

        public static string Format = " {0}=\"{1}\"";

        public Attribute(string Key, Nodes.Node Val) {
            this.Name = Key;
            this.Value = Val;
        }

        public override string Evaluate(Data.jsObject Context) {
            return string.Format(Format, Name, Value.Evaluate(Context));
        }

        public override void Evaluate(StringBuilder Output, Data.jsObject Context) {
            if (Value != null) {
                Output.AppendFormat(Format, Name, Value.Evaluate(Context));
            }
        }
    }
}
