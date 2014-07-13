using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Poly.Script.Node {
    public class Switch : Expression {
        public object Object = null;
        public Case Default = null;

        public override object Evaluate(Data.jsObject Context) {
            foreach (var Node in this.Values) {
                if (!(Node is Case))
                    continue;

                var Case = Node as Case;

                if (Bool.EvaluateNode(Case.Object, Context))
                    return GetValue(Case, Context);
            }

            return GetValue(Default, Context);
        }

        public override string ToString() {
            return "switch (" + Convert.ToString(Object) + ") " + base.ToString();
        }

        public static new Switch Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare("switch", Index)) {
                var Delta = Index + 6;
                ConsumeWhitespace(Text, ref Delta);

                var Open = Delta + 1;
                var Close = Delta;
                var Switch = new Switch();

                ConsumeEval(Text, ref Close);

                if (Delta == Close)
                    return null;

                Switch.Object = Engine.Parse(Text, ref Open, Close - 1);

                Delta = Close;
                ConsumeWhitespace(Text, ref Delta);

                if (Text[Delta] == '{') {
                    Open = Delta + 1;
                    Close = Delta;

                    ConsumeExpression(Text, ref Close);

                    while (true) {
                        var Option = Engine.Parse(Text, ref Open, Close) as Case;

                        if (Option != null) {
                            if (Option.IsDefault) {
                                Switch.Default = Option;
                            }
                            else if (Option.Object is Operator) {
                                (Option.Object as Operator).Left = Switch.Object;
                            }
                            else {
                                Option.Object = new Equal(Switch.Object, Option.Object);
                            }

                            Switch.Add(Option);
                        }
                        else break;
                    }

                    Index = Close;
                    return Switch;
                }
            }

            return null;
        }
    }
}
