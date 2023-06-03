#nullable enable

namespace Poly.Parsing; 

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