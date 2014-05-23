using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly {
    public partial class Event {
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

        public static Handler Wrapper<T1>(Action<T1> Func, params string[] ArgNames) {
			if(Func == null)
				return null;

            return (Args) => {
                object[] Arguments = new object[1];

                Arguments[0] = Args.Get<T1>(ArgNames[0]);

                return Func.DynamicInvoke(Arguments);
            };
        }

        public static Handler Wrapper<T1>(Func<T1, object> Func, params string[] ArgNames) {
			if(Func == null)
				return null;

            return (Args) => {
                object[] Arguments = new object[1];

                Arguments[0] = Args.Get<T1>(ArgNames[0]);

                return Func.DynamicInvoke(Arguments);
            };
        }

        public static Handler Wrapper<T1, T2>(Action<T1, T2> Func, params string[] ArgNames) {
			if(Func == null)
				return null;

            return (Args) => {
                object[] Arguments = new object[2];

                Arguments[0] = Args.Get<T1>(ArgNames[0]);
                Arguments[1] = Args.Get<T2>(ArgNames[1]);

                return Func.DynamicInvoke(Arguments);
            };
        }

        public static Handler Wrapper<T1, T2>(Func<T1, T2, object> Func, params string[] ArgNames) {
			if(Func == null)
				return null;

            return (Args) => {
                object[] Arguments = new object[2];

                Arguments[0] = Args.Get<T1>(ArgNames[0]);
                Arguments[1] = Args.Get<T2>(ArgNames[1]);

                return Func.DynamicInvoke(Arguments);
            };
        }

        public static Handler Wrapper<T1, T2, T3>(Action<T1, T2, T3> Func, params string[] ArgNames) {
			if(Func == null)
				return null;

            return (Args) => {
                object[] Arguments = new object[3];

                Arguments[0] = Args.Get<T1>(ArgNames[0]);
                Arguments[1] = Args.Get<T2>(ArgNames[1]);
                Arguments[2] = Args.Get<T3>(ArgNames[2]);

                return Func.DynamicInvoke(Arguments);
            };
        }

        public static Handler Wrapper<T1, T2, T3>(Func<T1, T2, T3, object> Func, params string[] ArgNames) {
			if(Func == null)
				return null;

            return (Args) => {
                object[] Arguments = new object[3];

                Arguments[0] = Args.Get<T1>(ArgNames[0]);
                Arguments[1] = Args.Get<T2>(ArgNames[1]);
                Arguments[2] = Args.Get<T3>(ArgNames[2]);

                return Func.DynamicInvoke(Arguments);
            };
        }

        public static Handler Wrapper<T1, T2, T3, T4>(Action<T1, T2, T3, T4> Func, params string[] ArgNames) {
			if(Func == null)
				return null;

            return (Args) => {
                object[] Arguments = new object[4];

                Arguments[0] = Args.Get<T1>(ArgNames[0]);
                Arguments[1] = Args.Get<T2>(ArgNames[1]);
                Arguments[2] = Args.Get<T3>(ArgNames[2]);
                Arguments[3] = Args.Get<T4>(ArgNames[3]);

                return Func.DynamicInvoke(Arguments);
            };
        }

        public static Handler Wrapper<T1, T2, T3, T4>(Func<T1, T2, T3, T4, object> Func, params string[] ArgNames) {
			if(Func == null)
				return null;

            return (Args) => {
                object[] Arguments = new object[4];

                Arguments[0] = Args.Get<T1>(ArgNames[0]);
                Arguments[1] = Args.Get<T2>(ArgNames[1]);
                Arguments[2] = Args.Get<T3>(ArgNames[2]);
                Arguments[3] = Args.Get<T4>(ArgNames[3]);

                return Func.DynamicInvoke(Arguments);
            };
        }

        public static Handler Wrapper<T1, T2, T3, T4, T5>(Action<T1, T2, T3, T4, T5> Func, params string[] ArgNames) {
			if(Func == null)
				return null;

            return (Args) => {
                object[] Arguments = new object[5];

                Arguments[0] = Args.Get<T1>(ArgNames[0]);
                Arguments[1] = Args.Get<T2>(ArgNames[1]);
                Arguments[2] = Args.Get<T3>(ArgNames[2]);
                Arguments[3] = Args.Get<T4>(ArgNames[3]);
                Arguments[4] = Args.Get<T5>(ArgNames[4]);

                return Func.DynamicInvoke(Arguments);
            };
        }

        public static Handler Wrapper<T1, T2, T3, T4, T5>(Func<T1, T2, T3, T4, T5, object> Func, params string[] ArgNames) {
			if(Func == null)
				return null;

            return (Args) => {
                object[] Arguments = new object[5];

                Arguments[0] = Args.Get<T1>(ArgNames[0]);
                Arguments[1] = Args.Get<T2>(ArgNames[1]);
                Arguments[2] = Args.Get<T3>(ArgNames[2]);
                Arguments[3] = Args.Get<T4>(ArgNames[3]);
                Arguments[4] = Args.Get<T5>(ArgNames[4]);

                return Func.DynamicInvoke(Arguments);
            };
        }

        public static Handler Wrapper<T1, T2, T3, T4, T5, T6>(Action<T1, T2, T3, T4, T5, T6> Func, params string[] ArgNames) {
			if(Func == null)
				return null;

            return (Args) => {
                object[] Arguments = new object[6];

                Arguments[0] = Args.Get<T1>(ArgNames[0]);
                Arguments[1] = Args.Get<T2>(ArgNames[1]);
                Arguments[2] = Args.Get<T3>(ArgNames[2]);
                Arguments[3] = Args.Get<T4>(ArgNames[3]);
                Arguments[4] = Args.Get<T5>(ArgNames[4]);
                Arguments[5] = Args.Get<T6>(ArgNames[5]);

                return Func.DynamicInvoke(Arguments);
            };
        }

        public static Handler Wrapper<T1, T2, T3, T4, T5, T6>(Func<T1, T2, T3, T4, T5, T6, object> Func, params string[] ArgNames) {
			if(Func == null)
				return null;

            return (Args) => {
                object[] Arguments = new object[6];

                Arguments[0] = Args.Get<T1>(ArgNames[0]);
                Arguments[1] = Args.Get<T2>(ArgNames[1]);
                Arguments[2] = Args.Get<T3>(ArgNames[2]);
                Arguments[3] = Args.Get<T4>(ArgNames[3]);
                Arguments[4] = Args.Get<T5>(ArgNames[4]);
                Arguments[5] = Args.Get<T6>(ArgNames[5]);

                return Func.DynamicInvoke(Arguments);
            };
        }

        public static Handler Wrapper<T1, T2, T3, T4, T5, T6, T7>(Action<T1, T2, T3, T4, T5, T6, T7> Func, params string[] ArgNames) {
			if(Func == null)
				return null;

            return (Args) => {
                object[] Arguments = new object[7];

                Arguments[0] = Args.Get<T1>(ArgNames[0]);
                Arguments[1] = Args.Get<T2>(ArgNames[1]);
                Arguments[2] = Args.Get<T3>(ArgNames[2]);
                Arguments[3] = Args.Get<T4>(ArgNames[3]);
                Arguments[4] = Args.Get<T5>(ArgNames[4]);
                Arguments[5] = Args.Get<T6>(ArgNames[5]);
                Arguments[6] = Args.Get<T7>(ArgNames[6]);

                return Func.DynamicInvoke(Arguments);
            };
        }

        public static Handler Wrapper<T1, T2, T3, T4, T5, T6, T7>(Func<T1, T2, T3, T4, T5, T6, T7, object> Func, params string[] ArgNames) {
			if(Func == null)
				return null;

            return (Args) => {
                object[] Arguments = new object[7];

                Arguments[0] = Args.Get<T1>(ArgNames[0]);
                Arguments[1] = Args.Get<T2>(ArgNames[1]);
                Arguments[2] = Args.Get<T3>(ArgNames[2]);
                Arguments[3] = Args.Get<T4>(ArgNames[3]);
                Arguments[4] = Args.Get<T5>(ArgNames[4]);
                Arguments[5] = Args.Get<T6>(ArgNames[5]);
                Arguments[6] = Args.Get<T7>(ArgNames[6]);

                return Func.DynamicInvoke(Arguments);
            };
        }
    }
}
