using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Script.Node {
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
            var Val = Node;
            if (Node is Node) {
                Val = ((Node)Node).Evaluate(Context);
            }

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

            return true;
        }

        public static new object Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare("true", Index, true)) {
                Index += 4;
                return true;
            }
            else if (Text.Compare("false", Index, true)) {
                Index += 5;
                return false;
            }

            return null;
        }
    }
}
