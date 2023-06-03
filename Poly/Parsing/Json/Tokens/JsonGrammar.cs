namespace Poly.Parsing.Json;

public enum JsonTokenType
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
    public static readonly Grammar<char, JsonTokenType> Definition = new()
    {
        new JsonStringParser(),
        new StaticTokenParser<char, JsonTokenType>(JsonTokenType.NameSeperator, ':'),
        new StaticTokenParser<char, JsonTokenType>(JsonTokenType.MemberSeperator, ','),
        new StaticTokenParser<char, JsonTokenType>(JsonTokenType.BeginObject, '{'),
        new StaticTokenParser<char, JsonTokenType>(JsonTokenType.EndObject, '}'),
        new StaticTokenParser<char, JsonTokenType>(JsonTokenType.BeginArray, '['),
        new StaticTokenParser<char, JsonTokenType>(JsonTokenType.EndArray, ']'),
        new StaticTokenParser<char, JsonTokenType>(JsonTokenType.NullValue, "null".ToCharArray()),
        new StaticTokenParser<char, JsonTokenType>(JsonTokenType.TrueValue, "true".ToCharArray()),
        new StaticTokenParser<char, JsonTokenType>(JsonTokenType.FalseValue, "false".ToCharArray()),
        new JsonCommentParser(),
        new VariantTokenParser<char, JsonTokenType>(JsonTokenType.NewLine, '\r', '\n'),
        new VariantTokenParser<char, JsonTokenType>(JsonTokenType.Whitespace, ' ', '\t'),
        new JsonNumberParser()
    };
}