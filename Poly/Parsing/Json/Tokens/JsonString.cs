using System.Buffers;

namespace Poly.Parsing.Json;


public record JsonStringParser
    : Grammar<char, JsonToken>.ITokenParser 
{    
    const char QuotationMark = '"';
    const char ReverseSolidus = '\\';
    
    public Grammar<char, JsonToken>.TokenParsingResult Parse(
        ref ReadOnlySequence<char> sequence)
    {
        var reader = new SequenceReader<char>(sequence);

        if (!reader.IsNext(QuotationMark, advancePast: true)) {
            return Grammar<char, JsonToken>.TokenParsingResult.Failure;
        }

        if (!reader.TryReadTo(out ReadOnlySequence<char> text, QuotationMark, ReverseSolidus, advancePastDelimiter: true)) {
            return Grammar<char, JsonToken>.TokenParsingResult.Failure;
        }

        sequence = sequence.Slice(reader.Position);
        
        return new Grammar<char, JsonToken>.TokenParsingResult(
            Success: true,
            Token: JsonToken.String,
            Segment: text
        );
    }
}