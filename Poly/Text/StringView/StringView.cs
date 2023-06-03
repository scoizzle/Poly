namespace Poly;

public partial struct StringView : IComparable<StringView>
{
    public static readonly StringView Empty = new(string.Empty);

    public int Index, LastIndex;

    public readonly string String;

    public StringView(string str) : this(str, 0, str?.Length ?? 0) { }

    public StringView(string str, int index, int lastIndex)
    {
        Guard.IsNotNull(str);
        Guard.IsInRange(index, 0, lastIndex);
        Guard.IsInRange(lastIndex, index, str.Length + 1);

        (String, Index, LastIndex) = (str, index, lastIndex);
    }

    public bool IsDone => Index >= LastIndex;

    public bool IsValid => Iteration.BoundsCheck(String, Index, LastIndex);

    public char this[int index] =>
        Iteration.BoundsCheck(String, index, LastIndex)
            ? String[index]
            : default;

    public char Current => this[Index];

    public char Next => this[Index + 1];

    public char Previous => this[Index - 1];

    public int Length => LastIndex - Index;

    public override int GetHashCode() => unchecked((Length << 16) + (Current << 8) + Next);

    public override string ToString() => 
        IsValid ?
            String[Index..LastIndex] :
            default;

    public ReadOnlySpan<char> AsSpan() => String.AsSpan(Index, Length);

    public int CompareTo(StringView other)
    {
        if (ReferenceEquals(String, other.String))
        {
            return (Index, LastIndex) == (other.Index, other.LastIndex)
                ? 0
                : -1;
        }

        return Length == other.Length
            ? string.Compare(String, Index, other.String, other.Index, Length)
            : -1;
    }

    public static implicit operator StringView(string text) => new(text);

    public static implicit operator StringView(ReadOnlyMemory<char> text) => new(text.ToString());
}
