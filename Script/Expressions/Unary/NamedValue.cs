using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Node {
    public class NamedValue : Operator {
        public NamedValue(object Left, object Right) {
            this.Left = Left;
            this.Right = Right;
        }

        public static object Parse(Engine Engine, string Text, ref int Index, int LastIndex, string Left) {
            if (Text.Compare(":", Index)) {
                return new NamedValue(
                    Engine.Parse(Left, 0),
                    Engine.Parse(Text, ref Index, LastIndex)
                );
            }
            return null;
        }
    }
}
