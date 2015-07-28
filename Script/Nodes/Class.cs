using System;
using System.Collections.Generic;
using Poly.Data;

namespace Poly.Script.Nodes {
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

			base.Evaluate (Inst);

            if (Constructor != null) {
                Context.Set("this", Inst);
                Constructor.Evaluate(Context);
                Context.Remove("this");
            }

			return Inst;
		}

        public void Register(Engine Engine) {
            if (Functions.TryGet<Function>(LastName, out Constructor)) {
                this.Instaciator = new Function(LastName, CreateInstance, Constructor.Arguments);
            }
            else {
                this.Instaciator = new Function(LastName, CreateInstance);
            }

            Engine.Types[Name] = this;
        }

        public Function GetFunction(string Name) {
			Function Func;

			if (Functions.TryGet<Function> (Name, out Func))
				return Func;

            if (Base != null)
                return Base.GetFunction(Name);

            return null;
        }

        public static Node Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            var Delta = Index;
            if (Text.Compare("class", Delta)) {
                Delta += 5;
                ConsumeWhitespace(Text, ref Delta);

                Class Type = null;

                var Open = Delta;
                ConsumeValidName(Text, ref Delta);

                if (Delta > Open) {
                    var Name = Text.Substring(Open, Delta - Open);
                    ConsumeWhitespace(Text, ref Delta);

                    if (Text.Compare(":", Delta)) {
                        Delta++;
                        ConsumeWhitespace(Text, ref Delta);

                        Open = Delta;
                        ConsumeValidName(Text, ref Delta);

                        if (Delta > Open) {
                            Class Base;
                            var BaseName = Text.Substring(Open, Delta - Open);
                            ConsumeWhitespace(Text, ref Delta);

                            if ((Base = Engine.Types[BaseName] as Class) != null) {
                                Type = new Class(Name, Base);
                            }
                        }
                    }
                    else {
                        Type = new Class(Name);
                    }

                    if (Type != null && Text.Compare("{", Delta)) {
                        Open = Delta + 1;
                        ConsumeWhitespace(Text, ref Open);
                        ConsumeExpression(Text, ref Delta);


                        var List = new List<Node>();
                        while (true) {
                            bool IsStatic = false;
                            Node Node = null;

                            if (Text.Compare("static", Open)) {
                                IsStatic = true;
                                Open += 6;
                            }

                            if ((Node = Function.Parse(Engine, Text, ref Open, Delta, false)) != null) {
                                var Func = Node as Function;

                                if (IsStatic) {
                                    Type.StaticFunctions.Add(Func.Name, Func);
                                }
                                else {
                                    Type.Functions.Add(Func.Name, Func);
                                }
                            }
                            else {
                                var Obj = Engine.Parse(Text, ref Open, Delta - 1);

                                if (Obj is Expressions.Assign) {
                                    List.Add(Obj);
                                }
                                else break;
                            }

                            ConsumeWhitespace(Text, ref Open);
                        }

                        Type.Elements = List.ToArray();

                        ConsumeWhitespace(Text, ref Delta);
                        Index = Delta;

                        Type.Register(Engine);
                        return Expression.NoOperation;
                    }
                }
            }

            return null;
        }
    }
}
