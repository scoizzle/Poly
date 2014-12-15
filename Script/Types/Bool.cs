using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Script.Types {
    using Nodes;
    public class Bool : DataType<bool> {
        public new static object Equal(bool Left, object Right) {
            if (Right is bool) {
                return Left == (bool)Right;
            }
            else if (Right is int) {
                return (Left ? 1 : 0) == ((int)Right % 2);
            }
            else if (Right is double) {
                return (Left ? 1 : 0) == ((double)Right % 2);
            }
            else if (Right is string) {
                return Left.ToString() == Right.ToString();
            }
            return null;
        }

        public static bool EvaluateNode(Node Node, jsObject Context) {
            if (Node == null)
                return false;

            var Value = Node.Evaluate(Context);

            if (Value is Boolean)
                return (Boolean)(Value);

            if (!string.IsNullOrEmpty(Value as string))
                return true;

            if (Value is Int32)
                return (Int32)(Value) != 0;

            if (Value is Double)
                return (Double)(Value) != Double.NaN;

            var Obj = Value as jsObject;
            if (Obj != null)
                return !Obj.IsEmpty;

            return false;
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
