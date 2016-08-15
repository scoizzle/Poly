using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Poly.Script.Expressions {
    using Nodes;
    using Types;

    public class Switch : Expression {
        public Node Object = null;
        public Case Default = null;

        public override object Evaluate(Data.jsObject Context) {
            for (int i = 0; i < Elements.Length; i++) {
                var Case = Elements[i] as Case;

                if (Case == null)
                    continue;

                if (Bool.EvaluateNode(Case.Object, Context))
                    return Case.Evaluate(Context);
            }

            if (Default != null)
                return Default.Evaluate(Context);

            return null;
        }

        public override string ToString() {
            return "switch (" + Convert.ToString(Object) + ") " + base.ToString();
        }

		new public static Node Parse(Engine Engine, StringIterator It) {
			if (It.Consume ("switch")) {
				It.Consume (WhitespaceFuncs);

				var Node = new Switch () {
					Object = Eval.Parse(Engine, It)
				};

				if (Node.Object != null) {
					It.Consume (WhitespaceFuncs);

					if (It.Consume ('{')) {
						var Start = It.Index;

						if (It.Goto ('{', '}') && It.Consume('}')) {
							var Sub = It.Clone (Start, It.Index);
							var List = new List<Node> ();

							while (!Sub.IsDone ()) {
								Sub.Consume (WhitespaceFuncs);

								var Member = Case.Parse(Engine, Sub) as Case;

								if (Member == null)
									break;

								if (Member.IsDefault) {
									Node.Default = Member;
								} else if (Member.Object is Operator) {
									(Member.Object as Operator).Left = Node.Object;
								} else {
									Member.Object = new Equal (Node.Object, Member.Object);
								}

								List.Add (Member);
							}

							Node.Elements = List.ToArray ();
							return Node;
						}
					}

				}
			}
			return null;
		}
    }
}
