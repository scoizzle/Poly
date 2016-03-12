using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Script.Types {
    using Nodes;
    public class Bool : Value {
        public static bool EvaluateNode(Node Node, jsObject Context) {
            if (Node == null)
                return false;

            var Value = Node.Evaluate(Context);

            if (Value == null)
                return false;

            if (Value is Boolean)
                return (Boolean)(Value);

            if (Value is string)
                return (Value as string).Length > 0;

            if (Value is jsObject)
                return !(Value as jsObject).IsEmpty;

            if (Value is byte || 
                Value is char ||
                Value is short ||
                Value is int ||
                Value is long ||
                Value is float ||
                Value is double ||
                Value is decimal) {
                dynamic dyn = Value;
                return dyn > 0;
            }

            return true;
        }

        public static Node Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare("true", Index, true)) {
                Index += 4;
                return Expression.True;
            }
            else if (Text.Compare("false", Index, true)) {
                Index += 5;
                return Expression.False;
            }

            return null;
        }
    }
}
