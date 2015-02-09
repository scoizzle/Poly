using System;
using System.Collections.Generic;
using System.Text;

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
                else if ((This as Helpers.SystemTypeGetter) == null) {
                    var Class = Engine.Types[Name];

                    if (Class != null) {
                        Function = Class.Instaciator;
                    }
                    else if ((Class = Object as Class) != null) {
                        Function = Class.Instaciator;
                    }
                    else if ((Class = This as Class) != null) {
                        this.Function = Function = Class.StaticFunctions[Name];
                    }
                    else if (Object is string && Engine.Types.ContainsKey(Object as string)) {
                        Function = Engine.Types[Object as string].GetFunction(Name);
                    }
                    else if ((Function = Engine.GetFunction(Name)) != null) {
                        if (Function.Arguments != null && Function.Arguments.Length < Arguments.Length) {
                            Function = null;
                        }
                        else {
                            this.Function = Function;
                        }
                    }
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

                    if (F != null) {
                        if (F.Arguments != null && string.IsNullOrEmpty(F.Name)) {
                            ArgList[Index] = F.GetFunctionHandler(Context);
                        }
                        else {
                            ArgList[Index] = new Event.Handler(F.Evaluate);
                        }
                    }
                    else {
                        ArgList[Index] = Arguments[Index].Evaluate(Context);
                    }
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
                jsObject Args = new jsObject("this", Object);

                for (Index = 0; Index < ArgList.Length; Index++) {
                    string Key;

                    if (Function.Arguments != null && Index < Function.Arguments.Length)
                        Key = Function.Arguments[Index];
                    else
                        Key = Index.ToString();

                    Args[Key] = ArgList[Index];
                }     

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

        public static new Node Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            return Parse(Engine, Text, ref Index, LastIndex, false);
        }

        public static Node Parse(Engine Engine, string Text, ref int Index, int LastIndex, bool Constructor = false) {
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
                        else if (!Name.StartsWith("_") && (Type = Helpers.SystemTypeGetter.GetType(Name)) != null) {
                            This = new Helpers.SystemTypeGetter(Name) { Cache = Type };
                        }
                        else if (Name.Contains('[', LastPeriod + 1)) {
                            This = new Variable(Engine, Name);
                            LastPeriod = Name.Length - 1;
                        }
                        else if (Engine.Types.ContainsKey(FirstName)) {
                            This = Engine.Types[FirstName];
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

                    if (This == null && Engine.HtmlTemplates.ContainsKey(Name)) {
                        var Args = Text.Substring(Delta, Close - Delta).ParseCParams();
                        var Arguments = new Html.Element[Args.Length];

                        for (int i = 0; i < Args.Length; i++) {
                            int Ignore = 0;
                            Arguments[i] = Html.Html.Parse(Engine, Args[i], ref Ignore, Args[i].Length);
                        }

                        This = new Html.Generator(new Html.Templater(Engine.HtmlTemplates[Name], Arguments));

                        if (!Text.Compare('.', Close)) {
                            return This;
                        }
                    }
                    else {
                        var List = new List<Node>();
                        for (int i = 0; i < RawArgs.Length; i++) {
                            var Arg = Engine.Parse(RawArgs[i], 0);

                            if (Arg == null)
                                return null;

                            List.Add(Arg);
                        }

                        Call.Arguments = List.ToArray();
                    }

                    ConsumeWhitespace(Text, ref Close);


                    if (Text.Compare('.', Close)) {
                        Close++;
                        ConsumeWhitespace(Text, ref Close);

                        var Child = Parse(Engine, Text, ref Close, LastIndex) as Call;

                        if (Child != null) {
                            Child.This = Call;

                            Index = Close;
                            return Child;
                        }
                    }

                    Index = Close;
                    return Call;
                }
            }

            return null;
        }

        public override string ToString() {
            StringBuilder Output = new StringBuilder();

            Output.Append(This);

            if (This != null && !string.IsNullOrEmpty(Name))
                Output.Append('.');

            Output.Append(Name);

            if (Arguments == default(Node[])) {
                Output.Append("()");
            }
            else {
                Output.AppendFormat("({0})", string.Join<Node>(", ", Arguments));
            }

            return Output.ToString();
        }
    }
}
