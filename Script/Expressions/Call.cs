using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Node {
    public class Call : Expression {
        public Variable Object = null;
        public string Name = string.Empty;

        public Engine Engine = null;
        public Function Function = null;
        public Helper.ArgumentList Arguments = new Helper.ArgumentList();

        public Call(Engine Engine, string Name) {
            if (Name.IndexOf('.') != -1) {
                if (Name[Name.LastIndexOf('.') + 1] != '[') {
                    this.Name = Name.Substring(Name.LastIndexOf('.') + 1);
                }
                this.Object = Variable.Parse(Engine, Name, 0, Name.Length - this.Name.Length - 1);
            }
            else {
                this.Name = Name;
            }
            this.Engine = Engine;
        }

        public override object Evaluate(Data.jsObject Context) {
            object This = null;

            if (Object != null) {
                This = GetValue(Object, Context);
            }
            
            if (Function == null) {
                if (string.IsNullOrEmpty(Name)) {
                    if (This is Function) {
                        Function = This as Function;
                    }
                }
                else {
                    Function = Function.Get(Engine, Name, This);
                }

                if (Function == null)
                    return null;
            }
            
            return Function.Call(Context, Arguments, This, Engine);
        }

        public override string ToString() {
            return Name + "(" + Arguments.ToString() + ")";
        }

        public static new Call Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            return Parse(Engine, Text, ref Index, LastIndex, false);
        }

        public static Call Parse(Engine Engine, string Text, ref int Index, int LastIndex, bool Constructor = false) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            var Delta = Index;
            ConsumeValidName(Text, ref Delta);

            if (Index != Delta) {
                var Name = Text.Substring(Index, Delta - Index);

                var Call = new Call(Engine, Name);
                ConsumeWhitespace(Text, ref Delta);

                if (Text.Compare("(", Delta)) {
                    Call.Function = Function.Get(Engine, Name);

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

