using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Script.Node {
    public class CustomTypeInstance : jsObject {
        public CustomType Type;

        public CustomTypeInstance(CustomType Type) {
            this.Type = Type;
        }
    }

    public class CustomType : Expression {
        public string Name;
        public string ShortName;

        public SystemFunction Construct;

        public Library Functions = new Library();
        public CustomType BaseType = null;

        private jsObject<Assign> Properties = new jsObject<Assign>();

        public CustomType(string Name, CustomType Base = null) {
            this.Name = Name;
            this.ShortName = Name.Substring(Name.LastIndexOf('.') + 1);

            this.BaseType = Base;
        }

        public void Register(Engine Engine) {
            Function Init;
            string[] ArgNames;

            if (Functions.TryGetValue(ShortName, out Init)) {
                ArgNames = Init.Arguments;
            }
            else {
                ArgNames = new string[0];
            }
            
            Construct = new SystemFunction(Name, (Args) => {
                return CreateInstance(Args);
            }, ArgNames);

            Engine.Types.Add(Name, this);
        }

        public Function GetFunction(string Name) {
            Function Func;
            if (Functions.TryGetValue(Name, out Func)) {
                return Func;
            }

            if (BaseType != null) {
                return BaseType.GetFunction(Name);
            }

            return null;
        }

        public CustomTypeInstance CreateInstance(jsObject Args) {
            Function Func;
            CustomTypeInstance Instance = new CustomTypeInstance(this);

            Properties.ForEach((K, V) => {
                V.Evaluate(Instance);
            });

            if (Functions.TryGetValue(ShortName, out Func)) {
                Args.Set("this", Instance);

                Func.Evaluate(Args);
            }

            return Instance;
        }

        public static new Expression Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            var Delta = Index;
            if (Text.Compare("class", Delta)) {
                Delta += 5;
                ConsumeWhitespace(Text, ref Delta);

                CustomType Type = null;

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
                            CustomType BaseType;
                            var BaseName = Text.Substring(Open, Delta - Open);
                            ConsumeWhitespace(Text, ref Delta);

                            if (Engine.Types.TryGetValue(BaseName, out BaseType)) {
                                Type = new CustomType(Name, BaseType);
                            }
                        }
                    }
                    else {
                        Type = new CustomType(Name);
                    }

                    if (Type != null && Text.Compare("{", Delta)) {
                        Open = Delta + 1;
                        ConsumeWhitespace(Text, ref Open);
                        ConsumeExpression(Text, ref Delta);

                        while (true) {
                            Node Node = null;

                            if ((Node = Function.Parse(Engine, Text, ref Open, Delta, false)) != null) {
                                Type.Functions.Add(Node as Function);
                            }
                            else {
                                var Obj = Engine.Parse(Text, ref Open, Delta - 1);

                                if (Obj != null && Obj is Assign) {
                                    Type.Properties.Add(Obj as Assign);
                                }
                                else break;
                            }

                            while (Text.Compare(";", Open)) {
                                Open++;
                            }
                            ConsumeWhitespace(Text, ref Open);
                        }

                        ConsumeWhitespace(Text, ref Delta);
                        Index = Delta;

                        Type.Register(Engine);
                        return NoOp;
                    }
                }
            }

            return null;
        }
    }
}
