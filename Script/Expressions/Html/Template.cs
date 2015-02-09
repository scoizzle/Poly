using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Expressions.Html {
    public class Template {
        public string[] Arguments;
        public Element Body;

        public Template(string[] Args, Element Body) {
            this.Arguments = Args;
            this.Body = Body;
        }

        public void Evaluate(StringBuilder Output, Data.jsObject Context) {
            if (Body == null)
                return;

            Body.Evaluate(Output, Context);
        }
    }

    public class Templater : Element {
        public Template Template;
        public Element[] Arguments;

        public Templater(Template Temp, params Element[] Args) {
            this.Template = Temp;
            this.Arguments = Args;
        }

        public override string Evaluate(Data.jsObject Context) {
            StringBuilder Out = new StringBuilder();
            Evaluate(Out, Context);
            return Out.ToString();
        }

        public override void Evaluate(StringBuilder Output, Data.jsObject Context) {
            var Args = new Data.jsObject();

            for (int i = 0; i < Arguments.Length && i < Template.Arguments.Length; i++) {
                if (Arguments[i] != null) {
                    Args[Template.Arguments[i]] = Arguments[i].Evaluate(Context);
                }
            }

            Template.Evaluate(Output, Args);
        }
    }
}
