using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Poly.Data;

namespace Poly.Script.Expressions {
    using Nodes;

    public class Async : Expression {
        public Node Node = null;

        public override object Evaluate(Data.jsObject Context) {
            return Task.Run<object>(() => {
                return Node.Evaluate(Context);
            });
        }

        public override string ToString() {
            return "async " + base.ToString();
        }

        public static new Async Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare("async", Index)) {
                var Delta = Index + 5;
                var Async = new Async();
                ConsumeWhitespace(Text, ref Delta);

                Async.Node = Engine.Parse(Text, ref Delta, LastIndex) as Node;

                Index = Delta;
                return Async;
            }


            return null;
        }
    }
}
