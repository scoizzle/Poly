using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace System { 
    public static class EnumerableExtensions {
        public static bool All<T>(this IEnumerable<T> This, int Start, Func<T, bool> f) {
            var arr = This.ToArray();
            var len = arr.Length;

            for (int i = Start; i < len; i++) {
                if (!f(arr[i])) return false;
            }

            return true;
        }

        public static bool Any<T>(this IEnumerable<Func<T, bool>> This, T _t) {
            return This.Any(f => f(_t));
        }

        public static void ForEach<TK, TV>(this IDictionary<TK, TV> This, Action<TK, TV> method) {
            foreach (var pair in This)
                method(pair.Key, pair.Value);
        }

        public static IEnumerable<T> Slice<T>(this IEnumerable<T> This, int Index, int Count) {
            var Array = This.ToArray();
            var End = Index + Count;

            if (Index < 0 || Index >= Array.Length || End >= Array.Length)
                throw new ArgumentOutOfRangeException();

            while (Index < End) {
                yield return Array[Index++];
            }
        }
    }
}
