using System;
using System.Runtime.CompilerServices;

namespace Poly
{
    public static class StringViewConsumption
    {
        public static bool Consume(ref this StringView view, int n = 1)
            => Iteration.Consume(view.String, ref view.Index, view.LastIndex, n);

        public static bool Consume(ref this StringView view, out char character)
            => Iteration.Consume(view.String, ref view.Index, view.LastIndex, out character);

        public static bool Consume(ref this StringView view, char character)
            => Iteration.Consume(view.String, ref view.Index, view.LastIndex, character);

        public static bool Consume(ref this StringView view, string text, StringComparison comparison = StringComparison.Ordinal)
            => Iteration.Consume(view.String, ref view.Index, view.LastIndex, text, comparison);

        public static bool Consume(ref this StringView view, string text, int index, int length, StringComparison comparison = StringComparison.Ordinal)
            => Iteration.Consume(view.String, ref view.Index, view.LastIndex, text, index, length, comparison);

        public static bool Consume(ref this StringView view, in StringView sub, StringComparison comparison = StringComparison.Ordinal)
            => Iteration.Consume(view.String, ref view.Index, view.LastIndex, sub.String, sub.Index, sub.Length, comparison);

        public static bool Consume(ref this StringView view, Func<char, bool> predicate)
            => Iteration.Consume(view.String, ref view.Index, view.LastIndex, predicate);

        public static bool Consume(ref this StringView view, params Func<char, bool>[] predicates)
            => Iteration.Consume(view.String, ref view.Index, view.LastIndex, predicates);

        public static bool ConsumeUntil(ref this StringView view, Func<char, bool> predicate)
            => Iteration.ConsumeUntil(view.String, ref view.Index, view.LastIndex, predicate);

        public static bool ConsumeUntil(ref this StringView view, params Func<char, bool>[] predicates)
            => Iteration.ConsumeUntil(view.String, ref view.Index, view.LastIndex, predicates);

        public static bool ConsumeWhitespace(ref this StringView view)
            => Iteration.Consume(view.String, ref view.Index, view.LastIndex, char.IsWhiteSpace);

        public static bool Goto(ref this StringView view, char character)
            => Iteration.Goto(view.String, ref view.Index, view.LastIndex, character);

        public static bool Goto(ref this StringView view, string text)
            => Iteration.Goto(view.String, ref view.Index, view.LastIndex, text);

        public static bool Goto(ref this StringView view, string text, int index, int length)
            => Iteration.Goto(view.String, ref view.Index, view.LastIndex, text, index, length);

        public static bool Goto(ref this StringView view, in StringView sub)
            => Iteration.Goto(view.String, ref view.Index, view.LastIndex, sub.String, sub.Index, sub.Length);

        public static bool GotoAndConsume(ref this StringView view, char character)
            => Iteration.GotoAndConsume(view.String, ref view.Index, view.LastIndex, character);

        public static bool GotoAndConsume(ref this StringView view, string text)
            => Iteration.GotoAndConsume(view.String, ref view.Index, view.LastIndex, text);

        public static bool GotoAndConsume(ref this StringView view, string text, int index, int length)
            => Iteration.GotoAndConsume(view.String, ref view.Index, view.LastIndex, text, index, length);

        public static bool GotoAndConsume(ref this StringView view, in StringView sub)
            => Iteration.GotoAndConsume(view.String, ref view.Index, view.LastIndex, sub.String, sub.Index, sub.Length);

        public static bool GotoAndConsume(ref this StringView view, Func<char, bool> predicate)
            => Iteration.GotoAndConsume(view.String, ref view.Index, view.LastIndex, predicate);

        public static bool GotoAndConsume(ref this StringView view, params Func<char, bool>[] predicates)
            => Iteration.GotoAndConsume(view.String, ref view.Index, view.LastIndex, predicates);
    }
}