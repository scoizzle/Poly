using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;
using Poly.Script.Nodes;

namespace Poly.Script {
    using Nodes;
    using Types;
    using Expressions;
    using Helpers;

    public partial class Engine {
		public static List<Parser> _ExpressionParsers = new List<Parser>() {
			Comment.Parse,
			Invoke.Parse,
			Expression.Parse,
			If.Parse,
			Do.Parse,
			Foreach.Parse,
			For.Parse,
			While.Parse,
			Class.Parse,
			Switch.Parse,
			Async.Parse,
            Await.Parse,
			Try.Parse,
			Include.Parse,
            Persist.Parse,
            Reload.Parse,
            Using.Parse,
			Return.Parse,
			Expressions.Html.Html.Parse,
			Expressions.Html.Function.Parse,
            Function.Parse,
            ParseOperation
        };

		public static List<Parser> _ValueParsers = new List<Parser>() {
            Invoke.Parse,
            Expressions.Html.Html.Parse,
            Expressions.Html.Function.ParseLambda,
            Function.ParseLambda,
            Call.Parse,
            Expressions.Eval.Parse,
            String.Parse,
			Bool.Parse,
            Integer.Parse,
            Float.Parse,
            Object.Parse,
            Variable.Parse
		};

		public Node Parse (StringIterator Text) {
			return ParseExpression (Text) ?? ParseValue (Text);
		}

		public Node ParseExpression(StringIterator It) {
            It.Consume(WhitespaceFuncs);

            if (!It.IsDone()) {
                Node Current = null;
				if (_ExpressionParsers.Any (f => (Current = f (this, It)) != null)) {
					return Current;
				}
			}

			return null;
		}

		public Node ParseValue(StringIterator It) {
            It.Consume(WhitespaceFuncs);

            if (!It.IsDone ()) {
				Node Current = null;
				if (_ValueParsers.Any (f => (Current = f (this, It)) != null)) {
					return Current;
				}
			}

			return null;
        }

        public Node ParseOperation(StringIterator It) {
            return ParseOperation(this, It);
        }

        public static Node ParseOperation(Engine Engine, StringIterator It) {
            var Node = Engine.ParseValue(It);

            while (!It.IsDone()) {
                It.ConsumeWhitespace();

                var List = Node == null ? 
                    Expressions.Unary.Parser.RightHandedParsers : 
                    Expressions.Unary.Parser.Parsers;

                if (!List.Any(p => {
                    if (It.Consume(p.Key)) {
                        It.ConsumeWhitespace();

                        Node = p.Value(Engine, It, Node);
                        return true;
                    }
                    return false;
                })) break;
            }

            return Node;
        }

        public Node ParseExpressions(StringIterator Text, Node Storage) {
			var List = new List<Node> ();

			while (!Text.IsDone ()) {
				var Node = ParseExpression (Text);

				if (Node == null)
					break;

				if (Node == Expression.NoOperation)
					continue;

				List.Add (Node);
			}

			if (List.Count != 0)
				Storage.Elements = List.ToArray ();

			return Storage;
		}

		public Node ParseValues(StringIterator Text, Node Storage) {
			var List = new List<Node> ();

			while (!Text.IsDone ()) {
				var Node = ParseValue (Text);

				if (Node == null)
					break;

				List.Add (Node);
			}

			if (List.Count != 0)
				Storage.Elements = List.ToArray ();

			return Storage;
        }

        public Node ParseOperations(StringIterator Text, Node Storage) {
            var List = new List<Node>();

            while (!Text.IsDone()) {
                var Node = ParseOperation(Text);

                if (Node == null)
                    break;

                List.Add(Node);
            }

            if (List.Count != 0)
                Storage.Elements = List.ToArray();

            return Storage;
        }
        
        public bool Parse(string Text) {
            return ParseExpressions(new StringIterator(Text), this) != null;
        }
    }
}
