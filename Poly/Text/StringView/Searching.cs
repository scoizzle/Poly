using System;
using System.Collections.Generic;

namespace Poly {
    public static class StringViewSearching {
        public static int IndexOf(ref this StringView view, char character)
            => Searching.IndexOf(view.String, view.Index, view.LastIndex, character);

        public static int IndexOf(ref this StringView view, string text, StringComparison comparison = StringComparison.Ordinal)
            => Searching.IndexOf(view.String, view.Index, view.LastIndex, text, comparison);

        public static int IndexOf(ref this StringView view, string text, int index, int length, StringComparison comparison = StringComparison.Ordinal)
            => Searching.IndexOf(view.String, view.Index, view.LastIndex, text, index, length, comparison);
            
        public static int IndexOfAny(ref this StringView view, ReadOnlySpan<char> character)
            => Searching.IndexOfAny(view.String, view.Index, view.LastIndex, character);

        public static int FindMatchingBracket(ref this StringView view, char open, char close)
            => Searching.FindMatchingBracket(view.String, view.Index, view.LastIndex, open, close);

        public static IEnumerable<int> FindAll(ref this StringView view, char character)
            => Searching.FindAll(view.String, view.Index, view.LastIndex, character);

        public static IEnumerable<int> FindAll(ref this StringView view, string text)
            => Searching.FindAll(view.String, view.Index, view.LastIndex, text);
            
        public static IEnumerable<int> FindAll(ref this StringView view, string text, int index, int length)
            => Searching.FindAll(view.String, view.Index, view.LastIndex, text, index, length);
    }
}