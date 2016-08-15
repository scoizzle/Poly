using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Nodes {
    public class Expression : Node {
        public static readonly Node NoOperation,
                                    Break,
                                    Continue;

        public static readonly StaticValue Null,
                                           True,
                                           False;


        static Expression() {
            NoOperation = new Node();
            Break = new Node();
            Continue = new Node();

            Null = new StaticValue(null);
            True = new StaticValue(true);
            False = new StaticValue(false);
        }

		public static bool Parse(Engine Engine, StringIterator It, Node Storage) {
			if (It.Consume ('{')) {
				var Open = It.Index;

				if (It.Goto ('{', '}')) {
					if (Engine.ParseExpressions (It.Clone (Open, It.Index), Storage) != null) {
						return It.Consume ('}');
					}
				}
			}
			return false;
		}


        public static bool ParseValues(Engine Engine, StringIterator It, Node Storage) {
            if (It.Consume('{')) {
                var Open = It.Index;

                if (It.Goto('{', '}')) {
                    if (Engine.ParseValues(It.Clone(Open, It.Index), Storage) != null) {
                        return It.Consume('}');
                    }
                }
            }
            return false;
        }


        public static Node Parse(Engine Engine, StringIterator It) {
			if (It.Consume ("break"))
				return Break;
			if (It.Consume ("continue"))
				return Continue;
			if (It.Consume ("null"))
				return Null;
			
			if (It.IsAt ('{')) {
				var Node = new Expression ();

				if (Parse (Engine, It, Node))
					return Node;
			}

			return null;
		}
    }
}
