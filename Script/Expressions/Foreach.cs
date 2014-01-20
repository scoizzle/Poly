using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Poly.Data;

namespace Poly.Script.Node {
    public class Foreach : Expression {
        public string VarName = "";
        public object List = null;

        public override object Evaluate(Data.jsObject Context) {
            var Values = GetValue(this.List, Context);

            if (Values == null)
                return null;

            var List = this.ToList();
            var Len = 0;

            if (Values is string)
                Len = (Values as string).Length;
            else if (Values is Data.jsObject)
                Len = (Values as Data.jsObject).Count;
            else if (Values is Node) {
                Len = (Values as Node).Count;
            }
            else return null;

            for (int i = 0; i < Len; i++) {
                var Key = "";
                var Value = (object)null;

                if (Values is string) {
                    Key = i.ToString();
                    Value = (string)Values;
                }
                else if (Values is jsObject) {
                    var Element = (Values as jsObject).ElementAt(i);
                    Key = Element.Key;
                    Value = Element.Value;
                }
                else if (Values is Node) {
                    var Element = (Values as Node).ElementAt(i);
                    Key = Element.Key;
                    Value = Element.Value;
                }

                Context[VarName, "Key"] = Key;
                Context[VarName, "Value"] = Value;

                for (int x = 0; x < List.Count; x++) {
                    var Node = (List[x].Value as Node);
                    var Result = Node.Evaluate(Context);

                    if (Node is Return)
                        return Result;

                    if (Result == Break) {
                        Context[VarName] = null;
                        return NoOp;
                    }

                    if (Result == Continue)
                        break;
                }

                Context[VarName] = null;
            }

            return null;
        }

        public override string ToString() {
            return "foreach (" + VarName + " in " + List.ToString() + ")" +
                base.ToString();
        }

        public static new Foreach Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare("foreach", Index)) {
                var Delta = Index + 7;
                ConsumeWhitespace(Text, ref Delta);

                if (Text.Compare("(", Delta)) {
                    var For = new Foreach();
                    var Open = Delta + 1;
                    var Close = Delta;

                    ConsumeEval(Text, ref Close);

                    if (Delta == Close)
                        return null;

                    For.VarName = ExtractValidName(Text, ref Open);
                    ConsumeWhitespace(Text, ref Open);

                    if (Text.Compare("in", Open)) {
                        Open += 2;
                        ConsumeWhitespace(Text, ref Open);

                        For.List = Engine.Parse(Text, ref Open, Close);
                        Open = Close;
                        ConsumeWhitespace(Text, ref Open);

                        var Exp = Engine.Parse(Text, ref Open, LastIndex);

                        if (Exp is Node) {
                            (Exp as Node).CopyTo(For);
                        }
                        else {
                            For.Add(Exp);
                        }
                        ConsumeWhitespace(Text, ref Open);

                        Index = Open;

                        return For;
                    }
                }
            }

            return null;
        }
    }
}
