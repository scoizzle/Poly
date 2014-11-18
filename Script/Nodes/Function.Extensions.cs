using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;
using Poly.Script.Nodes;

namespace Poly.Script {
    public static class FunctionExtensions {
        public static Event.Handler GetFunctionHandler(this Function Func, jsObject Context) {
            if (Func == null)
                return null;

            return (Args) => {
                GetHandlerArguments(Func, Context, Args);

                return Func.Evaluate(Args);
            };
        }

        public static jsObject GetFunctionArguments(this Function This, jsObject Context, jsObject ArgList) {
            var Args = new jsObject();
            var Index = 0;

            foreach (var Pair in ArgList) {
                var Key = Pair.Key;
                var Value = Pair.Value;
                var Name = "";

                if (This != null) {
                    if (Index >= This.Arguments.Length) {
                        Name = Key;
                    }
                    else {
                        Name = This.Arguments[Index];
                    }
                }
                else {
                    Name = Key;
                }

                var VFunc = Value as Function;

                if (VFunc != null) {
                    if (VFunc.Arguments.Length > 0) {
                        Value = GetFunctionHandler(VFunc, Context);
                    }
                    else {
                        Value = new Event.Handler(VFunc.Evaluate);
                    }
                }

                var Node = Value as Node;
                if (Node != null) {
                    Args[Name] = Node.Evaluate(Context);
                }
                else {
                    Args[Name] = null;
                }
                Index++;
            };

            return Args;
        }

        public static jsObject GetHandlerArguments(this Function Func, jsObject Context, jsObject Storage = null) {
            if (Storage == null)
                Storage = new jsObject();

            if (Func == null)
                return null;

            for (int Index = 0; Index < Func.Arguments.Length; Index++) {
                Storage[Func.Arguments[Index]] = Context[Func.Arguments[Index]];
            }

            return Storage;
        }
    }
}
