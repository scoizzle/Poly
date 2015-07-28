﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Expressions {
    using Nodes;
    using Types;
    public class LessThan : Operator {
        public LessThan(Node Left,  Node Right) {
            this.Left = Left;
            this.Right = Right;
        }

        public override object Execute(dynamic Left, dynamic Right) {
            try { return Left < Right; }
            catch { return false; }
        }

        public static Operator Parse(Engine Engine, string Text, ref int Index, int LastIndex, string Left) {
            if (Text.Compare("<", Index)) {
                Index += 1;
                ConsumeWhitespace(Text, ref Index);

                return new LessThan(
                    Engine.Parse(Left, 0),
                    Engine.Parse(Text, ref Index, LastIndex)
                );
            }

            return null;
        }

        public override string ToString() {
            return Left.ToString() + " < " + Right.ToString();
        }
    }
}
