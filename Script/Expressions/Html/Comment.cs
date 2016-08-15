using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Poly.Data;

namespace Poly.Script.Expressions.Html {
    public class Comment : Element {
        public string Content;

        public Comment(string Str) {
            Content = Str;
        }

        public override void Evaluate(StringBuilder Output, jsObject Context) {
            Output.Append("<!-- ").Append(Content).Append(" -->");
        }

        public override void ToEvaluationArray(StringBuilder output, List<Action<StringBuilder, jsObject>> list) {
            output.Append("<!-- ").Append(Content).Append(" -->");
        }

        new public static Element Parse(Engine Engine, StringIterator It) {
			if (It.Consume("/*")) {
                var Start = It.Index;

                if (It.Goto("*/")) {
					var Str = It.Substring(Start, It.Index - Start).Trim();
                    It.Consume("*/");
                    return new Comment(Str);
                }
            }
            return null;
        }
    }
}
