namespace Poly.Parsing.Json;

public enum JsonToken
{
    Invalid,
    BeginObject,
    EndObject,
    BeginArray,
    EndArray,
    NameSeperator,
    MemberSeperator,
    NullValue,
    TrueValue,
    FalseValue,
    Comment,
    NewLine,
    Whitespace,
    String,
    Number
}

public static class JsonGrammar {
    public static readonly Grammar<char, JsonToken> Definition = new()
    {
        new JsonStringParser(),
        new StaticTokenParser<char, JsonToken>(JsonToken.NameSeperator, ':'),
        new StaticTokenParser<char, JsonToken>(JsonToken.MemberSeperator, ','),
        new StaticTokenParser<char, JsonToken>(JsonToken.BeginObject, '{'),
        new StaticTokenParser<char, JsonToken>(JsonToken.EndObject, '}'),
        new StaticTokenParser<char, JsonToken>(JsonToken.BeginArray, '['),
        new StaticTokenParser<char, JsonToken>(JsonToken.EndArray, ']'),
        new StaticTokenParser<char, JsonToken>(JsonToken.NullValue, "null".ToCharArray()),
        new StaticTokenParser<char, JsonToken>(JsonToken.TrueValue, "true".ToCharArray()),
        new StaticTokenParser<char, JsonToken>(JsonToken.FalseValue, "false".ToCharArray()),
        new JsonCommentParser(),
        new VariantTokenParser<char, JsonToken>(JsonToken.NewLine, '\r', '\n'),
        new VariantTokenParser<char, JsonToken>(JsonToken.Whitespace, ' ', '\t'),
        new JsonNumberParser()
    };
}