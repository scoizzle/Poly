using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Expressions {
    using Nodes;
    using Types;

    public class Devide : Operator {
        public Devide(Node Left,  Node Right) {
            this.Left = Left;
            this.Right = Right;
        }

        public override object Execute(dynamic Left, dynamic Right) {
            try { return Left / Right; }
            catch { return null; }
        }

        public static Node Assignment(Engine Engine, StringIterator It, Node Left) {
            return new Assign(Left as Variable,
                new Devide(
                    Left,
                    Engine.ParseOperation(It)
                )
            );
        }

        public static Node Parse(Engine Engine, StringIterator It, Node Left) {
            return new Devide(Left, Engine.ParseValue(It));
        }

        public override string ToString() {
            return Left.ToString() + " / " + Right.ToString();
        }
    }
}
