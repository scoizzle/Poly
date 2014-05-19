using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;

namespace Poly.Script.Helper {
    using Data;
    using Node;

    public class SystemFunctions {
        private static jsObject<SystemFunction> Cache = new jsObject<SystemFunction>();

        public static Type SearchForType(string Name) {
            foreach (var Asm in AppDomain.CurrentDomain.GetAssemblies()) {
                var T = Asm.GetType(Name, false);

                if (T != null)
                    return T;
            }

            return null;
        }

        public static SystemFunction Get(Type Type, string Name, params Type[] Types) {
            var TypeString = GetTypeString(Types);
            var Func = Cache[Type.FullName, Name, TypeString] as SystemFunction;

            if (Func != null)
                return Func;

            var Info = GetInfo(Type, Name, Types);

            if (Info == null)
                return null;

            Func = new SystemFunction(Name, (Args) => {
                var This = Args.Get<object>("this");

                if (This != null) 
                    Args.Remove("this");

                return Info.Invoke(This, GetArguments(Args));
            });

            Cache[Type.FullName, Name, TypeString] = Func;

            return Func;
        }

        private static string GetTypeString(Type[] Types) {
            return string.Join(",", Types.Select(t => t.FullName));
        }

        private static MethodInfo GetInfo(Type Type, string Name, Type[] Types) {
            try {
                return Type.GetMethod(Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static, null, Types, null);
            }
            catch {
                return null;
            }
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

    public class MemberFunction : Function {
        private static jsObject<MemberFunction> Cache = new jsObject<MemberFunction>();

        public static MemberFunction Get(Type Type, string Name) {
            var Func = Cache[Type.FullName, Name];

            if (Func != null)
                return Func;

            return new MemberFunction(Type, Name);
        }

        public Type Type = null;

        public MemberFunction(Type Type, string Name) {
            this.Name = Name;
            this.Type = Type;

            Cache[Type.FullName, Name] = this;
        }

        public override object Evaluate(jsObject Context) {
            var Types = GetArgTypes(Context);
            var Func = SystemFunctions.Get(Type, Name, Types);

            if (Func != null)
                return Func.Evaluate(Context);

            return null;
        }

        private Type[] GetArgTypes(jsObject Args) {
            List<Type> TypeList = new List<System.Type>();
            
            foreach (var Pair in Args) {
                if (Pair.Key != "this")
                    TypeList.Add(Pair.Value.GetType());
            }

            return TypeList.ToArray();
        }
    }
}
