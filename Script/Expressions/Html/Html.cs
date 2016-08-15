using System;
using System.Collections.Generic;
using System.Linq;

namespace Poly.Script.Expressions.Html {
    using Nodes;

    public class Html : Expression {
        public delegate Element Parser(Engine Engine, StringIterator It);
        public static Parser[] _Parsers = new Parser[] {
			Comment.Parse,
            If.Parse,
            Foreach.Parse,
            For.Parse,
            StaticValue.Parse,
			PolyTemplate.Parse,
            Attribute.Parse,
            ComplexElement.Parse,
            Call.Parse,
			Variable.Parse
        };
        
        new public static Node Parse(Engine Engine, StringIterator It) {
			var Start = It.Index;
            if (It.Consume("html")) {
                It.ConsumeWhitespace();

				if (It.IsAt('{')) {
                    var Node = Document.Parse(Engine, It);

                    if (Node != null) {
                        return Optimizer.FromDocument(Node as Document);
                    }
				}
            }
			It.Index = Start;
            return null;
        }

        public static Element ParseElement(Engine Engine, StringIterator It) {
			It.Consume(WhitespaceFuncs);

			if (!It.IsDone()) {
                Element Current = null;
                if (_Parsers.Any(f => (Current = f(Engine, It)) != null)) {
                    return Current;
                }
            }

            return null;
        }

        public static Element ParseElements(Engine Engine, StringIterator It, Element Storage) {
            var List = new List<Node>();

            while (!It.IsDone()) {
                var Node = ParseElement(Engine, It);

                if (Node == null)
                    break;

                List.Add(Node);
            }

            if (List.Count != 0)
                Storage.Elements = List.ToArray();

            return Storage;
        }
    }
}
