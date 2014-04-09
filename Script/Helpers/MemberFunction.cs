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

        private string InfoId = string.Empty;
        public MethodInfo MethodInfo = null;

        private Dictionary<string, MethodInfo> InfoCache = new Dictionary<string, MethodInfo>();

        public MemberFunction(Type Type, string Name) {
            this.Name = Name;
            this.Type = Type;
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
            var Types = GetArgTypes(Args);
            var InfoId = GetInfoString(Types);

            if (MethodInfo == null || this.InfoId != InfoId) {
                MethodInfo Info;

                if (InfoCache.TryGetValue(InfoId, out Info)) {
                    MethodInfo = Info;
                }
                else {
                    MethodInfo = FindMethod(Types);
                    InfoCache[InfoId] = MethodInfo;
                }

                this.InfoId = InfoId;
            }

            return Invoke(This, Args);
        }

        private string GetInfoString(Type[] ArgTypes) {
            return string.Join<Type>(", ", ArgTypes);
        }

        private Type[] GetArgTypes(object[] Args) {
            var Types = new Type[Args.Length];

            for (int Index = 0; Index < Args.Length; Index++) {
                Types[Index] = Args[Index].GetType();
            }

            return Types;
        }

        private MethodInfo FindMethod(Type[] Types) {
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
