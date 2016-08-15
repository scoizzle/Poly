using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Expressions {
    using Nodes;
    using Types;
    public class GreaterThan : Operator {
        public GreaterThan(Node Left,  Node Right) {
            this.Left = Left;
            this.Right = Right;
        }

        public override object Execute(dynamic Left, dynamic Right) {
            try { return Left > Right; }
            catch { return false; }
        }

        public static Node Parse(Engine Engine, StringIterator It, Node Left) {
            return new GreaterThan(Left, Engine.ParseValue(It));
        }
        
        public override string ToString() {
            return Left.ToString() + " > " + Right.ToString();
        }
    }
}
