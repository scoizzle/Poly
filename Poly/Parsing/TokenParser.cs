#pragma warning disable IDE1006 // Naming rule violation: Missing prefix: 'I'

using System.Buffers;

namespace Poly.Parsing;

public record StaticTokenParser<TSequence, TToken>  : Grammar<TSequence, TToken>.ITokenParser where TSequence : unmanaged, IEquatable<TSequence> 
{
    public TToken Token { get; init; }
    public TSequence[] Parts { get; init; }

    public StaticTokenParser(
        TToken token,
        params TSequence[] parts)
    {
        Token = token;
        Parts = parts;
    }

    public Grammar<TSequence, TToken>.TokenParsingResult Parse(
        ref ReadOnlySequence<TSequence> sequence)
    {
        if (sequence.FirstSpan.StartsWith(Parts))
        {
            var begin = sequence.Start;
            var end = sequence.GetPosition(Parts.Length);
            var segment = sequence.Slice(begin, end);

            sequence = sequence.Slice(end);

            return new Grammar<TSequence, TToken>.TokenParsingResult(
                Success: true,
                Token: Token,
                Segment: segment
            );
        }

        return Grammar<TSequence, TToken>.TokenParsingResult.Failure;
    }
}

public record VariantTokenParser<TSequence, TToken> 
    : Grammar<TSequence, TToken>.ITokenParser 
        where TSequence : unmanaged, IEquatable<TSequence> 
{
    public TToken Token { get; init; }
    public TSequence[] Options { get; init; }

    public VariantTokenParser(
        TToken token,
        params TSequence[] options)
    {
        Token = token;
        Options = options;
    }

    public Grammar<TSequence, TToken>.TokenParsingResult Parse(
        ref ReadOnlySequence<TSequence> sequence)
    {
        var reader = new SequenceReader<TSequence>(sequence);
        var begin = reader.Position;
        var count = reader.AdvancePastAny(Options);
        var end = reader.Position;

        var segment = sequence.Slice(begin, end);

        if (count == 0)
            return Grammar<TSequence, TToken>.TokenParsingResult.Failure;

        sequence = sequence.Slice(end);

        return new Grammar<TSequence, TToken>.TokenParsingResult(
            Success: true,
            Token: Token,
            Segment: segment
        );
    }
}