using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
    
namespace Poly {
    public static class LinqExtensions {
        public static IEnumerable<U> TrySelect<T, U>(this IEnumerable<T> enumerable, Func<T, U> selector) {
            foreach (var element in enumerable) {
                U sub_element;

                try { sub_element = selector(element); }
                catch (Exception error) { Log.Debug(error); continue; }
                
                yield return sub_element;
            }
        }

        public static IEnumerable<U> TrySelectMany<T, U>(this IEnumerable<T> enumerable, Func<T, IEnumerable<U>> selector) {
            foreach (var element in enumerable) {
                IEnumerable<U> sub_enumerable;

                try { sub_enumerable = selector(element); }
                catch (Exception error) { Log.Debug(error); continue; }

                foreach (var sub_element in sub_enumerable)
                    yield return sub_element;
            }
        }
    }
}