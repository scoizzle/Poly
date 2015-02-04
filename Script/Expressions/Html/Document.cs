using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Expressions.Html {
    public class Document : Element {
        public Element[] Elements;

        public Document(params Element[] Members) {
            this.Elements = Members;
        }

        public override string Evaluate(Data.jsObject Context) {
            StringBuilder Output = new StringBuilder();

            foreach (var E in Elements) {
                E.Evaluate(Output, Context);
            }

            return Output.ToString();
        }

        public override void Evaluate(StringBuilder Output, Data.jsObject Context) {
            foreach (var E in Elements) {
                E.Evaluate(Output, Context);
            }
        }
    }
}
