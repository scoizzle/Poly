using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

using Poly.Data;

namespace Poly.Script.Nodes {
    public class Function : Expression {
        public string Name;
        public string[] Arguments;

        Event.Handler Method;

        public Function() {
            Method = base.Evaluate;
        }

        public Function(Event.Handler Handler) {
            Method = Handler;
            var Info = Handler.GetMethodInfo();
            Name = string.Concat(Info.DeclaringType.Name, '.', Info.Name);
        }

        public Function(string Name, Event.Handler Handler) {
            this.Name = Name;
            Method = Handler;
        }

        public Function(string Name, params string[] ArgumentNames) {
            this.Name = Name;
            Arguments = ArgumentNames;

            Method = base.Evaluate;
        }

        public Function(string Name, Event.Handler Handler, params string[] ArgumentNames) {
            this.Name = Name;
            Arguments = ArgumentNames.Where(N => N != "this").ToArray();
            Method = Handler;
        }

        public override object Evaluate(jsObject Context) {
            var Result = Method(Context);

            if (Result is Expressions.Return) {
                return (Result as Expressions.Return).Evaluate(Context);
            }

            return Result;
        }

        public override string ToString() {
            return string.Format("{0}({1}) {{{2}}}", Name, string.Join(", ", Arguments), base.ToString());
        }

        public static Function Create(string Name, Func<object> Func) {
            return new Function(Name, (Args) => { return Func(); });
        }

        public static Function Create<T1>(string Name, Func<T1, object> Func) {
            return new Function(Name, Event.Wrapper(Func), Event.GetArgumentNames(Func.GetMethodInfo()));
        }

        public static Function Create<T1, T2>(string Name, Func<T1, T2, object> Func) {
            return new Function(Name, Event.Wrapper(Func), Event.GetArgumentNames(Func.GetMethodInfo()));
        }

        public static Function Create<T1, T2, T3>(string Name, Func<T1, T2, T3, object> Func) {
            return new Function(Name, Event.Wrapper(Func), Event.GetArgumentNames(Func.GetMethodInfo()));
        }

        public static Function Create<T1, T2, T3, T4>(string Name, Func<T1, T2, T3, T4, object> Func) {
            return new Function(Name, Event.Wrapper(Func), Event.GetArgumentNames(Func.GetMethodInfo()));
        }

        public static Function Create<T1, T2, T3, T4, T5>(string Name, Func<T1, T2, T3, T4, T5, object> Func) {
            return new Function(Name, Event.Wrapper(Func), Event.GetArgumentNames(Func.GetMethodInfo()));
        }

        public static Function Create<T1, T2, T3, T4, T5, T6>(string Name, Func<T1, T2, T3, T4, T5, T6, object> Func) {
            return new Function(Name, Event.Wrapper(Func), Event.GetArgumentNames(Func.GetMethodInfo()));
        }

        public static Function Create<T1, T2, T3, T4, T5, T6, T7>(string Name, Func<T1, T2, T3, T4, T5, T6, T7, object> Func) {
            return new Function(Name, Event.Wrapper(Func), Event.GetArgumentNames(Func.GetMethodInfo()));
        }

		new public static Node Parse(Engine Engine, StringIterator It) {
			return Parse (Engine, It, true);
		}

		public static Node Parse(Engine Engine, StringIterator It, bool IsEngineWide) {
            int Start;
            string Name;
            string[] Args;

            if (It.Consume("func")) {
                It.Consume("tion");
                It.ConsumeWhitespace();

                Start = It.Index;
                if (It.Consume(NameFuncs)) {
                    Name = It.Substring(Start, It.Index - Start);
                    It.ConsumeWhitespace();
                }
                else Name = string.Empty;
            }
            else Name = string.Empty;

            if (It.IsAt('(')) {
                It.Tick();
                Start = It.Index;

                if (It.Goto(')')) {
                    Args = It.Substring(Start, It.Index - Start).ParseCParams();

                    It.Tick();
                    It.ConsumeWhitespace();

                    if (It.Consume("=>"))
                        It.ConsumeWhitespace();

                    if (It.IsAt('{')) {
                        var Func = new Function(Name, Args);
                        
                        Parse(Engine, It, Func);

						if (IsEngineWide && !string.IsNullOrEmpty(Name)) {
							Engine.Functions.Add (Func);
							return NoOperation;
						}
						else return Func;
                    }
                }
            }

            return null;
        }

        public static Node ParseLambda(Engine Engine, StringIterator It) {
            if (It.IsAt('(')) {
                var Begin = It.Index;
                It.Tick();

                var Start = It.Index;
                if (It.Goto(')')) {
                    var Args = It.Substring(Start, It.Index - Start).ParseCParams();

                    It.Tick();
                    It.ConsumeWhitespace();

                    if (It.Consume("=>")) {
                        It.ConsumeWhitespace();

                        if (It.IsAt('{')) {
                            var Func = new Function(string.Empty, Args);

                            Parse(Engine, It, Func);

                            return Func;
                        }
                    }
                }
                It.Index = Begin;
            }
            return null;
        }
    }
}
