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

            if (this.Name.Contains('.')) {
                this.ObjectName = Name.Substring("", ".", 0, false, true);
                this.Name = Name.Substring(this.ObjectName.Length + 1);
            }
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
                    if ((Value as Function).Arguments.Length > 0) {
                        Value = GetFunctionHandler(Value as Function, Context);
                    }
                    else {
                        Value = (Value as Function).GetSystemHandler();
                    }
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

        public override string ToString() {
            return "function " + Name + "(" + string.Join(", ", Arguments) + ")";
        }

        public static Function GetTypeConstructor(Engine Engine, string Name) {
            CustomType Type;

            if (Engine.Types.TryGetValue(Name, out Type)) {
                return Type.Construct;
            }

            return null;
        }

        public static Function GetConstructor(Engine Engine, string Name) {
            Function Func;

            if ((Func = GetTypeConstructor(Engine, Name)) != null) {
                return Func;
            }

            if (Library.Constructors.TryGetValue(Name, out Func)) {
                return Func;
            }

            if (Engine.RawInitializerCache.TryGetValue(Name, out Func)) {
                return Func;
            }

            var Init = Helper.Initializer.TryCreate(Name);

            if (Init != null) {
                Engine.RawInitializerCache[Name] = Init;

                return Init;
            }

            return null;
        }

        public static Function Get(Engine Engine, string Name, object This = null) {
            Function Func = (This as Function);

            if (Func != null)
                return Func;

            if (This == null) {
                if (Engine.Functions.TryGetValue(Name, out Func)) {
                    return Func;
                }

                if ((Func = GetConstructor(Engine, Name)) != null) {
                    return Func;
                }

                foreach (var Lib in Engine.Using) {
                    if (Lib.TryGetValue(Name, out Func)) {
                        return Func;
                    }
                }

                if (Name.IndexOf('.') > -1) {
                    Library Lib;
                    var ObjectName = Name.Substring(0, Name.LastIndexOf('.'));
                    var FunctionName = Name.Substring(ObjectName.Length + 1);

                    if (Library.StaticObjects.TryGetValue(ObjectName, out Lib)) {
                        if (Lib.TryGetValue(FunctionName, out Func)) {
                            return Func;
                        }
                    }
                }
            }
            else {
                Library Lib;
                Type Type = This.GetType();

                if (Type == typeof(CustomTypeInstance)) {
                    var CustomType = (This as CustomTypeInstance).Type;

                    if ((Func = CustomType.GetFunction(Name)) != null) {
                        return Func;
                    }
                }

                if (Library.TypeLibs.TryGetValue(Type, out Lib)) {
                    if (Lib.TryGetValue(Name, out Func)) {
                        return Func;
                    }
                }

                if (Library.Global.TryGetValue(Name, out Func)) {
                    return Func;
                }

                if (Engine.RawFunctionCache.TryGetValue(Type.Name + Name, out Func)) {
                    return Func;
                }

                return (Engine.RawFunctionCache[Type.Name + Name] = new Helper.MemberFunction(Type, Name));
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
        
        public static Event.Handler GetFunctionHandler(Function Func, jsObject Context) {
            return (Args) => {
                Function.GetHandlerArguments(Func, Context, Args);

                return Func.Evaluate(Args);
            };
        }

        public static new Expression Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            return Parse(Engine, Text, ref Index, LastIndex, true);
        }

        public static Expression Parse(Engine Engine, string Text, ref int Index, int LastIndex, bool IsEngineWide = true) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            var Delta = Index;
            if (Text.Compare("function", Delta)) {
                Delta += 8;
                ConsumeWhitespace(Text, ref Delta);
            }

            Function Func = null;

            var Open = Delta;
            var Close = Delta;

            ConsumeValidName(Text, ref Close);
            Delta = Close;
            ConsumeWhitespace(Text, ref Delta);

            if (Text.Compare("(", Delta)) {
                Func = new Function(Text.Substring(Open, Close - Open));

                Open = Delta + 1;
                Close = Delta;

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

                        if (!string.IsNullOrEmpty(Func.Name) && IsEngineWide) {
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
