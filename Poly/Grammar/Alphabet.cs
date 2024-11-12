using Poly.Text.Matching.Expressions;

namespace Poly.Grammar;

public sealed partial class Alphabet<TSymbol, TToken>
    where TSymbol : Enum
    where TToken : Enum
{

}

[Flags]
public enum JsonToken
{
    Unknown,
    Partial,
    Null,
    Number,
    String,
    BeginObject,
    EndObject,
    BeginArray,
    EndArray,
    True,
    False,
    Member,
    MemberSeparator,
    Whitespace
}

public class Ugh
{
    public IEnumerable<JsonToken> GetJsonTokens(string text, CancellationToken cancellationToken = new())
    {
        int index = 0;
        while (index < text.Length)
        {
            var start = index;
            cancellationToken.ThrowIfCancellationRequested();

            ParseResult result = Parse(text, ref index);

            if (result.Token == JsonToken.Unknown)
                break;

            if (index == start)
                throw new Exception(text.Substring(index, Math.Min(50, text.Length - index)));

            yield return result.Token;
        }
    }

    [DebuggerDisplay("{Token} : {Content}")]
    private readonly ref struct ParseResult(JsonToken token, ReadOnlySpan<char> content)
    {
        public JsonToken Token { get; } = token;
        public ReadOnlySpan<char> Content { get; } = content;
    }

    private ParseResult Parse(ReadOnlySpan<char> chars, ref int index)
    {
        SkipWhitespace(chars, ref index);

        if (!BoundsCheck(chars, index))
            goto failure;

        char chr = chars[index];

        switch (chr)
        {
            case '{':
                index++;
                return new ParseResult(JsonToken.BeginObject, string.Empty);

            case '}':
                index++;
                return new ParseResult(JsonToken.EndObject, string.Empty);

            case '[':
                index++;
                return new ParseResult(JsonToken.BeginArray, string.Empty);

            case ']':
                index++;
                return new ParseResult(JsonToken.EndArray, string.Empty);

            case '"':
                return ExtractString(chars, ref index);

            case '0':
            case '1':
            case '2':
            case '3':
            case '4':
            case '5':
            case '6':
            case '7':
            case '8':
            case '9':
            case '-':
                return ExtractNumber(chars, ref index);

            case 'n':
                return ExtractLiteral(chars, ref index, "null", JsonToken.Null);

            case 't':
                return ExtractLiteral(chars, ref index, "true", JsonToken.True);

            case 'f':
                return ExtractLiteral(chars, ref index, "false", JsonToken.False);
            
            case ',':
                index++;
                return new ParseResult(JsonToken.MemberSeparator, string.Empty);

            default:
                Debug.Assert(false, $"Unexpected character '{chr}'");
                return new ParseResult(JsonToken.Unknown, string.Empty);
        }

    failure:
        return new ParseResult(JsonToken.Unknown, string.Empty);

    }

    bool BoundsCheck(ReadOnlySpan<char> chars, int index)
    {
        return index >= 0 && index < chars.Length;
    }

    void SkipWhitespace(ReadOnlySpan<char> chars, ref int index)
    {
        while (BoundsCheck(chars, index) && char.IsWhiteSpace(chars[index])) index++;
    }

    ParseResult ExtractString(ReadOnlySpan<char> chars, ref int index)
    {
        int start = index++;
        while (BoundsCheck(chars, index))
        {
            switch (chars[index])
            {
                case '"':
                    index++;
                    ReadOnlySpan<char> result = chars.Slice(start + 1, index - start - 2);

                    SkipWhitespace(chars, ref index);

                    if (BoundsCheck(chars, index) && chars[index] == ':')
                    {
                        index++;
                        return new ParseResult(JsonToken.Member, result);
                    }

                    return new ParseResult(JsonToken.String, result);

                case '\\':
                    break;
            }

            index++;
        }

        return new ParseResult(JsonToken.Partial | JsonToken.String, string.Empty);
    }

    ParseResult ExtractNumber(ReadOnlySpan<char> chars, ref int index)
    {
        var slice = chars.Slice(index);
        var offset = slice.IndexOfAny([',', '}', ']']);

        if (offset == -1)
            return new ParseResult(JsonToken.Partial | JsonToken.Number, string.Empty);

        index += offset;
        return new ParseResult(JsonToken.Number, slice.Slice(0, offset));
    }

    ParseResult ExtractLiteral(ReadOnlySpan<char> chars, ref int index, ReadOnlySpan<char> candidate, JsonToken token)
    {
        var slice = chars.Slice(index);
        if (!BoundsCheck(slice, candidate.Length))
            return new ParseResult(JsonToken.Partial | token, string.Empty);

        if (!slice.StartsWith(candidate))
            return new ParseResult(JsonToken.Unknown, string.Empty);

        index += candidate.Length;
        return new ParseResult(token, string.Empty);
    }
}