namespace Poly;

public partial struct StringView : IComparable<StringView>
{
    public static readonly StringView Empty = new(string.Empty);

    public int Index, LastIndex;

    public readonly string String;

    public StringView(string str) : this(str, 0, str?.Length ?? 0) { }

    public StringView(string str, int Begin, int End)
    {
        Guard.IsNotNull(str);
        Guard.IsInRange(Begin, 0, End);
        Guard.IsInRange(End, Begin, str.Length + 1);

        (String, Index, LastIndex) = (str, Begin, End);
    }

    public bool IsDone => Index >= LastIndex;

    public bool IsValid => Iteration.BoundsCheck(String, Index, LastIndex);

    public char this[int Begin] =>
        Iteration.BoundsCheck(String, Begin, LastIndex)
            ? String[Begin]
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

    public ReadOnlySpan<char> AsSpan(int? Begin = default, int? length = default) => String.AsSpan(Begin ?? Index, length ?? Length);

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
