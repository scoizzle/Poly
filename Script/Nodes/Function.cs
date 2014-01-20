using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Script {
    public class Function : Node.Expression {
        public string Name = "";
        public string[] Arguments = new string[0];

        public override object Evaluate(jsObject Context) {
            return base.Evaluate(Context);
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
                    else if (SystemFunction.Raw.Exists(Type, FunName)) {
                        var Handler = new SystemFunction.Raw() {
                            Type = Type,
                            Name = FunName
                        };

                        Engine.RawFunctionCache[RawName] = Handler;

                        return Handler;
                    }
                }
                else if (Library.StaticObjects.ContainsKey(ObjName)) {
                    var Lib = Library.StaticObjects[ObjName];

                    if (Lib.ContainsKey(FunName)) {
                        return Lib[FunName];
                    }
                }
                else if (Engine.RawInitializerCache.ContainsKey(Name)) {
                    return Engine.RawInitializerCache[Name];
                }
                else if (SystemFunction.Initializer.Exists(Name)) {
                    var Handler = new SystemFunction.Initializer() {
                        Type = SystemFunction.Initializer.SearchForType(Name),
                        Name = Name
                    };

                    Engine.RawInitializerCache[Name] = Handler;

                    return Handler;
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

            Function Func = new Function();

            var NameStart = Delta;
            while (Delta < Text.Length && IsValidChar(Text[Delta])) 
                Delta++;

            if (NameStart < Delta) {
                Func.Name = Text.Substring(NameStart, Delta - NameStart);
                ConsumeWhitespace(Text, ref Delta);
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
                        }

                        return Func;
                    }
                }
            }

            return null;
        }
    }
}
