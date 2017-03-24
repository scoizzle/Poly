namespace System {
    using Collections.Generic;
    
    public static class LinqExtensions {
        public static void Foreach<T>(this IEnumerable<T> enumerable, Action<T> f) {
            foreach (var element in enumerable) {
                f(element);
            }
        }
    }
}