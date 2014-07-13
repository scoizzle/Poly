using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Poly.Script.Node {
    public class Case : Expression {
        public object Object = null;
        public bool IsDefault = false;

        public override string ToString() {
            return "case " + Convert.ToString(Object) + ":" + base.ToString();
        }

        public static new Case Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare("case", Index)) {
                var Delta = Index + 4;
                ConsumeWhitespace(Text, ref Delta);

                var Case = new Case();

                Case.Object = Engine.Parse(Text, ref Delta, LastIndex);
                ConsumeWhitespace(Text, ref Delta);

                if (Text[Delta] == ':') {
                    Delta++;
                    ConsumeWhitespace(Text, ref Delta);

                    Engine.Parse(Text, ref Delta, LastIndex, Case);
                    ConsumeWhitespace(Text, ref Delta);

                    Index = Delta;
                    return Case;
                }
            }
            else if (Text.Compare("default", Index)) {
                var Delta = Index + 7;
                ConsumeWhitespace(Text, ref Delta);

                var Case = new Case();

                if (Text[Delta] == ':') {
                    Delta++;
                    ConsumeWhitespace(Text, ref Delta);

                    Engine.Parse(Text, ref Delta, LastIndex, Case);
                    ConsumeWhitespace(Text, ref Delta);

                    Index = Delta;

                    Case.IsDefault = true;
                    return Case;
                }
            }

            return null;
        }
    }
}
