using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Node {
    public class While : Expression {
        public object Boolean = null;

        public override object Evaluate(Data.jsObject Context) {
            var List = this.ToList();

            while (Bool.EvaluateNode(Boolean, Context)) {
                for (int i = 0; i < List.Count; i++) {
                    var Obj = List[i];
                    var Result = GetValue(Obj.Value, Context);

                    if (Obj.Value is Return)
                        return Result;

                    if (Result == Break)
                        return null;

                    if (Result == Continue)
                        break;
                }
            }            

            return null;
        }

        public override string ToString() {
            return "while (" + Boolean.ToString() + ") " + base.ToString();
        }

        public static new While Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare("while", Index)) {
                var Delta = Index + 5;
                ConsumeWhitespace(Text, ref Delta);

                if (Text.Compare("(", Delta)) {
                    var While = new While();
                    var Open = Delta + 1;
                    var Close = Delta;

                    ConsumeEval(Text, ref Close);

                    if (Delta == Close)
                        return null;

                    While.Boolean = Engine.Parse(Text, ref Open, Close);

                    Delta = Close + 1;
                    ConsumeWhitespace(Text, ref Delta);
                    var Exp = Engine.Parse(Text, ref Delta, LastIndex);

                    if (Exp != null) {
                        While.Add(Exp);
                        ConsumeWhitespace(Text, ref Delta);

                        Index = Delta;
                        return While;
                    }
                    
                }
            }

            return null;
        }
    }
}
