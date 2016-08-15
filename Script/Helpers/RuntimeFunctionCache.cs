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
                    Info = Type.GetMethod(Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static, null, ArgTypes, null);
                    
                    Handler = GetDelegate(
                        Info ?? Type.GetMethod(Name),
                        ArgTypes
                    );
                }
                catch { }
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

        public static Func<object, object[], object> GetDelegate(MethodInfo Info, Type[] Types) {
            var Method = new DynamicMethod(string.Empty, typeof(object), new Type[] { typeof(object), typeof(object) }, Info.DeclaringType.Module);
            var Params = Info.GetParameters();

            if (Types.Length != Params.Length)
                return null;

            var IL = Method.GetILGenerator();
            var Locals = new LocalBuilder[Params.Length];

            for (int i = 0; i < Params.Length; i++) {
                Locals[i] = IL.DeclareLocal(Types[i]);
            }

            for (int i = 0; i < Params.Length; i++) {
                IL.Emit(OpCodes.Ldarg_1);

                EmitFastInt(IL, i);
                IL.Emit(OpCodes.Ldelem_Ref);

                EmitCastToReference(IL, Types[i]);
                IL.Emit(OpCodes.Stloc, Locals[i]);
            }

            if (!Info.IsStatic)
                IL.Emit(OpCodes.Ldarg_0);

            for (int i = 0; i < Params.Length; i++) {
                if (Params[i].ParameterType.IsByRef)
                    IL.Emit(OpCodes.Ldloca_S, Locals[i]);
                else
                    IL.Emit(OpCodes.Ldloc, Locals[i]);
            }

            if (Info.IsStatic)
                IL.EmitCall(OpCodes.Call, Info, null);
            else
                IL.EmitCall(OpCodes.Callvirt, Info, null);

            if (Info.ReturnType == typeof(void))
                IL.Emit(OpCodes.Ldnull);
            else
                EmitBoxIfNeeded(IL, Info.ReturnType);

            for (int i = 0; i < Params.Length; i++) {
                if (Params[i].ParameterType.IsByRef) {
                    IL.Emit(OpCodes.Ldarg_1);

                    EmitFastInt(IL, i);
                    IL.Emit(OpCodes.Ldloc, Locals[i]);

                    if (Locals[i].LocalType.IsValueType)
                        IL.Emit(OpCodes.Box, Locals[i].LocalType);

                    IL.Emit(OpCodes.Stelem_Ref);
                }
            }

            IL.Emit(OpCodes.Ret);
            return (Func<object, object[], object>)Method.CreateDelegate(typeof(Func<object, object[], object>));
        }


        static void EmitCastToReference(ILGenerator IL, Type Type) {
            if (Type.IsValueType)
                IL.Emit(OpCodes.Unbox_Any, Type);
            else
                IL.Emit(OpCodes.Castclass, Type);
        }

        static void EmitBoxIfNeeded(ILGenerator IL, Type Type) {
            if (Type.IsValueType)
                IL.Emit(OpCodes.Box, Type);
        }

        static void EmitFastInt(ILGenerator IL, int value) {
            switch (value) {
                case -1:
                    IL.Emit(OpCodes.Ldc_I4_M1);
                    return;
                case 0:
                    IL.Emit(OpCodes.Ldc_I4_0);
                    return;
                case 1:
                    IL.Emit(OpCodes.Ldc_I4_1);
                    return;
                case 2:
                    IL.Emit(OpCodes.Ldc_I4_2);
                    return;
                case 3:
                    IL.Emit(OpCodes.Ldc_I4_3);
                    return;
                case 4:
                    IL.Emit(OpCodes.Ldc_I4_4);
                    return;
                case 5:
                    IL.Emit(OpCodes.Ldc_I4_5);
                    return;
                case 6:
                    IL.Emit(OpCodes.Ldc_I4_6);
                    return;
                case 7:
                    IL.Emit(OpCodes.Ldc_I4_7);
                    return;
                case 8:
                    IL.Emit(OpCodes.Ldc_I4_8);
                    return;
            }

            if (value > -129 && value < 128)
                IL.Emit(OpCodes.Ldc_I4_S, (SByte)value);
            else
                IL.Emit(OpCodes.Ldc_I4, value);
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
