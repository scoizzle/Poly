using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

using Poly.Data;

namespace Poly.Script.Expressions {
    using Nodes;
    using Helpers;
    using Types;

    public class Call : Html.Element {
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
			else if ((C = Engine.Types[Name]) != null)
				Function = C.Instaciator;
            else if (Name.Contains('.')) {
                var Parent = Name.Substring(0, Name.LastIndexOf('.'));
                this.Name = Name.Substring(Parent.Length + 1);

                if (Engine.ReferencedTypes.ContainsKey(Parent))
                    This = new StaticValue(Engine.ReferencedTypes[Parent]);
                else if (Name.Contains('[', Parent.Length + 1)) 
                    { This = Variable.Parse(Engine, new StringIterator(Name, Parent.Length + 1, Name.Length)); this.Name = string.Empty; }
                else if (Engine.Types.ContainsKey(Parent)) 
                    { This = Engine.Types[Parent]; }
                else if (!Engine.GetFunction(Parent, this.Name, out Function))
                    This = Engine.Parse(new StringIterator(Parent));
            }
        }

        public override object Evaluate(jsObject Context) {
            object Object = null;
            var Function = this.Function;
            
            if (this.This != null) {
                Object = This.Evaluate(Context);
            }

            if (Function != null) {
                return Execute(Function, Object, Context);
            }

            if (SystemFunction != null && (SystemFunction.Method.IsStatic == (Object == null))) {
                return Execute(SystemFunction, Object, Context);
            }

            Type Type = Object is Type ?
                Object as Type :
                Object != null ?
                    Object.GetType() :
                    null;

            Function = GetFunction(Object, Context);

            if (Function != null) {
                return Execute(Function, Object, Context);
            }
            else if (Type == null) {
                This = Expression.ContextAccess;
                Object = Context;
                Type = Object.GetType();
            }

            var ArgList = GetArguments(Context);            
            var ArgTypes = ArgList != null && ArgList.Length > 0 ?
                ArgList.Where(o => o != null)
                       .Select(o => o.GetType())
                       .ToArray() :
                null;
                                    
            SystemFunction = Helpers.RuntimeFunctionCache.GetFunction(Type, Name, ArgTypes);
            if (SystemFunction != null)
                return Execute(SystemFunction, Object, ArgList);

            Function = Engine.GetFunction(Type, Name);
            if (Function != null)
                return Execute(Function, Object, Context);

            return null;
        }

        public override void Evaluate(StringBuilder Output, jsObject Context) {
            object Object = null;
            var Function = this.Function;

            if (This != null) {
                Object = This.Evaluate(Context);
            }

            if (Function != null) {
                if (Function is Html.Function) {
                    Execute(Function as Html.Function, Output, Object, Context);
                    return;
                }
                else {
                    Output.Append(Execute(Function, Object, Context));
                    return;
                }
            }

            if (SystemFunction != null && (SystemFunction.Method.IsStatic == (Object == null))) {
                Output.Append(Execute(SystemFunction, Object, Context));
                return;
            }

            Type Type = Object is Type ?
                Object as Type :
                Object != null ?
                    Object.GetType() :
                    null;

            Function = GetFunction(Object, Context);

            if (Function != null) {
                if (Function is Html.Function) {
                    Execute(Function as Html.Function, Output, Object, Context);
                    return;
                }
                else {
                    Output.Append(Execute(Function, Object, Context));
                    return;
                }
            }
            else if (Type == null) {
                This = Expression.ContextAccess;
                Object = Context;
                Type = Object.GetType();
            }

            var ArgList = GetArguments(Context);
            var ArgTypes = ArgList != null && ArgList.Length > 0 ?
                ArgList.Select(o => o.GetType()).ToArray() :
                null;

            SystemFunction = Helpers.RuntimeFunctionCache.GetFunction(Type, Name, ArgTypes);

            if (SystemFunction != null)
                Output.Append(Execute(SystemFunction, Object, ArgList));

            Function = Engine.GetFunction(Type, Name);
            if (Function != null)
                Output.Append(Execute(Function, Object, Context));        
        }

        private Function GetFunction(object Object, jsObject Context) {
            if (This is Class) {
                return Function = (This as Class).GetStaticFunction(Name);
            }
            else if (Object != null) {
                if (Object is ClassInstance) {
                    return (Object as ClassInstance).Class.GetFunction(Name);
                }
                else if (Object is Class) {
					if ((Object as Class).LastName == Name || string.IsNullOrEmpty(Name))
                        return (Object as Class).Instaciator;
                    else
                        return (Object as Class).GetStaticFunction(Name);
                }
                else if (Object is Function) {
                    return Object as Function;
                }
                else if (Object is string) {
                    var Str = Object as string;

                    if (Engine.Types.ContainsKey(Str)) {
                        return Engine.Types[Str].GetFunction(Name);
                    }
                    else if (!Engine.GetFunction(Str, Name, out Function)) {
                        return Engine.GetFunction(Name);
                    }
                }
            }

            return Engine.GetFunction(Name);
        }

        private object[] GetArguments(jsObject Context) {
            if (Arguments == null)
                return null;

            object[] ArgList = new object[Arguments.Length];

            for (int Index = 0; Index < Arguments.Length; Index++) {
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

            return ArgList;
        }

        private jsObject GetFunctionArguments(Function Func, object This, jsObject Context) {
            jsObject Args = new jsObject();

            if (This != null)
                Args.Set("this", This);

            if (Arguments == null)
                return Args;

            for (int Index = 0; Index < Arguments.Length; Index++) {
                var F = Arguments[Index] as Function;
                var Key = Func.Arguments != null && Index < Func.Arguments.Length ?
                    Func.Arguments[Index] :
                    Index.ToString();

                if (F != null) {
                    if (F.Arguments != null && string.IsNullOrEmpty(F.Name)) {
                        Args[Key] = F.GetFunctionHandler(Context);
                    }
                    else {
                        Args[Key] = new Event.Handler(F.Evaluate);
                    }
                }
                else {
                    Args[Key] = Arguments[Index].Evaluate(Context);
                }
            }

            return Args;
        }

		private object Execute(Function Func, object This, jsObject Context) {
            return Func.Evaluate(GetFunctionArguments(Func, This, Context));
        }

		private void Execute(Html.Function Func, StringBuilder Output, object This, jsObject Context) {
            Func.Evaluate(Output, GetFunctionArguments(Func, This, Context));
        }

        private object Execute(Func<object, object[], object> Func, object This, object[] Args) {
            return Func(This, Args);
        }

        private object Execute(Func<object, object[], object> Func, object This, jsObject Context) {
            return Func(This, GetArguments(Context));
        }

        new public static Call Parse(Engine Engine, StringIterator It) {
            string Name;

            var Start = It.Index;

            if (It.Consume(CallNameFuncs)) {
                Name = It.Substring(Start, It.Index - Start);

                It.ConsumeWhitespace();

                if (It.Consume("(")) {
                    Start = It.Index;

                    if (It.Goto('(', ')')) {
                        var Sub = It.Clone(Start, It.Index);
                        var List = new List<Node>();

                        while (!Sub.IsDone()) {
                            var Node = Engine.ParseOperation(Sub);

                            if (Node == null)
                                return null;

                            List.Add(Node);
                            Sub.Consume(WhitespaceFuncs);
                        }

                        var Call = new Call(Engine, Name, List.ToArray());

                        It.Tick();
                        It.ConsumeWhitespace();

                        if (It.Consume('.')) {
                            var Child = Parse(Engine, It);

                            if (Child != null) {
                                var Current = Child;
                                while (Current.This != null) {
                                    if (Current.This is Call)
                                        Current = Current.This as Call;
                                    else return Child;
                                }

                                Current.This = Call;
                                return Child;
                            }
                        }

                        return Call;
                    }
                }

                It.Index = Start;
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
