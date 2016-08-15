using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Script.Types {
    using Nodes;
    public class Object : Value {
        private bool IsArray;

        public Object() {
            IsArray = false;
        }

        public override object Evaluate(jsObject Context) {
            var Obj = new jsObject() { IsArray = IsArray };

            if (Elements == null)
                return Obj;

            if (IsArray) { 
                foreach (Node Element in Elements) {
                    Obj.AssignValue(Obj.Count.ToString(), Element.Evaluate(Context));
                }
            }
            else {
                foreach (Expressions.KeyValuePair Pair in Elements) {
                    var Result = Pair.Eval(Context);
                    var Key = Result.Key == null ?
                        Obj.Count.ToString() :
                        Result.Key.ToString();

                    Obj.AssignValue(Key, Result.Value);
                }
            }

            return Obj;
        }

		public static Node Parse(Engine Engine, StringIterator It) {
			if (It.Consume ('{')) {
				var Start = It.Index;

				if (It.Goto ('{', '}')) {
					var Sub = It.Clone (Start, It.Index);
					var Node = new Object ();

					if (Engine.ParseOperations (Sub, Node) != null) {
						if (Node.Elements == null || Node.Elements.All (n => n is Expressions.KeyValuePair)) {
							Sub.Consume (WhitespaceFuncs);

							if (Sub.IsDone() && It.Consume('}'))
								return Node;
						}
					}
				}
			} else if (It.Consume ('[')) {
				var Start = It.Index;

				if (It.Goto ('[', ']')) {
					var Sub = It.Clone (Start, It.Index);
					var Node = new Object() { IsArray = true };

					if (Engine.ParseValues (Sub, Node) != null) {
						Sub.Consume (WhitespaceFuncs);

						if (Sub.IsDone() && It.Consume(']'))
							return Node;
					}
				}
			}

			return null;
		}

		public override string ToString () {
			if (IsArray) {
				return '[' + (Elements != null ? string.Join<Node> (",", Elements) : "") + ']';
			}
			else {
				return '{' + (Elements != null ? string.Join<Node> (",", Elements) : "") + '}';
			}
		}
    }
}

