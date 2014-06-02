using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Poly.Data;

namespace Poly.Script.Node {
    public class Async : Expression {
        public Node Node = null;
        public int MaxExecutionTime = -1;

        public override object Evaluate(Data.jsObject Context) {
            ManualResetEventSlim Event = new ManualResetEventSlim();

            var Worker = new Thread((C) => {
                GetValue(Node, C as Data.jsObject);
                Event.Set();
            });

            Worker.Start(Context);

            if (MaxExecutionTime > -1) {
                var Manager = new Thread(() => {
                    if (!Event.Wait(MaxExecutionTime))
                        Worker.Abort();
                });

                Manager.Start();
            }

            return Worker;
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

                if (Text.Compare("(", Delta)) {
                    var Close = Delta;
                    Delta += 1;
                    ConsumeEval(Text, ref Close);

                    Async.MaxExecutionTime = Text.Substring(Delta, Close - Delta - 1).ToInt();

                    Delta = Close;
                    ConsumeWhitespace(Text, ref Delta);
                }

                Async.Node = Engine.Parse(Text, ref Delta, LastIndex) as Node;

                Index = Delta;
                return Async;
            }

            return null;
        }
    }
}
