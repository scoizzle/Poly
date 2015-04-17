using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Poly.Data;

namespace Poly.Script.Expressions {
    using Nodes;

    public class Foreach : Expression {
        public Variable Variable = null;
        public Node List = null;

        private object LoopNodes<K, V>(jsObject Context, K Key, V Value) {
            var Var = new jsObject();
            Variable.Assign(Context, Var);

            Var.Set("Key", Key);
            Var.Set("Value", Value);

            foreach (var Node in Elements) {
                var Ret = Node as Return;

                if (Ret != null)
                    return Ret;

                object Val;
                if (Node != null)
                    Val = Node.Evaluate(Context);
                else 
                    Val = null;                

                if (Ret == Break || Ret == Continue)
                    return Ret;
            }

            Variable.Assign(Context, null);
            return null;
        }

        private object LoopString(jsObject Context, string String) {
            if (string.IsNullOrEmpty(String))
                return null;

            for (int i = 0 ; i < String.Length; i ++) {
                var Result = LoopNodes<int, char>(Context, i, String[i]);

                var Ret = Result as Return;

                if (Ret != null)
                    return Ret;

                if (Result == Break)
                    break;
            }
            return null;
        }

        private object LoopObject(jsObject Context, jsObject Object) {
            if (Object == null || Object.IsEmpty)
                return null;

            foreach (var Pair in Object) {
                var Result = LoopNodes<string, object>(Context, Pair.Key, Pair.Value);
                var Ret = Result as Return;

                if (Ret != null)
                    return Ret;
                
                if (Result == Break)
                    break;
            }

            return null;
        }

        private object LoopArray(jsObject Context, Array Array) {
            int Index = 0;
            foreach (object O in Array) {
                var Result = LoopNodes(Context, Index.ToString(), O);

                var Ret = Result as Return;

                if (Ret != null)
                    return Ret;

                if (Result == Break)
                    break;
            }
            return null;
        }

        public override object Evaluate(Data.jsObject Context) {
            if (Variable == null || List == null) {
                return null;
            }

            var Collection = List.Evaluate(Context);

            if (Collection == null)
                return null;

            object Value = null;

            var String = Collection as string;
            if (!string.IsNullOrEmpty(String)) {
                Value = LoopString(Context, String);
            }
            else if (Collection is Array) {
                Value = LoopArray(Context, Collection as Array);
            }
            else {
                Value = LoopObject(Context, Collection as jsObject);
            }

            if (!(Value is Return))
                Variable.Assign(Context, null);

            return Value;
        }

        public override string ToString() {
            return "foreach (" + Variable.ToString() + " in " + List.ToString() + ")" +
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

                    For.Variable = Variable.Parse(Engine, Text, ref Open, Close);
                    ConsumeWhitespace(Text, ref Open);

                    if (Text.Compare("in", Open)) {
                        Open += 2;
                        ConsumeWhitespace(Text, ref Open);

                        For.List = Engine.Parse(Text, ref Open, Close);
                        Open = Close;
                        ConsumeWhitespace(Text, ref Open);

                        var Exp = Engine.Parse(Text, ref Open, LastIndex);
                        if (Exp != null) {
                            For.Elements = new Node[] { Exp };
                            ConsumeWhitespace(Text, ref Open);

                            Index = Open;

                            return For;
                        }
                    }
                }
            }

            return null;
        }
    }
}
