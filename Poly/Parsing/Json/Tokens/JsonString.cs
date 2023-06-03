using System.Buffers;

namespace Poly.Parsing.Json;


public record JsonStringParser
    : Grammar<char, JsonTokenType>.ITokenParser 
{    
    const char QuotationMark = '"';
    const char ReverseSolidus = '\\';
    
    public Grammar<char, JsonTokenType>.TokenParsingResult Parse(
        ref ReadOnlySequence<char> sequence)
    {
        var reader = new SequenceReader<char>(sequence);

        if (!reader.IsNext(QuotationMark, advancePast: true)) {
            return Grammar<char, JsonTokenType>.TokenParsingResult.Failure;
        }

        if (!reader.TryReadTo(out ReadOnlySequence<char> text, QuotationMark, ReverseSolidus, advancePastDelimiter: true)) {
            return Grammar<char, JsonTokenType>.TokenParsingResult.Failure;
        }

        sequence = sequence.Slice(reader.Position);
        
        return new Grammar<char, JsonTokenType>.TokenParsingResult(
            Success: true,
            Token: JsonTokenType.String,
            Segment: text
        );
    }
}