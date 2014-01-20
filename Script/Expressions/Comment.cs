using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Node {
    class Comment : Expression {
        public static new object Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare("//", Index)) {
                var Delta = Index + 2;

                while (Delta < LastIndex) {
                    if (Text.Compare(Environment.NewLine, Delta))
                        break;

                    Delta++;
                }

                Index = Delta;
            }
            else if (Text.Compare("/*", Index)) {
                var Delta = Index + 2;

                while (Delta < LastIndex) {
                    if (Text.Compare("*/", Delta))
                        break;

                    Delta++;
                }

                Index = Delta;
            }

            return null;
        }
    }
}
