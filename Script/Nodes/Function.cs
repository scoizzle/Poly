using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Script {
    using Node;

    public class Function : Node.Expression {
        public string ObjectName, Name = "";
        public string[] Arguments = new string[0];

        public Function() { }
        public Function(string Name) {
            this.Name = Name;

            if (this.Name.Contains('.'))
                this.ObjectName = Name.Substring("", ".", 0, false, true);
        }

        public override object Evaluate(jsObject Context) {
            return base.Evaluate(Context);
        }

        public object Call(jsObject Context, jsObject ArgList, object This = null, Engine Engine = null) {
            var Args = new jsObject();
            var Index = 0;

            ArgList.ForEach((Key, Value) => {
                var Name = Arguments.Length == 0 || Index >= Arguments.Length ?
                    Key :
                    Arguments[Index];

                if (Value is Function) {
                    GetHandlerArguments(Value as Function, Context, Args);
                }
                else if (Value is Node.Node) {
                    Value = (Value as Node.Node).Evaluate(Context);
                }

                Variable.Set(Name, Args, Value);
                Index++;
            });

            if (This != null) {
                Variable.Set(
                    "this", 
                    Args, 
                    This
                );
            }
            else if (!string.IsNullOrEmpty(ObjectName)) {
                This = Engine == null ?
                    Variable.Get(ObjectName, Context) :
                    Variable.Eval(Engine, ObjectName, Context);

                Variable.Set(
                    "this", 
                    Args, 
                    This
                );
            }

            return this.Evaluate(Args);
        }

        public Poly.Event.Handler GetSystemHandler() {
            return Evaluate;
        }

        public override string ToString() {
            return "function " + Name + "(" + string.Join(", ", Arguments) + ")";
        }

        public static Function Get(Engine Engine, string Name) {
            if (Engine.Functions.ContainsKey(Name)) {
                return Engine.Functions[Name];
            } 

            if (Library.TypeLibsByName.ContainsKey(Name)) {
                return Library.TypeLibsByName[Name].Get<Function>(Name);
            }

            var ObjName = Name.Substring("", ".", 0, false, true);
            var FunName = Name.Substring(ObjName.Length + 1);
            
            if (Library.StaticObjects.ContainsKey(ObjName)) {
                var Lib = Library.StaticObjects[ObjName];

                if (Lib.ContainsKey(FunName)) {
                    return Lib[FunName];
                }
            }

            foreach (var Lib in Engine.Using) {
                if (Lib.ContainsKey(Name))
                    return Lib[Name];
            }

            return null;
        }

        public static Function Get(Engine Engine, jsObject Context, string Name) {
            var Possible = Node.Variable.Eval(Engine, Name, Context);

            if (Possible is Function)
                return Possible as Function;

            if (Name.Contains('.')) {
                var ObjName = Name.Substring("", ".", 0, false, true);
                var FunName = Name.Substring(ObjName.Length + 1);

                var This = Context[ObjName];

                if (This == null) {
                    This = Node.Variable.Eval(Engine, ObjName, Context);
                }

                if (This != null) {
                    var Type = This.GetType();

                    if (Library.TypeLibs.ContainsKey(Type)) {
                        var Lib = Library.TypeLibs[Type];

                        if (Lib.ContainsKey(FunName))
                            return Lib[FunName];
                    }

                    if (Library.Global.ContainsKey(FunName)) {
                        return Library.Global[FunName]; 
                    }

                    var RawName = string.Join(".", Type.FullName, FunName);

                    if (Engine.RawFunctionCache.ContainsKey(RawName)) {
                        return Engine.RawFunctionCache[RawName];
                    }
                    else {
                        var Func = new Helper.MemberFunction(Type, FunName);

                        Engine.RawFunctionCache[RawName] = Func;

                        return Func;
                    }
                }
                else if (Engine.RawInitializerCache.ContainsKey(Name)) {
                    return Engine.RawInitializerCache[Name];
                }
                else {
                    var Init = Helper.Initializer.TryCreate(Name);

                    if (Init != null) {
                        Engine.RawInitializerCache[Name] = Init;

                        return Init;
                    }
                }
            }

            return null;
        }

        public static jsObject GetHandlerArguments(Function Func, jsObject Context, jsObject Storage = null) {
            if (Storage == null)
                Storage = new jsObject();

            if (Func == null)
                return null;

            for (int Index = 0; Index < Func.Arguments.Length; Index++) {
                Node.Variable.Set(Func.Arguments[Index],
                    Storage,
                    Node.Variable.Get(Func.Arguments[Index], Context)
                );
            }

            return Storage;
        }

        public static new Node.Expression Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            var Delta = Index;
            if (Text.Compare("function", Delta)) {
                Delta += 8;
                ConsumeWhitespace(Text, ref Delta);
            }

            Function Func = null;

            var NameStart = Delta;
            while (Delta < Text.Length && IsValidChar(Text[Delta])) 
                Delta++;

            if (NameStart < Delta) {
                Func = new Function(Text.Substring(NameStart, Delta - NameStart));

                ConsumeWhitespace(Text, ref Delta);
            }
            else {
                Func = new Function("");
            }
                        
            if (Text.Compare("(", Delta)) {
                var Open = Delta + 1;
                var Close = Delta;

                ConsumeEval(Text, ref Close);
                Delta = Close;
                ConsumeWhitespace(Text, ref Delta);

                if (Text.Compare("=>", Delta)) {
                    if (!string.IsNullOrEmpty(Func.Name))
                        return null;

                    Delta += 2;
                    ConsumeWhitespace(Text, ref Delta);
                }

                if (Text.Compare("{", Delta)) {
                    Func.Arguments = Text.Substring(Open, Close - Open - 1).ParseCParams();

                    var Exp = Node.Expression.Parse(Engine, Text, ref Delta, LastIndex);

                    if (Exp != null) {
                        Func.Add(Exp);
                        ConsumeWhitespace(Text, ref Delta);
                        Index = Delta;

                        if (!string.IsNullOrEmpty(Func.Name)) {
                            Engine.Functions[Func.Name] = Func;
                            return NoOp;
                        }

                        return Func;
                    }
                }
            }

            return null;
        }
    }
}
