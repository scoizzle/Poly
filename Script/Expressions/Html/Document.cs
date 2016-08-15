using System;
using System.Collections.Generic;
using System.Text;

namespace Poly.Script.Expressions.Html {
    using Nodes;
    using Data;

    public class Document : Element {
        public override void ToEvaluationArray(StringBuilder output, List<Action<StringBuilder, jsObject>> list) {
            foreach (Element e in Elements) {
                e.ToEvaluationArray(output, list);
            }

            if (output.Length > 0) {
                list.Add(Element.StaticAppender(output));
            }
        }

        new public static Element Parse(Engine Engine, StringIterator It) {
			if (It.Consume ('{')) {
				var Start = It.Index;

				if (It.Goto('{', '}')) {
					var Sub = It.Clone (Start, It.Index);
					var List = new List<Element> ();

					while (!Sub.IsDone ()) {
						var Elem = Html.ParseElement (Engine, Sub);

						if (Elem == null)
							break;

						List.Add (Elem);
						Sub.Consume (WhitespaceFuncs);
					}

					It.Consume ('}');
					return new Document () {
						Elements = List.ToArray ()
					};
				}
			}

			return null;
        }

        public static Element Parse(Engine Engine, StringIterator It, Element Storage) {
            if (It.Consume('{')) {
                var Start = It.Index;

                if (It.Goto('{', '}')) {
                    var Sub = It.Clone(Start, It.Index);
                    var List = new List<Element>();

                    while (!Sub.IsDone()) {
                        var Elem = Html.ParseElement(Engine, Sub);

                        if (Elem == null)
                            break;

                        List.Add(Elem);
                        Sub.Consume(WhitespaceFuncs);
                    }

                    It.Consume('}');
                    Storage.Elements = List.ToArray();
                }
            }

            return null;
        }
    }
}
