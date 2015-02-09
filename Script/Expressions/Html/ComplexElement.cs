using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Expressions.Html {
    using Nodes;

    public class ComplexElement : Element {
        public static List<string> SingletonTags = new List<string>() {
            "area", "base", "br", "col", "command", "embed", "hr", "img", "input", "link", "meta", "param", "source", "!doctype"
        };

        public string Type;
        public List<Attribute> Attributes = new List<Attribute>();
        public List<Element> Elements = new List<Element>();

        public override string Evaluate(Data.jsObject Context) {
            StringBuilder Out = new StringBuilder();

            Evaluate(Out, Context);

            return Out.ToString();
        }

        public override void Evaluate(StringBuilder Output, Data.jsObject Context) {
            Output.AppendFormat("<{0}", Type);

            foreach (var Attr in Attributes) {
                Attr.Evaluate(Output, Context);
            }

            Output.Append(">");

            foreach (var Elem in Elements) {
                Elem.Evaluate(Output, Context);
            }

            if (!SingletonTags.Contains(Type)) {
                Output.AppendFormat("</{0}>", Type);
            }
        }

        public static ComplexElement Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            ComplexElement E = new ComplexElement();
            var Delta = Index;

            if (Text.FindMatchingBrackets("{", "}", ref Delta, ref LastIndex)) {
                while (Expression.IsParseOk(Engine, Text, ref Delta, LastIndex)) {
                    var Obj = Html.Parse(Engine, Text, ref Delta, LastIndex);

                    if (Obj == null)
                        break;

                    var Attr = Obj as Attribute;
                    if (Attr != null) {
                        E.Attributes.Add(Attr);
                        continue;
                    }

                    var Elem = Obj as Element;
                    if (Elem != null)
                        E.Elements.Add(Elem);
                }

                return E;
            }
            return null;
        }
    }
}
