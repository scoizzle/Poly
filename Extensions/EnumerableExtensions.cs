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
    }
}
