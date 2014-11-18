﻿using System;
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
        public int MaxExecutionTime = -1;

        private async Task<object> ExecuteAsync(Data.jsObject Context) {
            return await Task.Run<object>(() => {
                return Node.Evaluate(Context);
            });
        }

        public override object Evaluate(Data.jsObject Context) {
            return Task.Run<object>(() => {
                var Exec = Task.Run<object>(() => {
                    return Node.Evaluate(Context);
                });
                Exec.Wait(MaxExecutionTime);
                return Exec.Result;
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
