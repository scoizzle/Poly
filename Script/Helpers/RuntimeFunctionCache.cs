using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Reflection.Emit;

namespace Poly.Script.Helpers {
    using Data;

    public class RuntimeFunctionCache {
        static Type[] EmptyTypeArray;
        static Dictionary<Type, KeyValueCollection<Dictionary<TypeList, Func<object, object[], object>>>> Items;

        static RuntimeFunctionCache() {
            EmptyTypeArray = new Type[0];
            Items = new Dictionary<Type, KeyValueCollection<Dictionary<TypeList, Func<object, object[], object>>>>();
        }

        public static Func<object, object[], object> GetFunction(Type Type, string Name, Type[] ArgTypes) {
            if (ArgTypes == null)
                ArgTypes = EmptyTypeArray;

            var ti = Type.GetTypeInfo();
            var Types = new TypeList() { List = ArgTypes };
            var Handler = GetCacheItem(Type, Name, Types);

            if (Handler != null)
                return Handler;

            if (Type.Name == Name || Type.FullName == Name) {
                Handler = (object This, object[] Args) => {
                    return Activator.CreateInstance(Type, Args);
                };
            }
            else {
                MethodInfo Info = null;

                try {
                    Info = ti.GetMethod(Name, ArgTypes) ?? ti.GetMethod(Name);
                }
                catch { }

                if (Info == null) return null;

                Handler = Info.Invoke;
            }

            if (Handler != null)
                SetCacheItem(Type, Name, Types, Handler);

            return Handler;
        }

        private static Func<object, object[], object> GetCacheItem(Type Type, string Name, TypeList ArgTypes) {
            KeyValueCollection<Dictionary<TypeList, Func<object, object[], object>>> TypeCache;

            if (Items.TryGetValue(Type, out TypeCache)) {
                Dictionary<TypeList, Func<object, object[], object>> Methods;

                if (TypeCache.TryGetValue(Name, out Methods)) {
                    Func<object, object[], object> Handler;

                    if (Methods.TryGetValue(ArgTypes, out Handler))
                        return Handler;
                }
            }

            return null;
        }

        private static void SetCacheItem(Type Type, string Name, TypeList ArgTypes, Func<object, object[], object> Func) {
            KeyValueCollection<Dictionary<TypeList, Func<object, object[], object>>> TypeCache;
            Dictionary<TypeList, Func<object, object[], object>> Methods;

            if (!Items.TryGetValue(Type, out TypeCache)) {
                Items[Type] = TypeCache = new KeyValueCollection<Dictionary<TypeList, Func<object, object[], object>>>();
            }

            if (!TypeCache.TryGetValue(Name, out Methods)) {
                TypeCache[Name] = Methods = new Dictionary<TypeList, Func<object, object[], object>>();
            }

            Methods[ArgTypes] = Func;
        }
        
        public class TypeList {
            public Type[] List;

			public override int GetHashCode() {
				int Sum = 13;
				for (var i = 0; i < List.Length; i++) unchecked {
					Sum += List [i].GetHashCode ();
				}

				return Sum;
            }

            public override bool Equals(object obj) {
                var L = obj as TypeList;

                if (L == null)
                    return false;

                if (List.Length == L.List.Length) {
                    for (var i = 0; i < List.Length; i++) {
                        if (List[i] != L.List[i])
                            return false;
                    }

                    return true;
                }

                return false;
            }
        }
    }
}
