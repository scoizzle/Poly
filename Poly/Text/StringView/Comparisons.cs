using System;

namespace Poly {
    public static class StringViewComparison {
        public static bool IsAt(ref this StringView view, char character)
            => Iteration.IsAt(view.String, view.Index, view.LastIndex, character);

        public static bool IsAt(ref this StringView view, string text, StringComparison comparison = StringComparison.Ordinal)
            => Iteration.IsAt(view.String, view.Index, view.LastIndex, text, comparison);

        public static bool IsAt(ref this StringView view, string text, int index, int length, StringComparison comparison = StringComparison.Ordinal)
            => Iteration.IsAt(view.String, view.Index, view.LastIndex, text, index, length, comparison);

        public static bool IsAt(ref this StringView view, StringView sub, StringComparison comparison = StringComparison.Ordinal)
            => Iteration.IsAt(view.String, view.Index, view.LastIndex, sub.String, sub.Index, sub.Length, comparison);

        public static bool IsAt(ref this StringView view, Func<char, bool> predicate)
            => Iteration.IsAt(view.String, view.Index, view.LastIndex, predicate);

        public static bool IsAt(ref this StringView view, params Func<char, bool>[] predicates)
            => Iteration.IsAt(view.String, view.Index, view.LastIndex, predicates);

        public static bool IsNotAt(ref this StringView view, Func<char, bool> predicate)
            => Iteration.IsNotAt(view.String, view.Index, view.LastIndex, predicate);

        public static bool IsNotAt(ref this StringView view, params Func<char, bool>[] predicates)
            => Iteration.IsNotAt(view.String, view.Index, view.LastIndex, predicates);
        
        public static bool Equals(ref this StringView view, char character)
            => view.Length == 1 
            && Iteration.IsAt(view.String, view.Index, view.LastIndex, character);

        public static bool Equals(ref this StringView view, string text, StringComparison comparison = StringComparison.Ordinal)
            => Iteration.Equals(view.String, view.Index, view.LastIndex, text, comparison);

        public static bool Equals(ref this StringView view, string text, int index, int length, StringComparison comparison = StringComparison.Ordinal)
            => Iteration.Equals(view.String, view.Index, view.LastIndex, text, index, length, comparison);

        public static bool Equals(ref this StringView view, StringView sub, StringComparison comparison = StringComparison.Ordinal)
            => view.Length == sub.Length
            && Iteration.IsAt(view.String, view.Index, view.LastIndex, sub.String, sub.Index, sub.Length, comparison); 
    }
}