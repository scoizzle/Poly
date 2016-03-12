using System;
using System.Collections.Generic;
using System.Text;

namespace Poly.Script.Expressions.Html {
    using Nodes;
    using Data;

    public class Document : Element {
        Element[] Members;

        public override void Evaluate(StringBuilder Output, jsObject Context) {
            if (Members != null)
            for (int i = 0; i < Members.Length; i++) {
                Members[i].Evaluate(Output, Context);
            }
        }

        new public static Node Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            var Delta = Index;
            if (Text.Compare('{', Delta)) {
                if (Text.FindMatchingBrackets('{', '}', ref Delta, ref LastIndex)) {
                    List<Element> Elements = new List<Element>();

                    while (IsParseOk(Engine, Text, ref Delta, LastIndex)) {
                        var E = Html.Parse(Engine, Text, ref Delta, LastIndex) as Element;

                        if (E == null)
                            break;

                        Elements.Add(E);
                    }

                    Index = Delta + 1;
                    return new Document() {
                        Members = Elements.ToArray()
                    };
                }
            }
            return null;
        }
    }
}
