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

        static Type[] EmptyTypeArray;
        static Dictionary<int, Func<object, object[], object>> Cache;

        static Function() {
            EmptyTypeArray = new Type[0];
            Cache = new Dictionary<int, Func<object, object[], object>>();
        }

        private Function() {
            Method = base.Evaluate;
        }

        public Function(Event.Handler Handler) {
            this.Method = Handler;
            this.Name = string.Concat(Handler.Method.DeclaringType.Name, '.', Handler.Method.Name);
        }

        public Function(string Name, Event.Handler Handler) {
            this.Name = Name;
            this.Method = Handler;
        }

        public Function(string Name, params string[] ArgumentNames) {
            this.Name = Name;
            this.Arguments = ArgumentNames;

            this.Method = base.Evaluate;
        }

        public Function(string Name, Event.Handler Handler, params string[] ArgumentNames) {
            this.Name = Name;
            this.Arguments = ArgumentNames.Where(N => N != "this").ToArray();
            this.Method = Handler;
        }

        public override object Evaluate(jsObject Context) {
            var Result = Method(Context);
            var R = Result as Expressions.Return;

            if (R != null)
                return R.Evaluate(Context);

            return Result;
        }

        public override string ToString() {
            return string.Format("{0}({1}) {{2}}", Name, string.Join(", ", Arguments), base.ToString());
        }

        public static Function Create(string Name, Func<object> Func) {
            return new Function(Name, (Args) => { return Func(); });
        }

        public static Function Create<T1>(string Name, Func<T1, object> Func, params string[] Params) {
            return new Function(Name, Event.Wrapper(Func, Params), Params);
        }

        public static Function Create<T1, T2>(string Name, Func<T1, T2, object> Func, params string[] Params) {
            return new Function(Name, Event.Wrapper(Func, Params), Params);
        }

        public static Function Create<T1, T2, T3>(string Name, Func<T1, T2, T3, object> Func, params string[] Params) {
            return new Function(Name, Event.Wrapper(Func, Params), Params);
        }

        public static Function Create<T1, T2, T3, T4>(string Name, Func<T1, T2, T3, T4, object> Func, params string[] Params) {
            return new Function(Name, Event.Wrapper(Func, Params), Params);
        }

        public static Function Create<T1, T2, T3, T4, T5>(string Name, Func<T1, T2, T3, T4, T5, object> Func, params string[] Params) {
            return new Function(Name, Event.Wrapper(Func, Params), Params);
        }

        public static Function Create<T1, T2, T3, T4, T5, T6>(string Name, Func<T1, T2, T3, T4, T5, T6, object> Func, params string[] Params) {
            return new Function(Name, Event.Wrapper(Func, Params), Params);
        }

        public static Function Create<T1, T2, T3, T4, T5, T6, T7>(string Name, Func<T1, T2, T3, T4, T5, T6, T7, object> Func, params string[] Params) {
            return new Function(Name, Event.Wrapper(Func, Params), Params);
        }

        public static Node Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            return Parse(Engine, Text, ref Index, LastIndex, true);
        }

        public static Node Parse(Engine Engine, string Text, ref int Index, int LastIndex, bool IsEngineWide = true) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            var Delta = Index;
            if (Text.Compare("func", Delta)) {
                Delta += 4;

                if (Text.Compare("tion", Delta)) {
                    Delta += 4;
                }
            }
            ConsumeWhitespace(Text, ref Delta);

            Function Func = null;

            var Open = Delta;
            var Close = Delta;

            ConsumeValidName(Text, ref Close);
            Delta = Close;
            ConsumeWhitespace(Text, ref Delta);

            if (Text.Compare("(", Delta)) {
                Func = new Function(Text.Substring(Open, Close - Open));

                Open = Delta + 1;
                Close = Delta;

                ConsumeEval(Text, ref Close);
                Delta = Close;
                ConsumeWhitespace(Text, ref Delta);

                if (Text.Compare("=>", Delta)) {
                    if (!string.IsNullOrEmpty(Func.Name))
                        return null;

                    Delta += 2;
                    ConsumeWhitespace(Text, ref Delta);
                }

                if (Text.Compare("{", Delta)) {
                    if (Expression.Parse(Engine, Text, ref Delta, LastIndex, Func)) {
                        Func.Arguments = Text.Substring(Open, Close - Open - 1).ParseCParams();

                        ConsumeWhitespace(Text, ref Delta);
                        Index = Delta;

                        if (!string.IsNullOrEmpty(Func.Name) && IsEngineWide) {
                            Engine.Functions[Func.Name] = Func;
                            return Expression.NoOperation;
                        }

                        return Func;
                    }
                }
            }
            return null;
        }

        public static Func<object, object[], object> GetFunction(Type Type, string Name, Type[] ArgTypes) {
            var Key = GetHashCode(Type.Name) + GetHashCode(Name);

            if (ArgTypes != null)
                Key -= GetHashCode(ArgTypes);
            else
                ArgTypes = EmptyTypeArray;

            Func<object, object[], object> Function;

            if (Cache.TryGetValue(Key, out Function))
                return Function;

            MethodInfo Info;

            if (Type.Name == Name) {
                return Cache[Key] = (object This, object[] Args) => {
                    return Activator.CreateInstance(Type, Args);
                };
            }
            else {
                try {
                    Info = Type.GetMethod(Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static, null, ArgTypes, null);

                    if (Info == null)
                        Info = Type.GetMethod(Name);
                }
                catch { 
					return null; 
				}
            }

            if (Info == null)
                return null;

            return Cache[Key] = GetDelegate(Info, ArgTypes);
        }

        static Func<object, object[], object> GetDelegate(MethodInfo Info, Type[] Types) {
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

        static int GetHashCode(string Input) {
            int Result = 0;

            foreach (char c in Input)
                Result += c;

            return Result;
        }

        static int GetHashCode(params Type[] Types) {
            int Result = 17;

            foreach (var T in Types) {
                foreach (var C in T.Name)
                    Result -= C;
                Result *= 31;
            }

            return Result;
        }
    }
}
