﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Script {
    using Node;

    public class Function : Node.Expression {
        public Variable Object = null;

        public string Name = "";
        public string[] Arguments = new string[0];

        public Function() { }
        public Function(string Name) {
            this.Name = Name;
        }

        public object Call(jsObject Context, jsObject ArgList, object This = null, Engine Engine = null) {
            var Args = new jsObject();
            var Index = 0;

            ArgList.ForEach((Key, Value) => {
                var Name = Arguments.Length == 0 || Index >= Arguments.Length ?
                    Key :
                    Arguments[Index];

                var VFunc = Value as Function;

                if (VFunc != null) {
                    if (VFunc.Arguments.Length > 0) {
                        Value = GetFunctionHandler(VFunc, Context);
                    }
                    else {
                        Value = VFunc.GetSystemHandler();
                    }
                }

                Variable.Set(Name, Args, GetValue(Value, Context));
                Index++;
            });

            Variable.Set(
                "this",
                Args,
                This == null && Object != null ?
                    Object.Evaluate(Context) :
                    This
            );

            return this.Evaluate(Args);
        }

        public override string ToString() {
            return "function " + Name + "(" + string.Join(", ", Arguments) + ")";
        }

        public static Function GetTypeConstructor(Engine Engine, string Name) {
            CustomType Type = Engine.Types.Get<CustomType>(Name);

            if (Type != null) {
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

            if (Library.TypeLibsByName.ContainsKey(Name)) {
                Func = Library.TypeLibsByName[Name].Get<Function>(Name);
                return Func;
            }

            if (Engine.RawInitializerCache.TryGetValue(Name, out Func)) {
                return Func;
            }

            return Helper.Initializer.Get(Name);
        }

        public static Function Get(Engine Engine, string Name, object This) {
            if (This == null)
                return null;

            Function Func = (This as Function);

            if (Func != null)
                return Func;

            CustomTypeInstance CustomThis = This as CustomTypeInstance;

            if (CustomThis != null) {
                var CustomType = CustomThis.Type;

                if ((Func = CustomType.GetFunction(Name)) != null) {
                    return Func;
                }
            }

            if (Library.Global.TryGetValue(Name, out Func)) {
                return Func;
            }

            Library Lib;
            Type Type = This.GetType();

            if (Library.TypeLibs.TryGetValue(Type, out Lib)) {
                if (Lib.TryGetValue(Name, out Func)) {
                    return Func;
                }
            }

            return Helper.MemberFunction.Get(Type, Name);
        }

        public static Function GetStatic(Engine Engine, string Name) {
            Function Func;

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

            int Index = Name.LastIndexOf('.');
            if (Index > -1) {
                Library Lib;
                var ObjectName = Name.Substring(0, Index);
                var FunctionName = Name.Substring(Index + 1);

                if (Library.StaticLibraries.TryGetValue(ObjectName, out Lib)) {
                    if (Lib.TryGetValue(FunctionName, out Func)) {
                        return Func;
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

                if (Func.Name.IndexOf('.') > -1) {
                    Open = 0;
                    Close = Func.Name.LastIndexOf('.');

                    Func.Object = Variable.Parse(Engine, Func.Name, ref Open, Close);
                    Func.Name = Func.Name.Substring(Close + 1);
                }

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
                    if (Node.Expression.Parse(Engine, Text, ref Delta, LastIndex, Func)) {
                        Func.Arguments = Text.Substring(Open, Close - Open - 1).ParseCParams();

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
