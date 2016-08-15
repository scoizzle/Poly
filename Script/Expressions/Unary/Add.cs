using System;

namespace Poly.Script.Expressions {
    using Nodes;
    public class Add : Operator {
        public Add(Node Left,  Node Right) {
            this.Left = Left;
            this.Right = Right;
        }

        public override object Execute(dynamic Left, dynamic Right) {
            try { return Left + Right; }
            catch { return null; }
        }

        public static Node Assignment(Engine Engine, StringIterator It, Node Left) {
            return new Assign(Left as Variable, 
                new Add(
                    Left, 
                    Engine.ParseOperation(It)
                )
            );
        }

        public static Node Iterator(Engine Engine, StringIterator It, Node Left) {
            return new Assign(Left as Variable, 
                new Add(Left, new StaticValue(1))
            );
        }

        public static Node Parse(Engine Engine, StringIterator It, Node Left) {
            return new Add(Left, Engine.ParseValue(It));
        }
        
        public override string ToString() {
            return Left.ToString() + " + " + Right.ToString();
        }
    }
}
