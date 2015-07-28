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

        public Call(Engine Engine, string Name, Node[] Arguments = default(Node[])) {
			this.Engine = Engine;
            this.Arguments = Arguments;

            Type T; Class C;

            this.Name = Name;

            if ((T = SystemTypeGetter.GetType(Name)) != null)
                This = new StaticValue(T);
            else if ((T = SystemTypeGetter.GetType(Name)) != null)
                This = new StaticValue(T);
            else if (Name.Contains('.')) {
                var Parent = Name.Substring(0, Name.LastIndexOf('.'));
                this.Name = Name.Substring(Parent.Length + 1);

                if ((C = Engine.Types[Name]) != null)
                    Function = C.Instaciator;
                else if (Engine.ReferencedTypes.ContainsKey(Parent))
                    This = new Nodes.StaticValue(Engine.ReferencedTypes[Parent]);
                else if (Name.Contains('[', Parent.Length + 1)) 
                {   This = new Variable(Engine, Name); this.Name = string.Empty; }
                else if (Engine.Types.ContainsKey(Parent))
                    This = Engine.Types[Parent];
                else if (!Engine.GetFunction(Parent, this.Name, out Function)) 
                    This = Engine.Parse(Parent, 0);
                else return;
            }
        }

        public override object Evaluate(jsObject Context) {
            int Index;
            object Object = null;
            object[] ArgList = null;
            var Function = this.Function;
            
            if (this.This != null) {
                Object = This.Evaluate(Context);
            }

            if (Arguments != null) {
                ArgList = new object[Arguments.Length];

                for (Index = 0; Index < Arguments.Length; Index++) {
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

            if (SystemFunction != null && Object != null) {
                return SystemFunction(Object, ArgList);
            }

            if (Function != null) {
                return Execute(Function, Object, ArgList);
            }

            Type Type = Object as Type;
            if (Type == null) {
                if (This == null) {
                    This = Node.ContextAccess;
                    Object = Context;

                    Type = Object.GetType();
                }
                else if (Object != null) {
                    Type = Object.GetType();
                }

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

                if (Function != null) {
                    return Execute(Function, Object, ArgList);
                }
            }
            else if (!(This is StaticValue)) {
                This = new StaticValue(Type);
            }

            if (Object == null)
                return null;
            
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

            if (Func == null) {
                if ((Function = Engine.GetFunction(Type, Name)) != null)
                    return Execute(Function, Object, ArgList);
                else 
                    return null;
            }
                        
            SystemFunction = Func;
            try {
                return Func(Object, ArgList);
            }
            catch { 
                SystemFunction = null;
                return null;
            }
        }

        private static object Execute(Function Func, object This, object[] ArgList) {
            jsObject Args = new jsObject("this", This);

            if (ArgList != null) {
                for (int Index = 0; Index < ArgList.Length; Index++) {
                    string Key;

                    if (Func.Arguments != null && Index < Func.Arguments.Length)
                        Key = Func.Arguments[Index];
                    else
                        Key = Index.ToString();

                    Args[Key] = ArgList[Index];
                }
            }

            return Func.Evaluate(Args);

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
                    var Call = new Call(Engine, Name);


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
