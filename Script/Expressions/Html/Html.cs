using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Expressions.Html {
    using Nodes;
    public class Html : Node {
        public static Element Parse(Engine Engine, string Text, ref int Index, int LastPossible) {
            Expression.ConsumeWhitespace(Text, ref Index);
            var Delta = Index;

            string Name;
            if (Expression.ConsumeString(Text, ref Delta)) {
                Name = Text.Substring(Index + 1, Delta - Index - 2);
            }
            else if (Text.Compare('@', Delta)) {
                var Var = Variable.Parse(Engine, Text, ref Delta, Text.Length);

                if (Var != null)
                    Index = Delta;

                return Var;
            }
            else {
                var Next = Text.FirstPossible(Delta, ':', ',', '{');

                if (Next != ':' && Next != ',')
                    return null;

                Expression.ConsumeValidName(Text, ref Delta);
                Name = Text.Substring(Index, Delta - Index);  
            }

            if (string.IsNullOrEmpty(Name))
                return null;

            Text.ConsumeWhitespace(ref Delta);

            if (Delta >= LastPossible || Text.Compare(',', Delta)) {
                Index = Delta + 1;

                if (Name.Compare('@', 0)) {
                    Delta = 0;
                    return Variable.Parse(Engine, Name, ref Delta, Name.Length);
                }
                else if (ComplexElement.SingletonTags.Contains(Name)) {
                    return new ComplexElement() { Type = Name };
                }
                else {
                    return new StringElement(Name);
                }
            }
            else if (Text.Compare(':', Delta)) {
                Delta++;
                Expression.ConsumeWhitespace(Text, ref Delta);

                var Close = Delta;
                if (Expression.ConsumeString(Text, ref Close)) {
                    Index = Close;
                    return new Attribute(Name, new Types.String(Text.Substring(Delta + 1, Close - Delta - 2)));
                }
                else if (Text.Compare('@', Delta)) {
                    Delta++;
                    Expression.ConsumeValidName(Text, ref Close);

                    Index = Close + 1;
                    return new Attribute(Name, Nodes.Variable.Parse(Engine, Text, ref Delta, Close));
                }
                else if (Text.Compare('{', Delta)) {
                    Expression.ConsumeExpression(Text, ref Close);
                    var Elem = ComplexElement.Parse(Engine, Text, ref Delta, Close);

                    if (Elem != null) {
                        Elem.Type = Name;
                        Index = Close;
                    }

                    return Elem;
                }
            }

            return null;
        }
    }
}
