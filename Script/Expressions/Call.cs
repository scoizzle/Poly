using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Node {
    public class Call : Expression {
        public string Name = string.Empty;

        public Engine Engine = null;
        public Function Function = null;
        public Helper.ArgumentList Arguments = new Helper.ArgumentList();

        public override object Evaluate(Data.jsObject Context) {
            if (Function == null) {
                Function = Function.Get(Engine, Context, Name);

                if (Function == null)
                    return null;
            }

            var Args = new Data.jsObject();
            var ArgList = Arguments.ToArray();

            for (int i = 0; i < ArgList.Length; i++) {
                var Item = ArgList[i];
                var Key = Function.Arguments.Length == 0 ?
                    Item.Key :
                    Function.Arguments.ElementAt(i);

                if (Item.Value is Function) {
                    Args[Key] = Item.Value;

                    Function.GetHandlerArguments(
                        Item.Value as Function,
                        Context,
                        Args
                    );
                }
                else {

                    var Value = Item.Value is Node ?
                        (Item.Value as Node).Evaluate(Context) :
                        Item.Value;

                    Variable.Set(
                        Key,
                        Args,
                        Value
                    );
                }
            }

            if (Name.Contains('.')) {
                var ObjName = Name.Substring("", ".", 0, false, true);
                var FunName = Name.Substring(ObjName.Length + 1);

                Args["this"] = Variable.Eval(Engine, ObjName, Context);
            }

            return Function.Evaluate(Args);
        }

        public override string ToString() {
            return Name + "(" + Arguments.ToString() + ")";
        }

        public static new Call Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            var Delta = Index;
            var Call = new Call() { Engine = Engine };
            ConsumeValidName(Text, ref Delta);

            if (Index != Delta) {
                Call.Name = Text.Substring(Index, Delta - Index);
                ConsumeWhitespace(Text, ref Delta);

                if (Text.Compare("(", Delta)) {
                    Call.Function = Function.Get(Engine, Call.Name);

                    var Open = Delta + 1;
                    var Close = Delta;

                    ConsumeEval(Text, ref Close);

                    if (Delta == Close)
                        return null;

                    var RawArgs = Text.Substring(Open, Close - Open - 1).ParseCParams();

                    for (int i = 0; i < RawArgs.Length; i++) {
                        var Arg = Engine.Parse(RawArgs[i], 0);

                        if (Arg == null)
                            return null;

                        if (Call.Function != null && i < Call.Function.Arguments.Length) {
                            Call.Arguments[Call.Function.Arguments[i]] = Arg;
                        }
                        else {
                            Call.Arguments.Add(Call.Arguments.Count.ToString(), Arg);
                        }
                    }

                    Close++;
                    ConsumeWhitespace(Text, ref Close);
                    
                    Index = Close;
                    return Call;
                }
            }

            return null;
        }
    }
}

