using System;
using System.Text;

using Poly.Data;

namespace Poly.Script.Nodes {
    public class Node {
        public Node[] Elements;

		public static Node ContextAccess;
        public static Func<char, bool>[] WhitespaceFuncs,
                                         NameFuncs,
                                         CallNameFuncs;
	
		static Node() {
			ContextAccess = new Helpers.ContextAccessor ();

			WhitespaceFuncs = new Func<char, bool>[] {
				char.IsWhiteSpace, 
				c => c == ';' || c == ','
			};

			NameFuncs = new Func<char, bool>[] {
				char.IsLetterOrDigit, 
				c => c == '.' || c == '_'
			};

            CallNameFuncs = new Func<char, bool>[] {
                char.IsLetterOrDigit,
                c => c == '.' || c == '_' || c == '[' || c == ']'
            };
		}

        public virtual object Evaluate(jsObject Context) {
            if (Elements == null)
                return null;

            for (int i = 0; i < Elements.Length; i++) {
                var N = Elements[i];

                var R = N as Expressions.Return;
                if (R != null)
                    return R;

                var Value = N.Evaluate(Context);

                if (Value == null)
                    continue;

                if ((R = Value as Expressions.Return) != null)
                    return R;

                if (Value == Expression.Break || Value == Expression.Continue)
                    return Value;
            }
            return null;
        }

        public override string ToString() {
            if (Elements == null) 
                return string.Empty;

            StringBuilder Output = new StringBuilder();

            foreach (var Elem in Elements) {
                Output.Append(Elem.ToString()).AppendLine(";");
            }

            return Output.ToString();
        }
    }
}
