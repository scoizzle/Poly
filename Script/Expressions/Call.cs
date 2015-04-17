using System;
using System.Collections.Generic;
using System.Text;

using Poly.Data;

namespace Poly.Script.Expressions {
    using Nodes;
    using Helpers;
    using Types;

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
            
            if (Function == null && !(Object is Type)) {
                if (Object is ClassInstance) {
                    Function = (Object as ClassInstance).Class.GetFunction(Name);
                }
                else if (Object is Class) {
                    Function = (Object as Class).Instaciator;
                }
                else if (Object is Function) {
                    Function = Object as Function;
                }
                else if (This is Class) {
                    this.Function = Function = (This as Class).StaticFunctions[Name];
                }
                else if (Object is string) {
                    var Str = Object as string;

                    if (Engine.Types.ContainsKey(Str)) {
                        Function = Engine.Types[Str].GetFunction(Name);
                    }
                    else if (!Engine.GetFunction(Str, Name, out Function)) {
                        Function = Engine.GetFunction(Name);
                    }
                }
                else {
                    Function = Engine.GetFunction(Name);
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

            if (Object == null) {
                if (This == null) {
                    This = new Variable(Engine, "_");
                    Object = Context;
                }
                else return null;
            }

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
                    var Call = new Call(Engine, null, Name);

                    Class Class;
                    Type Type;

                    if ((Type = SystemTypeGetter.GetType(Name)) != null) {
                        Call.This = new SystemTypeGetter(Name) { Cache = Type };
                    }
                    else if ((Class = Engine.Types[Name]) != null) {
                        Call.Function = Class.Instaciator;
                    }
                    else if (Name.Contains('.')) {
                        var LastPeriod = Name.LastIndexOf('.');
                        var FirstName = Name.Substring(0, LastPeriod);

                        Call.Name = Name.Substring(LastPeriod + 1);

                        if (Engine.Shorthands.ContainsKey(FirstName)) {
                            Call.This = new Helpers.SystemTypeGetter(Engine.Shorthands[FirstName]);
                        }
                        else if (Name.Contains('[', LastPeriod + 1)) {
                            Call.This = new Variable(Engine, Name);
                            LastPeriod = Name.Length - 1;
                        }
                        else if (Engine.Types.ContainsKey(FirstName)) {
                            Call.This = Engine.Types[FirstName];
                        }
                        else if (!Engine.GetFunction(FirstName, Call.Name, out Call.Function)) {
                            Call.This = Engine.Parse(FirstName, 0);
                        }
                    }

                    var Open = Delta + 1;
                    var Close = Delta;

                    ConsumeEval(Text, ref Close);

                    if (Delta == Close)
                        return null;

                    var RawArgs = Text.Substring(Open, Close - Open - 1).ParseCParams();

                    if (Call.This == null && Engine.HtmlTemplates.ContainsKey(Name)) {
                        var Arguments = new Html.Element[RawArgs.Length];

                        for (int i = 0; i < RawArgs.Length; i++) {
                            Arguments[i] = new Html.Variable(Engine.Parse(RawArgs[i], 0), null);
                        }

                        var This = new Html.Generator(new Html.Templater(Engine.HtmlTemplates[Name], Arguments));

                        if (!Text.Compare('.', Close)) {
                            Index = Close;
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
