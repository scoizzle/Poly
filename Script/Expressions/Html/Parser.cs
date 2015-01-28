using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Expressions.Html {
    using Nodes;
    public class Parser {
        public static Nodes.Node Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!Expression.IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare('`', Index)) {
                var Delta = Index;
                var Close = Delta;

                if (Text.FindMatchingBrackets("`", "`", ref Delta, ref Close)) {
                    var Obj =  Html.Parse(Engine, Text, ref Delta, Close);

                    if (Obj != null) {
                        Index = Close + 1;
                        return new Generator(Obj);
                    }
                }                
            }
            else if (Text.Compare("html", Index)) {
                var Delta = Index;

                if (Html.FuncParser(Engine, Text, ref Delta, LastIndex) != null) {
                    Index = Delta;
                    return Expression.NoOperation;
                }
            }

            return null;
        }
    }
}
