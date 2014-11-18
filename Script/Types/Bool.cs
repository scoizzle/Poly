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

        public static bool EvaluateNode(object Node, jsObject Context) {
            object Val;
            var N = Node as Node;

            if (N != null)
                Val = N.Evaluate(Context);
            else
                Val = Node;
            
            if (Val == null)
                return false;

            if (Val is bool) {
                return (bool)Val;
            }
            else if (Val is string) {
                return !string.IsNullOrEmpty((string)Val);
            }
            else if (Val is int) {
                return (int)Val!= 0;
            }
            else if (Val is double) {
                return !double.IsNaN((double)Val);
            }
            else if (Val is jsObject) {
                return !(Val as jsObject).IsEmpty;
            }

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
