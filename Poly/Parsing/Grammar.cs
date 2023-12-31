using Poly.Parsing.Json;

namespace Poly.Parsing;

public readonly struct JsonStateMachine
{
    static readonly StateMachine<JsonToken, JsonToken> _state;

    static JsonStateMachine()
    {
        _state = new StateMachine<JsonToken, JsonToken>(JsonToken.Invalid); 

        _state.Configure(JsonToken.Invalid)
            .Permit(JsonToken.BeginObject, JsonToken.BeginObject)
            .Permit(JsonToken.BeginArray, JsonToken.BeginArray)
            .Permit(JsonToken.NullValue, JsonToken.NullValue)
            .Permit(JsonToken.String, JsonToken.String)
            .Permit(JsonToken.Number, JsonToken.Number);

        _state.Configure(JsonToken.BeginObject)
            .Permit(JsonToken.MemberSeperator, JsonToken.MemberSeperator);

        _state.Configure(JsonToken.BeginArray)
            .Permit(JsonToken.BeginArray, JsonToken.BeginArray)
            .Permit(JsonToken.BeginObject, JsonToken.BeginObject)
            .Permit(JsonToken.NullValue, JsonToken.NullValue)
            .Permit(JsonToken.String, JsonToken.String)
            .Permit(JsonToken.Number, JsonToken.Number);
    }
}

public readonly ref struct TokenReaderResult<TSequence, TToken> {
    public TokenReaderResult(TToken token, ReadOnlySpan<TSequence> body) { 
        Token = token;
        Body = body;
    }

    public TToken Token { get; init; }

    public ReadOnlySpan<TSequence> Body { get; init; }
}

public interface ITokenReader<TToken> {
    
}

public interface ITokenParser<TToken, TExpression> {
    TExpression? CurrentExpression { get; }

    bool TryParseExpression(out TExpression expression);
}

public interface IGrammar<TToken, TExpression>
{
    ITokenParser<TToken, TExpression> GetTokenParser();
}

public struct JsonStringTokenReader : ITokenReader<JsonToken>
{
    private StringView _text;

    public JsonStringTokenReader(StringView text) => _text = text;
    
    public JsonToken Current { get; private set; } = JsonToken.Invalid;

    public bool IsDone => _text.IsDone;

    public bool TryReadToken(out TokenReaderResult<char, JsonToken> result)
    {
        switch (_text.Current)
        {
            case '{':
                result = new(JsonToken.BeginObject, _text.AsSpan(length: 1));
                break;

            case '}':
                result = new(JsonToken.EndObject, _text.AsSpan(length: 1));
                break;

            case '[':
                result = new(JsonToken.BeginArray, _text.AsSpan(length: 1));
                break;

            case ']':
                result = new(JsonToken.EndArray, _text.AsSpan(length: 1));
                break;

            case '"':
                result = new(JsonToken.String, _text.AsSpan(length: 1));
                break;

            case ':':
                result = new(JsonToken.NameSeperator, _text.AsSpan(length: 1));
                break;

            case ',':
                result = new(JsonToken.MemberSeperator, _text.AsSpan(length: 1)); 
                break;

            case 't':
                if (!_text.IsAt("true"))
                    goto default;

                result = new(JsonToken.TrueValue, _text.AsSpan(length: 4));
                break;

            case 'f':
                if (!_text.IsAt("false"))
                    goto default;

                result = new(JsonToken.TrueValue, _text.AsSpan(length: 5));
                break;


            case 'n':
                if (!_text.IsAt("null"))
                    goto default;

                result = new(JsonToken.TrueValue, _text.AsSpan(length: 4));
                break;


            default:
                result = new(default, default);
                break;
        }
        var token = _text.Current switch {       
            '{' => JsonToken.BeginObject,
            '}' => JsonToken.EndObject,
            '[' => JsonToken.BeginArray,
            ']' => JsonToken.EndArray,
            '"' => JsonToken.String,
            ':' => JsonToken.NameSeperator,
            ',' => JsonToken.MemberSeperator,
            't' => _text.IsAt("true") ? JsonToken.TrueValue : JsonToken.Invalid,
            'f' => _text.IsAt("false") ? JsonToken.FalseValue : JsonToken.Invalid,
            'n' => _text.IsAt("null") ? JsonToken.NullValue : JsonToken.Invalid,
            '/' => _text.IsAt("//") ? JsonToken.Comment : JsonToken.Invalid,
            (>= '0' and <= '9') or '.' or '-' or 'e' or 'E' => JsonToken.Number,
            char chr when char.IsWhiteSpace(chr) => JsonToken.Whitespace,
            _ => JsonToken.Invalid
        };

        if (token == JsonToken.Invalid) {
            result = new(JsonToken.Invalid, default);
            return false;
        }

        var body = token switch {
            JsonToken.BeginObject or
            JsonToken.EndObject or
            JsonToken.BeginArray or
            JsonToken.EndArray or
            JsonToken.NameSeperator or
            JsonToken.MemberSeperator => _text.ExtractAsSpan(1),

            JsonToken.TrueValue or
            JsonToken.NullValue => _text.ExtractAsSpan(4),

            JsonToken.FalseValue => _text.ExtractAsSpan(5),

            JsonToken.String => _text.ExtractStringLiteralAsSpan(),
            JsonToken.Comment => _text.ExtractUntilCharOrEndAsSpan(stackalloc char[] { '\n' }),

            JsonToken.Whitespace or
            JsonToken.NewLine => _text.ExtractWhileAsSpan(char.IsWhiteSpace),
            
            JsonToken.Number => _text.ExtractUntilCharOrEndAsSpan(stackalloc char[] { ',', '}', ']' }, inclusive: false),

            _ => ReadOnlySpan<char>.Empty,
        };

        if (body.IsEmpty) {
            result = new(JsonToken.Invalid, default);
            return false;
        }

        result = new(token, ReadOnlySpan<char>.Empty);
        return true;
    }

    public override string ToString() => _text.ToString();
}

public interface IJsonExpression
{

}

public struct JsonStringTokenParser : ITokenParser<JsonToken, IJsonExpression>
{
    private JsonStringTokenReader _reader;

    public JsonStringTokenParser(JsonStringTokenReader reader) => _reader = reader;

    public IJsonExpression? CurrentExpression { get; }

    public bool TryParseExpression(out IJsonExpression expression)
    {
        expression = default;
        return _reader.TryReadToken(out _);
    }
}


public interface ILexable<in TInput, TOutput>
{
    static abstract bool TryLex(TInput sequence, out TOutput result);
}

public interface IParseable<in TInput, TOutput>
{
    static abstract bool TryParse(TInput sequence, out TOutput result);
}

class Test : ILexable<StringView, StringView>,
             IParseable<IEnumerable<StringView>, StringView>
{
    public static bool TryLex(StringView sequence, out StringView result)
    {
        var index = sequence.Index;

        if (!sequence.Goto(' '))
        {
            result = sequence;
            return true;
        }

        result = sequence with { 
            Index = index, 
            LastIndex = sequence.Index 
        };

        sequence.Consume();
        return true;
    }

    public static bool TryParse(IEnumerable<StringView> sequence, out StringView result)
    {
        throw new NotImplementedException();
    }
}

public class Gram<TSequence, TLexicalSlice, TToken>
{

}

public partial record Grammar<TSequence, TToken>
{
    readonly List<TokenParserDelegate> _tokenParserDelegates = new();

    public TokenParser ParseAllTokens(in ReadOnlySequence<TSequence> sequence)
        => new(this, in sequence);

    private TokenParsingResult Parse(
        ref ReadOnlySequence<TSequence> sequence)
    {
        if (sequence.IsEmpty)
            return TokenParsingResult.Failure;

        foreach (var parser in _tokenParserDelegates)
        {
            if (parser(ref sequence) is { Success: true } result)
            {
                return result;
            }
        }

        return TokenParsingResult.Failure;
    }
}

public partial record Grammar<TSequence, TToken>
{
    public delegate TokenParsingResult TokenParserDelegate(ref ReadOnlySequence<TSequence> context);

    public interface ITokenParser
    {
        TokenParsingResult Parse(ref ReadOnlySequence<TSequence> context);
    }

    public record struct TokenParsingResult(
            bool Success,
            TToken Token,
         in ReadOnlySequence<TSequence> Segment)
    {
        public static readonly TokenParsingResult Failure = new();
    }
}

public partial record Grammar<TSequence, TToken> : IEnumerable<Grammar<TSequence, TToken>.TokenParserDelegate>
{
    public void Add(TokenParserDelegate parseTokenDelegate)
        => _tokenParserDelegates.Add(parseTokenDelegate);

    public void Add(ITokenParser parser)
        => _tokenParserDelegates.Add(parser.Parse);

    public IEnumerator<Grammar<TSequence, TToken>.TokenParserDelegate> GetEnumerator()
        => _tokenParserDelegates.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => _tokenParserDelegates.GetEnumerator();

    public record TokenParser(
        in Grammar<TSequence, TToken> Grammar,
        in ReadOnlySequence<TSequence> Sequence) : IEnumerator<TokenParsingResult>, IEnumerable<TokenParsingResult>
    {
        private ReadOnlySequence<TSequence> _segment = Sequence;

        public bool IsDone => _segment.IsEmpty;

        public TokenParsingResult Current { get; private set; } = TokenParsingResult.Failure;

        object IEnumerator.Current => Current;

        public bool MoveNext() {
            if (_segment.IsEmpty)
                return false;

            Current = Grammar.Parse(ref _segment);

            return Current.Success;
        }

        public void Reset() {
            _segment = Sequence;
            Current = default;
        }

        public void Dispose() {
            _segment = default;
            Current = TokenParsingResult.Failure;

            GC.SuppressFinalize(this);
        }

        public IEnumerator<TokenParsingResult> GetEnumerator() => this;

        IEnumerator IEnumerable.GetEnumerator() => this;
    }
}