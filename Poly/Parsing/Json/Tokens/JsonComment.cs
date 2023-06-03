using System.Buffers;

namespace Poly.Parsing.Json;

public record JsonCommentParser
    : Grammar<char, JsonTokenType>.ITokenParser 
{    
    const char NewLine = '\n';
    static readonly char[] DoubleSolidus = new[] { '/', '/' };
    
    public Grammar<char, JsonTokenType>.TokenParsingResult Parse(
        ref ReadOnlySequence<char> sequence)
    {
        var reader = new SequenceReader<char>(sequence);
        var begin = reader.Position;

        if (!reader.IsNext(DoubleSolidus, advancePast: true))
            return Grammar<char, JsonTokenType>.TokenParsingResult.Failure;

        if (!reader.TryAdvanceTo(NewLine, advancePastDelimiter: true))
            reader.AdvanceToEnd();
        
        var end = reader.Position;
        var segment = sequence.Slice(begin, end);
        
        sequence = sequence.Slice(end);

        return new Grammar<char, JsonTokenType>.TokenParsingResult(
            Success: true,
            Token: JsonTokenType.Comment,
            Segment: segment
        );
    }
}