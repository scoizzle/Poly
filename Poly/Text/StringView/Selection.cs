using System;
using System.Collections.Generic;

namespace Poly
{
    public static class StringViewSelection
    {
        public static IEnumerable<StringView> GetSplitSections(ref this StringView view, char character)
            => GetSplitSections(view.FindAll(character), view.String, view.Index, view.LastIndex, 1);

        public static IEnumerable<StringView> GetSplitSections(ref this StringView view, string text)
            => GetSplitSections(view.FindAll(text), view.String, view.Index, view.LastIndex, 1);

        public static IEnumerable<StringView> GetSplitSections(ref this StringView view, string text, int index, int length)
            => GetSplitSections(view.FindAll(text, index, length), view.String, view.Index, view.LastIndex, 1);

        private static IEnumerable<StringView> GetSplitSections(IEnumerable<int> indices, string text, int index, int lastIndex, int offset)
        {
            foreach (var i in indices)
            {
                if (i != index)
                    yield return new StringView(text, index, i);

                index = i + offset;
            }

            if (index < lastIndex)
                yield return new StringView(text, index, lastIndex);
        }
    }
}