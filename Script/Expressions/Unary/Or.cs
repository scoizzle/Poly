﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Expressions {
    using Nodes;
    using Types;

    public class Or : Operator {
        public Or(Node Left,  Node Right) {
            this.Left = Left;
            this.Right = Right;
        }

        public override object Evaluate(Data.jsObject Context) {
            return Bool.EvaluateNode(Left, Context) || Bool.EvaluateNode(Right, Context);
        }

        public static Node Parse(Engine Engine, StringIterator It, Node Left) {
            return new Or(Left, Engine.ParseOperation(It));
        }

        public override string ToString() {
            return Left.ToString() + " || " + Right.ToString();
        }
    }
}
