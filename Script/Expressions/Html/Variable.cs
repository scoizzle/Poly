using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Poly.Data;

namespace Poly.Script.Expressions.Html {
    using Nodes;
    public class Variable : Element {
        Script.Nodes.Node Var;
        Element Template;

        public Variable(Script.Nodes.Node V, Element Template) {
            this.Var = V;
            this.Template = Template;
        }

        public override string Evaluate(jsObject Context) {
            if (Var == null)
                return null;

            var Obj = Var.Evaluate(Context);

            if (Obj == null)
                return null;

            return Obj.ToString();
        }

        public override void Evaluate(StringBuilder Output, jsObject Context) {
            if (Var == null)
                return;

            var Obj = Var.Evaluate(Context);
            var js = Obj as jsObject;

            if (Template != null && js != null) {
                js.ForEach<jsObject>((K, V) => {
                    Template.Evaluate(Output, V);
                });
            }
            else {
                if (Obj != null)
                    Output.Append(Obj.ToString());
            }
        }

        public override string ToString() {
            if (Template != null) {
                return string.Format("{0}{{{1}}}", Var, Template);
            }
            else {
                return string.Format("{0}", Var);
            }
        }

        public static Element Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!Expression.IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare("@", Index)) {
                var Delta = Index + 1;
                var Var = Engine.Parse(Text, ref Delta, LastIndex);

                Element Template = null;

                if (Var != null && Text.Compare("{", Delta)) {
                    var Start = Delta;
                    var End = Delta;
                    if (Text.FindMatchingBrackets("{", "}", ref Start, ref End)) {
                        Template = Html.Parse(Engine, Text, ref Start, End);
                        Index = End + 1;
                    }
                }
                else {
                    Index = Delta;
                }

                return new Variable(Var, Template);
            }

            return null;
        }
    }
}
