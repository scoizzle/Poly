using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using Poly.Script.Node;

namespace Poly.Script {
    public class SystemFunction : Function {
        public Event.Handler Handler = null;

        public SystemFunction(string Name, Event.Handler Handler, params string[] ArgNames) {
            this.Name = Name;
            this.Handler = Handler;
            this.Arguments = ArgNames;
        }

        public override object Evaluate(Data.jsObject Context) {
            if (Handler == null)
                return null;

            return Handler(Context);
        }

        public static explicit operator SystemFunction(Event.Handler Handler) {
            return new SystemFunction("", Handler);
        }

        public class Raw : Function {
            public Type Type = null;
            private MethodInfo Info = null;

            public override object Evaluate(Data.jsObject Context) {
                if (Type == null || string.IsNullOrEmpty(Name))
                    return null;

                if (Info != null)
                    return Invoke(Info, Context);

                var i = 0;
                var Count = Context.ContainsKey("this") ?
                    Context.Count - 1 :
                    Context.Count;

                try {
                    var Types = new Type[Count];

                    if (Count > 0) {
                        foreach (var Pair in Context) {
                            if (Pair.Key == "this")
                                continue;

                            if (Pair.Value is Function) {
                                Types[i++] = typeof(Poly.Event.Handler);
                            }
                            else {
                                Types[i++] = Pair.Value.GetType();
                            }
                        }
                    }

                    Info = Type.GetMethod(Name, Types);

                    if (Info == null) {
                        Info = Type.GetMethod(Name);
                    }

                    if (Info != null) {
                        return Invoke(Info, Context);
                    }
                }
                catch { }

                return null;
            }

            public static object Invoke(MethodInfo Info, Data.jsObject Context) {
                object This = null;
                object[] Args = null;
                int Count = Context.Count, Index = 0;

                if (!Info.IsStatic && Context.ContainsKey("this")) {
                    This = Context["this"];
                    Count--;
                }

                Args = new object[Count];

                foreach (var Pair in Context) {
                    if (Pair.Key != "this") {
                        if (Pair.Value is Function) {
                            Args[Index++] = (Pair.Value as Function).GetSystemHandler();
                        }
                        else {
                            Args[Index++] = Pair.Value;
                        }
                    }
                }

                try {
                    return Info.Invoke(This, Args);
                }
                catch {
                    return null;
                }
            }

            public static bool Exists(Type T, string Name) {
                try {
                    foreach (var M in T.GetMethods())
                        if (M.Name == Name)
                            return true;
                }
                catch { }
                return false;
            }
        }

        public class Initializer : Function {
            public Type Type = null;

            public override object Evaluate(Data.jsObject Context) {
                if (Type == null)
                    return null;

                return Invoke(Type, Context);
            }

            public static object Invoke(Type Type, Data.jsObject Context) {
                object[] Args = null;
                int Count = Context.Count, Index = 0;
                
                Args = new object[Count];

                foreach (var Pair in Context) {
                    Args[Index++] = Pair.Value;
                }

                return Activator.CreateInstance(Type, Args);
            }

            public static Type SearchForType(string Name) {
                foreach (var Asm in AppDomain.CurrentDomain.GetAssemblies()) {
                    var Type = Asm.GetType(Name);

                    if (Type != null)
                        return Type;
                }

                return null;
            }

            public static bool Exists(string Name) {
                return (SearchForType(Name) != null);
            }
        }
    }
}
