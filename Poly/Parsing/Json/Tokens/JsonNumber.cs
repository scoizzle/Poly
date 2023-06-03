using System.Buffers;

namespace Poly.Parsing.Json;

public record JsonNumberParser
    : Grammar<char, JsonTokenType>.ITokenParser 
{    
    const char Negatative = '-';
    const char Positive = '+';
    const char Period = '.';
    const char Exponent = 'e';
    const char ExponentUpper = 'E';

    static readonly char[] ValidCharacters = "0123456789".ToCharArray();
    
    public Grammar<char, JsonTokenType>.TokenParsingResult Parse(
        ref ReadOnlySequence<char> sequence)
    {
        
        var reader = new SequenceReader<char>(sequence);
        var begin = reader.Position;

        reader.IsNext(Negatative, advancePast: true);
        
        if (reader.AdvancePastAny(ValidCharacters) == 0)
        {
            return Grammar<char, JsonTokenType>.TokenParsingResult.Failure;
        }

        if (reader.IsNext(Period, advancePast: true))
        {
            if (reader.AdvancePastAny(ValidCharacters) == 0)
            {
                return Grammar<char, JsonTokenType>.TokenParsingResult.Failure;
            }
        }

        if (reader.IsNext(Exponent, advancePast: true) ||
            reader.IsNext(ExponentUpper, advancePast: true))
        {
            reader.IsNext(Negatative, advancePast: true);
            reader.IsNext(Positive, advancePast: true);
        
            if (reader.AdvancePastAny(ValidCharacters) == 0)
            {
                return Grammar<char, JsonTokenType>.TokenParsingResult.Failure;
            }
        }
        
        var end = reader.Position;

        var num = sequence.Slice(begin, end);

        sequence = sequence.Slice(end);

        return new Grammar<char, JsonTokenType>.TokenParsingResult(
            Success: true,
            Token: JsonTokenType.Number,
            Segment: num
        );
    }
}