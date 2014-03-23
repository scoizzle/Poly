using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;

namespace Poly.Script.Helper {
    using Data;
    using Node;

    public class MemberFunction : Function {
        public Type Type = null;
        public MethodInfo MethodInfo = null;
        private int ArgCount = 0;

        public MemberFunction(Type Type, string Name) {
            this.Name = Name;
            this.Type = Type;
            this.ObjectName = Type.Name;
        }

        public override object Evaluate(jsObject Context) {
            if (!Context.ContainsKey("this")) {
                return null;
            }

            var This = Context.Get<object>("this");

            if (This == null) {
                return null;
            }
            else {
                Context.Remove("this");
            }

            var Args = GetArguments(Context);

            if (MethodInfo == null || Args.Length != ArgCount) {
                MethodInfo = FindMethod(Args);
                ArgCount = Args.Length;
            }

            return Invoke(This, Args);
        }

        private MethodInfo FindMethod(object[] Args) {
            var Types = new Type[Args.Length];

            for (int Index = 0; Index < Args.Length; Index++) {
                Types[Index] = Args[Index].GetType();
            }

            try {
                return Type.GetMethod(Name, Types);
            }
            catch { return null; }
        }

        public object Invoke(object This, object[] Args) {
            if (MethodInfo == null)
                return null;

            try {
                return MethodInfo.Invoke(This, Args);
            }
            catch { return null; }
        }

        public static object[] GetArguments(jsObject Context) {
            var Args = new object[Context.Count];
            var Index = 0;

            Context.ForEach((Key, Value) => {
                Node Node = Value as Node;

                if (Node != null) {
                    Value = Node.GetSystemHandler();
                }

                Args[Index++] = Value;
            });

            return Args;
        }
    }
}
