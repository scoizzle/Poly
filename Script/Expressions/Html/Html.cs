using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Expressions.Html {
    using Nodes;
    public class Html : Node {
        public static Element Parse(Engine Engine, string Text, ref int Index, int LastPossible) {
            if (!IsParseOk(Engine, Text, ref Index, LastPossible))
                return null;

            var Delta = Index;
            string Name;

            if (Text.Compare('"', Delta) || Text.Compare('\'', Delta)) {
                Expression.ConsumeString(Text, ref Delta);
                Name = Text.Substring(Index + 1, Delta - Index - 2);
            }
            else if (Text.Compare('@', Delta)) {
                var Var = Variable.Parse(Engine, Text, ref Delta, Text.Length);

                if (Var != null)
                    Index = Delta;

                return Var;
            }
            else {
                var Next = Text.FirstPossible(Delta, ':', ',', '(', '{', '}');

                if (Next == '{' || Next == char.MinValue) {
                    return null;
                }

                if (Next == ',' || Next == '}') {
                    var End = Text.IndexOf(Next, Delta);

                    Index = End + 1;

                    return new Attribute(Text.Substring(Delta, End - Delta).Trim(), Types.String.Empty);
                }

                ConsumeValidName(Text, ref Delta);
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
            else if (Text.Compare('(', Delta) && Engine.HtmlTemplates.ContainsKey(Name)) {
                var Close = Delta;

                if (Text.FindMatchingBrackets("(", ")", ref Delta, ref Close)) {
                    var Args = Text.Substring(Delta, Close - Delta).ParseCParams();
                    var Arguments = new Element[Args.Length];

                    for (int i = 0; i < Args.Length; i++){
                        int Ignore = 0;
                        Arguments[i] = Parse(Engine, Args[i], ref Ignore, Args[i].Length);
                    }

                    Index = Close + 1;
                    return new Templater(Engine.HtmlTemplates[Name], Arguments);
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
                    ConsumeValidName(Text, ref Close);

                    Index = Close + 1;
                    return new Attribute(Name, Engine.Parse(Text, Delta));
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
                else {
                    var Next = Text.FirstPossible(Delta, ':', ',', '(', '{', '}');

                    if (Next == ',' || Next == '}') {
                        var End = Text.IndexOf(Next, Delta);

                        Index = End + 1;

                        return new Attribute(Text.Substring(Delta, End - Delta).Trim(), null);
                    }
                }
            }

            return null;
        }

        public static Template FuncParser(Engine Engine, string Text, ref int Index, int LastPossible) {
            if (!IsParseOk(Engine, Text, ref Index, LastPossible))
                return null;

            var Delta = Index;
            if (Text.Compare("html", Delta)) {
                Delta += 4;
                ConsumeWhitespace(Text, ref Delta);

                var End = Delta;

                ConsumeValidName(Text, ref End);
                if (End > Delta) {
                    var Name = Text.Substring(Delta, End - Delta);

                    Delta = End;
                    ConsumeWhitespace(Text, ref Delta);

                    if (Text.Compare('(', Delta)) {
                        if (Text.FindMatchingBrackets("(", ")", ref Delta, ref End)) {
                            var Args = Text.Substring(Delta, End - Delta).ParseCParams();

                            Delta = End + 1;
                            ConsumeWhitespace(Text, ref Delta);

                            if (Text.Compare('{', Delta)) {
                                if (Text.FindMatchingBrackets("{", "}", ref Delta, ref End, false)) {
                                    List<Element> Members = new List<Element>();

                                    while (Delta < End) {
                                        var E = Parse(Engine, Text, ref Delta, End);

                                        if (E != null)
                                            Members.Add(E);
                                    }

                                    var Body = Members.Count > 1 ? 
                                        new Document(Members.ToArray()) :
                                        Members.First();

                                    Index = End + 1;
                                    return Engine.HtmlTemplates[Name] = new Template(Args, Body);
                                }
                            }
                        }
                    }
                }
            }

            return null;
        }

        public static void ConsumeValidName(string Text, ref int Index) {
            var Delta = Index;

            for (; Delta < Text.Length; ) {
                if (IsValidChar(Text[Delta]) || Text[Delta] == '@' || Text[Delta] == '-' || Text[Delta] == '!')
                    Delta++;
                else if (Text[Delta] == '[')
                    ConsumeBlock(Text, ref Delta);
                else break;
            }

            Index = Delta;
        }
    }
}
