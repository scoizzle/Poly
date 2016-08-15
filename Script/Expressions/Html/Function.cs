using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Expressions.Html {
    using Data;
	using Script.Nodes;

    public class Function : Nodes.Function {
        public Element Format;

		public Function(string Name, params string[] args)
            : base(Name, args) {
        }

        public override object Evaluate(jsObject Context) {
            return Format?.Evaluate(Context);
        }

        public virtual void Evaluate(StringBuilder Output, jsObject Context) {
            Format?.Evaluate(Output, Context);
        }

		new public static Node Parse(Engine Engine, StringIterator It) {
			return Parse (Engine, It, true);
		}

		new public static Node Parse(Engine Engine, StringIterator It, bool IsEngineWide) {
			string Name;
			string[] Args;

			if (!It.Consume("html"))
				return null;

			It.ConsumeWhitespace();

			var Start = It.Index;
			if (It.Consume(NameFuncs)) {
				Name = It.Substring(Start, It.Index - Start);
				It.ConsumeWhitespace();
			}
			else Name = string.Empty;

			if (It.Consume('(')) {
				Start = It.Index;

				if (It.Goto(')')) {
					Args = It.Substring(Start, It.Index - Start).ParseCParams();

					It.Tick();
					It.ConsumeWhitespace();

                    if (It.IsAt('{')) {
						var Func = new Function(Name, Args);

						Func.Format = Document.Parse (Engine, It);

						if (IsEngineWide) {
							Engine.Functions.Add (Func);
							return NoOperation;
						}
						else return Func;
					}
				}
			}

			return null;
		}

        new public static Node ParseLambda(Engine Engine, StringIterator It) {
            if (It.IsAt("html")) {
                var Begin = It.Index;
                It.Consume("html");
                It.ConsumeWhitespace();

                if (It.Consume('(')) {
                    var Start = It.Index;

                    if (It.Goto(')')) {
                        var Args = It.Substring(Start, It.Index - Start).ParseCParams();

                        It.Tick();
                        It.ConsumeWhitespace();

                        if (It.Consume("=>")) {
                            It.ConsumeWhitespace();

                            if (It.IsAt('{')) {
                                var Func = new Function(string.Empty, Args);

                                Func.Format = Document.Parse(Engine, It);

                                return Func;
                            }
                        }
                    }
                }
                It.Index = Begin;
            }
            return null;

        }
    }
}
