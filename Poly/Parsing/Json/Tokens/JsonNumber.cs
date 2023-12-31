using System.Buffers;

namespace Poly.Parsing.Json;

public record JsonNumberParser
    : Grammar<char, JsonToken>.ITokenParser 
{    
    const char Negative = '-';
    const char Positive = '+';
    const char Period = '.';
    const char Exponent = 'e';
    const char ExponentUpper = 'E';

    static readonly char[] ValidCharacters = "0123456789".ToCharArray();
    
    public Grammar<char, JsonToken>.TokenParsingResult Parse(
        ref ReadOnlySequence<char> sequence)
    {
        
        var reader = new SequenceReader<char>(sequence);
        var begin = reader.Position;

        reader.IsNext(Negative, advancePast: true);
        
        if (reader.AdvancePastAny(ValidCharacters) == 0)
        {
            return Grammar<char, JsonToken>.TokenParsingResult.Failure;
        }

        if (reader.IsNext(Period, advancePast: true))
        {
            if (reader.AdvancePastAny(ValidCharacters) == 0)
            {
                return Grammar<char, JsonToken>.TokenParsingResult.Failure;
            }
        }

        if (reader.IsNext(Exponent, advancePast: true) ||
            reader.IsNext(ExponentUpper, advancePast: true))
        {
            reader.IsNext(Negative, advancePast: true);
            reader.IsNext(Positive, advancePast: true);
        
            if (reader.AdvancePastAny(ValidCharacters) == 0)
            {
                return Grammar<char, JsonToken>.TokenParsingResult.Failure;
            }
        }
        
        var end = reader.Position;

        var num = sequence.Slice(begin, end);

        sequence = sequence.Slice(end);

        return new Grammar<char, JsonToken>.TokenParsingResult(
            Success: true,
            Token: JsonToken.Number,
            Segment: num
        );
    }
}