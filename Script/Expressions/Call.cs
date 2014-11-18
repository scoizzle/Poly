using System;
using System.Collections.Generic;
using Poly.Data;

namespace Poly.Script.Expressions {
    using Nodes;
    public class Call : Expression {
        string Name;

        Node This;
        Function Function;
        Func<object, object[], object> SystemFunction;

        Node[] Arguments;
        Engine Engine;

        public Call(Engine Engine, Node This, string Name, Node[] Arguments = default(Node[])) {
			this.Engine = Engine;
            this.This = This;
            this.Name = Name;
            this.Arguments = Arguments;
        }

        public override object Evaluate(jsObject Context) {
            int Index = 0;
            object Object = null;
            object[] ArgList = null;
            var Function = this.Function;

            if (this.This != null) {
                Object = This.Evaluate(Context);
            }
            
            if (Function == null) {
                var Instance = Object as Types.ClassInstance;
                if (Instance != null) {
                    Function = Instance.Class.GetFunction(Name);
                }
                else {
                    var Class = Engine.Types[Name];
                    if (Class != null)
                        Function = Class.Instaciator;
                    else if ((Class = Object as Class) != null)
                        Function = Class.Instaciator;
                    else
                        this.Function = Function = Engine.GetFunction(Name);
                }

                if (string.IsNullOrEmpty(Name)) {
                    Function = Object as Function;

                    if (Function == null) {
                        var C = Object as Class;

                        if (C != null)
                            Function = C.Instaciator;
                    }
                }
            }

            if (Arguments != null) {
                ArgList = new object[Arguments.Length];

                for (; Index < Arguments.Length; Index++) {
                    var F = Arguments[Index] as Function;

                    if (F != null)
                        ArgList[Index] = new Event.Handler(F.Evaluate);
                    else
                        ArgList[Index] = Arguments[Index].Evaluate(Context);

                }
            }

            if (SystemFunction != null) {
                return SystemFunction(Object, ArgList);
            }

            if (Function == null && This != null) {
                if (Object == null)
                    this.Function = Function = Engine.GetFunction(This.ToString(), Name);
                else
                    this.Function = Function = Engine.GetFunction(Object.GetType(), Name);
            }

            if (Function != null) {
                jsObject Args = new jsObject();
                for (Index = 0; Index < ArgList.Length; Index++) {
                    string Key;

                    if (Function.Arguments != null && Index < Function.Arguments.Length)
                        Key = Function.Arguments[Index];
                    else
                        Key = Index.ToString();

                    Args[Key] = ArgList[Index];
                }     
                Args["this"] = Object;
                return Function.Evaluate(Args);
            }

            if (Object == null)
                return null;

            var Type = Object as Type;
            
            if (Type == null)
                Type = Object.GetType();

            Type[] ArgTypes = null;

            if (ArgList != null && ArgList.Length > 0) {
                ArgTypes = new Type[ArgList.Length];

                for (Index = 0; Index < ArgList.Length; Index++) {
                    if (ArgList[Index] == null) {
                        ArgTypes = null;
                        break;
                    }

                    ArgTypes[Index] = ArgList[Index].GetType();
                }
            }

            var Func = Function.GetFunction(Type, Name, ArgTypes);

            if (Func == null)
                return null;

            SystemFunction = Func;
            try {
                return Func(Object, ArgList);
            }
            catch { 
                SystemFunction = null;
                return null;
            };
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
                    Node This;
                    Type Type;

                    if (Name.Contains('.')) {
                        var LastPeriod = Name.LastIndexOf('.');
                        var FirstName = Name.Substring(0, LastPeriod);

                        if (Engine.Shorthands.ContainsKey(FirstName)){
                            This = new Helpers.SystemTypeGetter(Engine.Shorthands[FirstName]);
                        }
                        else if ((Type = Helpers.SystemTypeGetter.GetType(Name)) != null) {
                            This = new Helpers.SystemTypeGetter(Name) { Cache = Type };
                        }
                        else if (Name.Contains('[', LastPeriod + 1)) {
                            This = new Variable(Engine, Name);
                            LastPeriod = Name.Length - 1;
                        }
                        else {
                            This = new Variable(Engine, FirstName); 
                        }
                        Name = Name.Substring(LastPeriod + 1);
                    }
                    else This = null;

                    var Call = new Call(Engine, This, Name);

                    var Open = Delta + 1;
                    var Close = Delta;

                    ConsumeEval(Text, ref Close);

                    if (Delta == Close)
                        return null;

                    var RawArgs = Text.Substring(Open, Close - Open - 1).ParseCParams();
                    var List = new List<Node>();

                    for (int i = 0; i < RawArgs.Length; i++) {
                        var Arg = Engine.Parse(RawArgs[i], 0);

                        if (Arg == null)
                            return null;

                        List.Add(Arg);
                    }

                    Call.Arguments = List.ToArray();
                    Close++;
                    ConsumeWhitespace(Text, ref Close);

                    Index = Close;
                    return Call;
                }
            }

            return null;
        }

        public override string ToString() {
            return string.Join(".", This, string.Format("{0}({1})", Name, string.Join<Node>(",", Arguments)));
        }
    }
}
