using System;
using System.Text;

namespace Poly.Script.Expressions.Html {
    using Data;
    using Nodes;
	using Types;

    public class PolyTemplate : Element {
		string Format;

        public override void Evaluate(StringBuilder Output, jsObject Context) {
            jsObjectExtension.Template(Context, Format, Output, false);
        }

		new public static Element Parse(Engine Engine, StringIterator It) {
			if (It.Consume ('|')) {
				var Result = String.Parse (Engine, It)?.ToString ();

				if (!string.IsNullOrEmpty (Result))
					return new PolyTemplate () { Format = Result };
			}
			return null;
		}
    }
}
