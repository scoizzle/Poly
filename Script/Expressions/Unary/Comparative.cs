﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Script.Expressions {
    using Nodes;
    using Types;

    public class Comparative : Operator {
        Node Boolean;

        public Comparative(Node Bool, Node Left, Node Right) {
            this.Boolean = Bool;
            this.Left = Left;
            this.Right = Right;
        }

        public override object Evaluate(jsObject Context) {
            if (Left != null && Bool.EvaluateNode(this.Boolean, Context)) {
                return this.Left.Evaluate(Context);
            }
            else if (Right != null) {
                return this.Right.Evaluate(Context);
            }
            return null;
        }

        public static Operator Parse(Engine Engine, string Text, ref int Index, int LastIndex, string Left) {
            if (Text.Compare("?", Index)) {
                Index += 1;
                ConsumeWhitespace(Text, ref Index);

                var L = Engine.Parse(Text, ref Index, LastIndex);
                ConsumeWhitespace(Text, ref Index);

                if (Text.Compare(':', Index)) {
                    var R = Engine.Parse(Text, ref Index, LastIndex);

                    return new Comparative(Engine.Parse(Left, 0), L, R);
                }
            }

            return null;
        }

        public override string ToString() {
            return string.Format("{0} ? {1} : {2}", Boolean, Left, Right);
        }
    }
}