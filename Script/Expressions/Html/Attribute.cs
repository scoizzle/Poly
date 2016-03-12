using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Expressions.Html {
    using Data;
    using Nodes;

    public class Attribute : Element {
        public string Key;
        public Node Value;

        new public static Node Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            var Delta = Index;
            ConsumeValidName(Text, ref Delta);

            if (Delta != Index) {
                var Name = Text.Substring(Index, Delta - Index);
                ConsumeWhitespace(Text, ref Delta);

                if (Text.Compare(':', Delta)) {
                    Delta++;
                    ConsumeWhitespace(Text, ref Delta);

                    if (Text.Compare('{', Delta))
                        return ComplexElement.Parse(Engine, Text, ref Index, LastIndex);

                    Index = Delta;

                    return new Attribute() {
                        Key = Name,
                        Value = Html.Parse(Engine, Text, ref Index, LastIndex)
                    };
                }
            }

            return null;
        }

        public override string ToString() {
            return string.Format("{0}=\"{1}\"", Key, Value);
        }
    }
}