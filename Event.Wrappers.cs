using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;

using Poly.Data;

namespace Poly {
    public partial class Event {
        public static string[] GetArgumentNames(MethodInfo Info) {
            var Args = Info.GetParameters();
            var Names = new string[Args.Length];

            for (int i = 0; i < Names.Length; i++) {
                Names[i] = Args[i].Name == "This" ? "this" : Args[i].Name;
            }

            return Names;
        }

        public static Handler Wrapper(Handler Func, params object[] ArgPairs) {
            if (Func == null)
                return null;

            return (Args) => {
                for (int i = 0; i < ArgPairs.Length / 2; i += 2) {
                    if (Func == null)
                        return null;

                    Args[ArgPairs[i].ToString()] = ArgPairs[i + 1];
                }

                return Func(Args);
            };
        }

        public static Handler Wrapper(Action Func) {
            if (Func == null)
                return null;

            return (Args) => {
                Func();
                return null;
            };
        }

        public static Handler Wrapper(Func<object> Func) {
            if (Func == null)
                return null;

            return (Args) => {
                return Func();
            };
        }

        public static Handler Wrapper<T1>(Func<T1, object> Func) {
            if (Func == null)
                return null;

            var Names = GetArgumentNames(Func.Method);

            return (Args) => {
                return Func(
                    Args.Get<T1>(Names[0])
                );
            };
        }

        public static Handler Wrapper<T1, T2>(Func<T1, T2, object> Func) {
            if (Func == null)
                return null;

            var Names = GetArgumentNames(Func.Method);

            return (Args) => {
                return Func(
                    Args.Get<T1>(Names[0]),
                    Args.Get<T2>(Names[1])
                );
            };
        }

        public static Handler Wrapper<T1, T2, T3>(Func<T1, T2, T3, object> Func) {
            if (Func == null)
                return null;

            var Names = GetArgumentNames(Func.Method);

            return (Args) => {
                return Func(
                    Args.Get<T1>(Names[0]),
                    Args.Get<T2>(Names[1]),
                    Args.Get<T3>(Names[2])
                );
            };
        }

        public static Handler Wrapper<T1, T2, T3, T4>(Func<T1, T2, T3, T4, object> Func) {
            if (Func == null)
                return null;

            var Names = GetArgumentNames(Func.Method);

            return (Args) => {
                return Func(
                    Args.Get<T1>(Names[0]),
                    Args.Get<T2>(Names[1]),
                    Args.Get<T3>(Names[2]),
                    Args.Get<T4>(Names[3])
                );
            };
        }

        public static Handler Wrapper<T1, T2, T3, T4, T5>(Func<T1, T2, T3, T4, T5, object> Func) {
            if (Func == null)
                return null;

            var Names = GetArgumentNames(Func.Method);

            return (Args) => {
                return Func(
                    Args.Get<T1>(Names[0]),
                    Args.Get<T2>(Names[1]),
                    Args.Get<T3>(Names[2]),
                    Args.Get<T4>(Names[3]),
                    Args.Get<T5>(Names[4])
                );
            };
        }

        public static Handler Wrapper<T1, T2, T3, T4, T5, T6>(Func<T1, T2, T3, T4, T5, T6, object> Func) {
            if (Func == null)
                return null;

            var Names = GetArgumentNames(Func.Method);

            return (Args) => {
                return Func(
                    Args.Get<T1>(Names[0]),
                    Args.Get<T2>(Names[1]),
                    Args.Get<T3>(Names[2]),
                    Args.Get<T4>(Names[3]),
                    Args.Get<T5>(Names[4]),
                    Args.Get<T6>(Names[5])
                );
            };
        }

        public static Handler Wrapper<T1, T2, T3, T4, T5, T6, T7>(Func<T1, T2, T3, T4, T5, T6, T7, object> Func) {
            if (Func == null)
                return null;

            var Names = GetArgumentNames(Func.Method);

            return (Args) => {
                return Func(
                    Args.Get<T1>(Names[0]),
                    Args.Get<T2>(Names[1]),
                    Args.Get<T3>(Names[2]),
                    Args.Get<T4>(Names[3]),
                    Args.Get<T5>(Names[4]),
                    Args.Get<T6>(Names[5]),
                    Args.Get<T7>(Names[6])
                );
            };
        }
    }
}
