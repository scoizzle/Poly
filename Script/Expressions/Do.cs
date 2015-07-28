﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Poly.Script.Expressions {
    using Nodes;
    using Types;
    using Expressions;

    public class Do : Expression {
        public Node Boolean = null;

        public override object Evaluate(Data.jsObject Context) {
            do {
                for (int i = 0; i < Elements.Length; i++) {
                    var Node = Elements[i];

                    if (Node is Return)
                        return Node;

                    var Result = Node.Evaluate(Context);

                    if (Result == Break)
                        return null;

                    if (Result == Continue)
                        break;
                }
            }
            while (Bool.EvaluateNode(Boolean, Context) && Thread.CurrentThread.ThreadState == ThreadState.Running);

            return null;
        }

        public override string ToString() {
            return "do {" + base.ToString() + " } while (" + Boolean.ToString() + ");";
        }

        public static new Do Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare("do", Index)) {
                var Delta = Index + 2;
                ConsumeWhitespace(Text, ref Delta);

                if (Text.Compare("{", Delta)) {
                    var Do = new Do();
                    var Open = Delta + 1;
                    var Close = Delta;

                    ConsumeExpression(Text, ref Close);

                    if (Delta == Close)
                        return null;

                    Engine.Parse(Text, ref Open, Close, Do);

                    Open = Close + 1;
                    ConsumeWhitespace(Text, ref Open);

                    if (Text.Compare("while", Open)) {
                        Open += 5;
                        ConsumeWhitespace(Text, ref Open);

                        if (Text.Compare("(", Open)) {
                            Close = Open;
                            Open ++;

                            ConsumeEval(Text, ref Close);

                            Do.Boolean = Engine.Parse(Text, ref Open, Close);
                            Index = Close;

                            return Do;
                        }
                    }
                }
            }

            return null;
        }
    }
}
