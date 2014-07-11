﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Node {
    public class LessThan : Operator {
        public LessThan(object Left, object Right) {
            this.Left = Left;
            this.Right = Right;
        }

        public override object Evaluate(Data.jsObject Context) {
            var L = GetLeft(Context);
            var R = GetRight(Context);

            if (L == null || R == null) {
                if (L != null)
                    return L;
                if (R != null)
                    return R;
                return null;
            }

            return DataType.LessThan(L, R);
        }

        public static Operator Parse(Engine Engine, string Text, ref int Index, int LastIndex, string Left) {
            if (Text.Compare("<", Index)) {
                Index += 1;
                Text.ConsumeWhitespace(ref Index);

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
