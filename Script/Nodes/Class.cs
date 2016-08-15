using System;
using System.Collections.Generic;
using System.Diagnostics;
using Poly.Data;

namespace Poly.Script.Nodes {
    [DebuggerDisplay("{Name}")]
    public class Class : Node {
		public string Name, LastName;
		public jsObject<Function> Functions, StaticFunctions;
        public Function Constructor,
                        Instaciator;

        public Class Base;

		public Class(string Name, Class Base = null) {
			this.Name = Name;

            if (this.Name.Contains('.'))
                this.LastName = this.Name.Substring(this.Name.LastIndexOf('.') + 1);
            else
                this.LastName = Name;

			this.Functions = new jsObject<Function> ();
            this.StaticFunctions = new jsObject<Function>();
            this.Base = Base;
		}

		public object CreateInstance(jsObject Context) {
			var Inst = new Types.ClassInstance (this);
			Construct (Inst, Context);
			return Inst;
		}

		private void Construct(Types.ClassInstance Instance, jsObject Context) {
			Base?.Construct (Instance, Context);
			base.Evaluate (Instance);

			if (Constructor != null) {
				Context.Set("this", Instance);
				Constructor.Evaluate(Context);
				Context.Remove("this");
			}
		}

        public void Register(Engine Engine) {
            Engine.Types[Name] = this;
        }

		public void InitInstanciator() {
			if (Functions.TryGet<Function>(LastName, out Constructor)) {
				this.Instaciator = new Function(LastName, CreateInstance, Constructor.Arguments);
			}
			else {
				this.Instaciator = new Function(LastName, CreateInstance);
			}
		}

        public Function GetFunction(string Name) {
			Function Func;

			if (Functions.TryGet<Function> (Name, out Func))
				return Func;

            return Base?.GetFunction(Name);
        }

        public Function GetStaticFunction(string Name) {
            Function Func;

            if (StaticFunctions.TryGet<Function>(Name, out Func))
                return Func;

            if (Base != null)
                return Base.GetStaticFunction(Name);

            return null;

        }

		public static Node Parse(Engine Engine, StringIterator It) {
			Class Node;
			if (It.Consume ("class")) {
				It.ConsumeWhitespace ();

				var Start = It.Index;
				if (It.Consume (NameFuncs)) {
					var Name = It.Substring (Start, It.Index - Start);
					It.ConsumeWhitespace ();

					if (It.Consume (':')) {
						It.ConsumeWhitespace ();
						Start = It.Index;

						if (It.Consume (NameFuncs)) {
							var BaseName = It.Substring (Start, It.Index - Start);
							var Base = Engine.Types [BaseName];
								
							Node = new Class (Name, Base);
						} else
							return null;
					} else {
						Node = new Class (Name);
					}
					It.ConsumeWhitespace ();

					if (It.Consume ('{')) {
						It.ConsumeWhitespace ();

						Start = It.Index;
						if (It.Goto ('{', '}')) {
							var Sub = It.Clone (Start, It.Index);
							var List = new List<Node> ();
							Node.Register (Engine);

							while (!Sub.IsDone ()) {
								bool IsStatic = Sub.Consume ("static");
								Function Func;

								Sub.ConsumeWhitespace ();
								if ((Func = Expressions.Html.Function.Parse (Engine, Sub, false) as Function) != null ||
								    (Func = Nodes.Function.Parse (Engine, Sub, false) as Function) != null) {

									if (IsStatic) {
										Node.StaticFunctions.Add (Func.Name, Func);
									} else {
										Node.Functions.Add (Func.Name, Func);
									}
								} else {
									var Exp = Engine.ParseExpression (Sub);

									if (Exp == null)
										break;

									List.Add (Exp);
								}

								Sub.Consume (WhitespaceFuncs);
							}

							Node.Elements = List.ToArray ();

							It.Consume ('}');

							Node.InitInstanciator ();
							return Expression.NoOperation;
						}
					}
				}
			}
			return null;
		}
    }
}
