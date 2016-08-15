using System;

namespace Poly.Script.Types {
    using Nodes;

	public class String : Value {
        public readonly static StaticValue Empty = new StaticValue(string.Empty);

		public static Node Parse(Engine Engine, StringIterator It) {
            if (It.IsAt('"')) {
                return new StaticValue(It.Extract('"', '"'));
            }
            else if (It.IsAt('\'')) {
                return new StaticValue(It.Extract('\'', '\''));
            }

            return null;
		}
    }
}
