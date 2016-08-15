using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Nodes {
    public class Operator : Value {
        public Node Left, Right;

        public override object Evaluate(Data.jsObject Context) {
            object L, R;

            if (Left != null)
                L = Left.Evaluate(Context);
            else
                L = null;

            if (Right != null)
                R = Right.Evaluate(Context);
            else
                R = null;

            return Execute(L, R);
        }

        public virtual object Execute(dynamic Left, dynamic Right) {
            return null;
        }

        public static void ConsumeContent(StringIterator It) {
            if (It.Consume('(')) {
                It.Goto('(', ')');
                It.Tick();
            }
            else if (It.Consume('{')) {
                It.Goto('{', '}');
                It.Tick();
            }
            else if (It.Consume('[')) {
                It.Goto('[', ']');
                It.Tick();
            }
            else if (It.Consume('"')) {
                It.Goto('"', '"');
                It.Tick();
            }
            else if (It.Consume('\'')) {
                It.Goto('\'', '\'');
                It.Tick();
            }
            else {
                It.Consume(NameFuncs);
            }
        }
    }
}
