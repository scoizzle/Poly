using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Poly.Data;

namespace Poly.Script.Expressions {
    using Nodes;

	public class Foreach : Expression {
		public Node List;
        public Variable Variable, ValueVar;

		public Foreach() {
			List = null;
			Variable = null;
			ValueVar = null;
		}

        private object LoopNodes<K, V>(jsObject Context, K Key, V Value) {				
            var Var = new jsObject();
            Variable.Assign(Context, Var);

            Var.Set("Key", Key);
            Var.Set("Value", Value);

            foreach (var Node in Elements) {
                var Ret = Node as Return;

                if (Ret != null)
                    return Ret;
				
                if (Node != null)
                    Node.Evaluate(Context);             

                if (Ret == Break || Ret == Continue)
                    return Ret;
            }

            Variable.Assign(Context, null);
            return null;
        }

		private object LoopNodes<K, V>(jsObject Context, Variable KeyName, K Key, Variable ValueName, V Value) {
			KeyName.Assign (Context, Key);
			ValueName.Assign (Context, Value);

			foreach (var Node in Elements) {
				var Ret = Node as Return;

				if (Ret != null)
					return Ret;

				if (Node != null)
					Node.Evaluate (Context);             

				if (Ret == Break || Ret == Continue)
					return Ret;
			}
			return null;
		}

        private object LoopString(jsObject Context, string String) {
            if (string.IsNullOrEmpty(String))
                return null;

            for (int i = 0 ; i < String.Length; i ++) {
				var Result = ValueVar == null ?
					LoopNodes (Context, i, String [i]) :
					LoopNodes (Context, Variable, i, ValueVar, String [i]);;

                var Ret = Result as Return;

                if (Ret != null)
                    return Ret;

                if (Result == Break)
                    break;
            }
            return null;
        }

        private object LoopObject(jsObject Context, jsObject Object) {
            if (Object == null || Object.IsEmpty)
                return null;

            foreach (var Pair in Object) {
				var Result = ValueVar == null ?
					LoopNodes (Context, Pair.Key, Pair.Value) :
					LoopNodes (Context, Variable, Pair.Key, ValueVar, Pair.Value);
				
                var Ret = Result as Return;

                if (Ret != null)
                    return Ret;
                
                if (Result == Break)
                    break;
            }

            return null;
        }

        private object LoopArray(jsObject Context, Array Array) {
			for (int Index = 0; Index < Array.Length; Index++) {
				var Result = ValueVar == null ?
					LoopNodes (Context, Index, Array.GetValue(Index)) :
					LoopNodes (Context, Variable, Index, ValueVar, Array.GetValue(Index));
                
                var Ret = Result as Return;

                if (Ret != null)
                    return Ret;

                if (Result == Break)
                    break;

            }

            return null;
        }

        public override object Evaluate(jsObject Context) {
            if (Variable == null || List == null) {
                return null;
            }

            var Collection = List.Evaluate(Context);

            if (Collection == null)
                return null;

            object Value = null;
			if (Collection is jsObject)
				Value = LoopObject(Context, Collection as jsObject);
			else if (Collection is string)
				Value = LoopString(Context, Collection as string);
			else if (Collection is Array)
				Value = LoopArray(Context, Collection as Array);

            if (!(Value is Return))
                Variable.Assign(Context, null);

            return Value;
        }

        public override string ToString() {
            return "foreach (" + Variable.ToString() + " in " + List.ToString() + ")" +
                base.ToString();
        }

		new public static Node Parse(Engine Engine, StringIterator It) {
			if (It.Consume ("foreach")) {
				It.Consume (WhitespaceFuncs);

				if (It.Consume ('(')) {
					var Node = new Foreach ();
					var Key = Engine.ParseOperation (It);

					if (Key is KeyValuePair) {
						var Pair = (Key as KeyValuePair);
						Node.Variable = Pair.Left as Variable;
						Node.ValueVar = Pair.Right as Variable;
					} else if (Key is Variable) {
						Node.Variable = Key as Variable;
					} else
						return null;

					It.Consume (WhitespaceFuncs);
					if (It.Consume ("in")) {
						It.Consume (WhitespaceFuncs);

						Node.List = Engine.ParseValue (It);

						It.Consume (WhitespaceFuncs);
						if (It.Consume (')')) {
							It.Consume (WhitespaceFuncs);

							if (It.IsAt ('{')) {
								Expression.Parse (Engine, It, Node);
							}
							else {
								Node.Elements = new Node[] {
									Engine.ParseExpression (It)
								};
							}

							return Node;
						}
					}
				}
			}
			return null;
		}
    }
}
