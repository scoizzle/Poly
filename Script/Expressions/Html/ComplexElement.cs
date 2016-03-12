using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Expressions.Html {
    using Data;
    using Nodes;

    public class ComplexElement : Element {
        string Type;
        Attribute[] Attributes;
        Element[] Members;

        public ComplexElement(string Type) {
            this.Type = Type;

            Attributes = null;
            Members = null;
        }

        public override void Evaluate(StringBuilder Output, jsObject Context) {
            Output.Append("<")
                  .Append(Type);
            
            for (int i = 0; i < Attributes.Length; i++) {
                var Item = Attributes[i];
                Output.Append(" ")
                      .Append(Item.Key);

                    if (Item.Value != null)
                        Output.Append("=\"")
                              .Append(Item.Value.Evaluate(Context))
                              .Append("\"");
            }
            Output.Append(">");
            
            for (int i = 0; i < Members.Length; i++) {
                if (Members[i] != null)
                    Members[i].Evaluate(Output, Context);
            }


            if (!IsSingletonTag(Type)) {
                Output.Append("</")
                      .Append(Type)
                      .Append(">");
            }
        }

        public static bool IsSingletonTag(string Tag) {
            switch (Tag) {
                case "area":
                case "base":
                case "br":
                case "col":
                case "command":
                case "embed":
                case "hr":
                case "img":
                case "input":
                case "link":
                case "meta":
                case "param":
                case "source":
                case "!DOCTYPE":
                    return true;
            }

            return false;
        }
        

        new public static Node Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            var Delta = Index;
            ConsumeValidName(Text, ref Delta);

            if (Delta != Index) {
                var Name = Text.Substring(Index, Delta - Index).TrimEnd();
                var Class = String.Empty;
                ConsumeWhitespace(Text, ref Delta);

                if (Text.Compare('.', Delta)) {
                    Delta++;
                    var Next = Text.FirstPossibleIndex(Delta, ':', '{');

                    Class = Text.Substring(Delta, Next - Delta);
                    Delta = Next;
                    ConsumeWhitespace(Text, ref Delta);
                }

                if (Text.Compare(':', Delta)) {
                    Delta++;
                    ConsumeWhitespace(Text, ref Delta);
                }

                if (Text.Compare('{', Delta)) {
                    if (Text.FindMatchingBrackets('{', '}', ref Delta, ref LastIndex, false)) {
                        List<Attribute> Attributes = new List<Attribute>();
                        List<Element> Elements = new List<Element>();

                        if (Class != string.Empty) {
                            Attributes.Add(new Attribute() {
                                Key = "class",
                                Value = new Nodes.StaticValue(Class.Trim())
                            });
                        }

                        while (IsParseOk(Engine, Text, ref Delta, LastIndex)) {
                            var E = Html.Parse(Engine, Text, ref Delta, LastIndex) as Element;

                            if (E is Attribute) {
                                Attributes.Add(E as Attribute);
                            }
                            else if (E != null) {
                                Elements.Add(E);
                            }
                        }

                        Index = LastIndex + 1;
                        return new ComplexElement(Name) {
                            Attributes = Attributes.ToArray(),
                            Members = Elements.ToArray()
                        };
                    }
                }
                else if (IsSingletonTag(Name)) {
                    Index = Delta;

                    return new ComplexElement(Name);
                }
            }
            return null;
        }

        public new static bool IsValidChar(char c) {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '!';
        }

        public new static void ConsumeValidName(string Text, ref int Index) {
            var Delta = Index;

            while (IsValidChar(Text[Delta]) || char.IsWhiteSpace(Text[Delta]))
                Delta++;

            Index = Delta;
        }
    }
}
