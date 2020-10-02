using System;

namespace Poly
{
    public partial struct StringView
    {
        public static readonly StringView Empty = new StringView(string.Empty);

        public int Index, LastIndex;

        public readonly string String;

        public StringView(string str) : this(str, 0, str?.Length ?? 0) { }

        public StringView(string str, int index, int lastIndex)
        {
            String = str ?? throw new ArgumentNullException(nameof(str));

            Index = index;
            LastIndex = lastIndex;
        }

        public bool IsDone
            => Index >= LastIndex;

        public bool IsValid
            => Iteration.BoundsCheck(String, Index, LastIndex);

        public char this[int index]
            => Iteration.BoundsCheck(String, index, LastIndex) ?
                String[index] :
                default;

        public char Current
            => this[Index];

        public char Next
            => this[Index + 1];

        public char Previous
            => this[Index - 1];

        public int Length
            => LastIndex - Index;

        public override int GetHashCode()
            => (Length << 16)
             + (Current << 8)
             + (Next);

        public override string ToString()
            => IsValid ?
                String[Index..LastIndex] :
                default;

        public StringView Clone(int? index = default, int? lastIndex = default)
            => new StringView(
                String,
                index ?? Index,
                lastIndex ?? LastIndex
                );

        public ReadOnlySpan<char> AsSpan()
            => String.AsSpan(Index, Length);

        public static implicit operator StringView(string text)
            => new StringView(text);
    }
}