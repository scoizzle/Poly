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
