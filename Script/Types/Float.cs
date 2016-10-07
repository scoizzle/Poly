using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Types {
    using Nodes;

    public class Float : Value {
		public static Node Parse(Engine Engine, StringIterator It) {
			var Start = It.Index;
			It.Consume ('+');
			It.Consume ('-');

			It.Consume (
				c => c == '.',
				char.IsDigit
			);

			if (It.Index > Start) {
				double Value;

				if (double.TryParse (It.Substring (Start, It.Index - Start), out Value))
					return new StaticValue(Value);
			}

			It.Index = Start;
			return null;
		}
    }
}
