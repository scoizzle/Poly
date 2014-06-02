using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Script.Node {
    public class Call : Expression {
        private bool Cacheable = false;

        private Variable Object = null;
        private string Name = string.Empty;

        public Engine Engine = null;
        public Function Function = null;
        public Helper.ArgumentList Arguments = new Helper.ArgumentList();

        public Call(Engine Engine, string Name, Function Func = null) {
            this.Engine = Engine;
            this.Name = Name;
            this.Function = Func;

            if (Func == null) {
                if (Name.Contains('[')) {
                    this.Object = Variable.Parse(Engine, Name, 0);
                }
                else if (Name.Contains('.')) {
                    this.Name = Name.Substring("", ".", 0, false, true);
                    this.Object = Variable.Parse(Engine, this.Name, 0);
                    this.Name = Name.SubString(this.Name.Length + 1);
                }
                else {
                    this.Name = Name;
                }
            }
        }

        public override object Evaluate(Data.jsObject Context) {
            object This = GetValue(Object, Context);

            if (Function != null) {
                return Function.Call(Context, Arguments, This, Engine);
            }

            Function Func;

            if ((Func = Function.Get(Engine, Name, This, ref Cacheable)) == null) {
                if (Object == null)
                    return null;

                var ObjectName = Object.ToString();

                if (Engine.Shorthands.ContainsKey(ObjectName)) {
                    var T = Helper.SystemFunctions.SearchForType(Engine.Shorthands[ObjectName]);

                    if (T != null) {
                        var Args = Function.GetFunctionArguments(null, Context, Arguments);
                        var Types = Helper.MemberFunction.GetArgTypes(Args);

                        Func = Helper.SystemFunctions.Get(T, Name, Types);
                        Cacheable = true;
                    }
                }
                else {
                    return null;
                }
            }

            if (Cacheable)
                Function = Func;

            if (Func == null)
                return null;

            return Func.Call(Context, Arguments, This, Engine);
        }

        public bool ParseArguments(Engine Engine, string Text, int Open, int Close) {
            int i = 0;

            foreach (var Raw in Text.SubString(Open, Close - Open).ParseCParams()) {
                var Arg = Engine.Parse(Raw, 0);

                if (Arg == null)
                    return false;

                if (Function != null && i < Function.Arguments.Length) {
                    Arguments[Function.Arguments[i]] = Arg;
                }
                else {
                    Arguments.Add(Arguments.Count.ToString(), Arg);
                }
            }

            return true;
        }

        public override string ToString() {
            return (Object != null ? Object.ToString() : "" ) + Name + "(" + Arguments.ToString() + ")";
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
                ConsumeWhitespace(Text, ref Delta);

                if (Text.Compare("(", Delta)) {
                    var Call = new Call(Engine, Name, Function.GetStatic(Engine, Name));

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

