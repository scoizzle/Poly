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

        public ComplexElement(string Type) {
            this.Type = Type;

			Attributes = new Attribute[0];
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
            
            for (int i = 0; i < Elements.Length; i++) {
                (Elements[i] as Element)?.Evaluate(Output, Context);
            }
            
            if (!IsSingletonTag(Type)) {
                Output.Append("</")
                      .Append(Type)
                      .Append(">");
            }
        }

        public override void ToEvaluationArray(StringBuilder output, List<Action<StringBuilder, jsObject>> list) {
            output.Append('<')
                  .Append(Type);
            
            for (int i = 0; i < Attributes.Length; i++) {
                var Item = Attributes[i];
                output.Append(" ")
                      .Append(Item.Key);

                if (Item.Value != null) {
                    output.Append("=\"");

                    if (Item.Value is Element) {
                        (Item.Value as Element).ToEvaluationArray(output, list);
                    }
                    else { 
                        list.Add(StaticAppender(output));
                        list.Add(NodeAppender(Item.Value.Evaluate));
                        output.Clear();
                    }

                    output.Append('"');
                }
            }
            output.Append('>');
            
            for (int i = 0; i < Elements.Length; i++) {
                var Elem = Elements[i];

                if (Elem is Element)
                    (Elem as Element).ToEvaluationArray(output, list);
                else {
                    list.Add(StaticAppender(output));
                    list.Add(NodeAppender(Elem.Evaluate));
                    output.Clear();
                }
            }

            if (!IsSingletonTag(Type)) {
                output.Append("</")
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
        
        // type .class #id { ... }
        new public static Element Parse(Engine Engine, StringIterator It) {
            var Start = It.Index;
            if (It.Consume(ComplexNameFuncs)) {
				if (It.IsAt ('{')) {
					string Class, Id;
					var Node = ParseIdentification (It.Clone (Start, It.Index), out Class, out Id);
                    It.Tick();
					Start = It.Index;

					if (It.Goto ('{', '}')) {
						var Sub = It.Clone (Start, It.Index);
						var Attributes = new List<Attribute> ();
						var Elements = new List<Element> ();

						if (!string.IsNullOrEmpty (Class)) {
							Attributes.Add (new Attribute ("class", new Nodes.StaticValue (Class)));
						}
						if (!string.IsNullOrEmpty (Id)) {
							Attributes.Add (new Attribute ("id", new Nodes.StaticValue (Id)));
						}

						while (!Sub.IsDone ()) {
							var Element = Html.ParseElement (Engine, Sub);

							if (Element == null)
								break;

							if (Element is Attribute) {
								Attributes.Add (Element as Attribute);
							} else {
								Elements.Add (Element);
							}

							Sub.Consume (WhitespaceFuncs);
						}

						Node.Attributes = Attributes.ToArray ();
						Node.Elements = Elements.ToArray ();

						It.Consume ('}');
						return Node;
					}
				} else It.Index = Start;
            }
            return null;
        }
        
        private static ComplexElement ParseIdentification(StringIterator It, out string Class, out string Id) {
            var Start = It.Index;
            It.Consume(ElementNameFuncs);
            var Node = new ComplexElement(It.Substring(Start, It.Index - Start));

            It.ConsumeWhitespace();
            if (It.Consume('.')) {
                Start = It.Index;
                It.ConsumeUntil(c => c == '#');

				if (It.Index == Start)
					It.Index = It.Length;
				
                Class = It.Substring(Start, It.Index - Start).Trim();
                It.ConsumeWhitespace();
            }
            else Class = null;

            if (It.Consume('#')) {
                Id = It.Substring(It.Index, It.Length - It.Index).Trim();
                It.ConsumeWhitespace();
            }
            else Id = null;

            return Node;
        }
    }
}
