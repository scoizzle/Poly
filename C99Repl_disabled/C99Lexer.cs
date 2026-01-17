namespace Poly.Tests.Interpretation.C99Repl;

/// <summary>
/// Lexer for C99 source code. Tokenizes C99 source into a stream of tokens.
/// </summary>
public sealed class C99Lexer
{
    private readonly string _source;
    private int _position;
    private int _line = 1;
    private int _column = 1;

    private static readonly Dictionary<string, TokenType> Keywords = new()
    {
        { "int", TokenType.Int },
        { "float", TokenType.Float },
        { "if", TokenType.If },
        { "else", TokenType.Else },
        { "return", TokenType.Return },
        { "while", TokenType.While },
        { "for", TokenType.For },
        { "do", TokenType.Do },
        { "break", TokenType.Break },
        { "continue", TokenType.Continue },
    };

    public C99Lexer(string source)
    {
        _source = source ?? string.Empty;
    }

    public List<C99Token> Tokenize()
    {
        var tokens = new List<C99Token>();
        while (_position < _source.Length)
        {
            SkipWhitespaceAndComments();
            if (_position >= _source.Length) break;

            var token = NextToken();
            if (token.Type != TokenType.Whitespace && token.Type != TokenType.Comment)
            {
                tokens.Add(token);
            }
        }
        tokens.Add(new C99Token(TokenType.EOF, "", _line, _column));
        return tokens;
    }

    private C99Token NextToken()
    {
        var ch = Current();
        var line = _line;
        var column = _column;

        // Two-character operators
        if (_position + 1 < _source.Length)
        {
            var twoChar = _source[_position..(_position + 2)];
            var tokenType = twoChar switch
            {
                "==" => TokenType.Equal,
                "!=" => TokenType.NotEqual,
                "<=" => TokenType.LessThanOrEqual,
                ">=" => TokenType.GreaterThanOrEqual,
                "&&" => TokenType.And,
                "||" => TokenType.Or,
                "++" => TokenType.Increment,
                "--" => TokenType.Decrement,
                "+=" => TokenType.PlusAssign,
                "-=" => TokenType.MinusAssign,
                "*=" => TokenType.StarAssign,
                "/=" => TokenType.SlashAssign,
                "<<" => TokenType.LeftShift,
                ">>" => TokenType.RightShift,
                "->" => TokenType.Arrow,
                _ => (TokenType?)null
            };
            if (tokenType.HasValue)
            {
                Advance();
                Advance();
                return new C99Token(tokenType.Value, twoChar, line, column);
            }
        }

        // Single-character tokens
        var singleChar = ch switch
        {
            '+' => TokenType.Plus,
            '-' => TokenType.Minus,
            '*' => TokenType.Star,
            '/' => TokenType.Slash,
            '%' => TokenType.Percent,
            '=' => TokenType.Assign,
            '<' => TokenType.LessThan,
            '>' => TokenType.GreaterThan,
            '!' => TokenType.Not,
            '&' => TokenType.BitwiseAnd,
            '|' => TokenType.BitwiseOr,
            '^' => TokenType.BitwiseXor,
            '~' => TokenType.BitwiseNot,
            '?' => TokenType.Question,
            ':' => TokenType.Colon,
            '(' => TokenType.LeftParen,
            ')' => TokenType.RightParen,
            '{' => TokenType.LeftBrace,
            '}' => TokenType.RightBrace,
            '[' => TokenType.LeftBracket,
            ']' => TokenType.RightBracket,
            ';' => TokenType.Semicolon,
            ',' => TokenType.Comma,
            '.' => TokenType.Dot,
            _ => (TokenType?)null
        };

        if (singleChar.HasValue)
        {
            Advance();
            return new C99Token(singleChar.Value, ch.ToString(), line, column);
        }

        // String literals
        if (ch == '"')
        {
            return ReadStringLiteral();
        }

        // Numbers
        if (char.IsDigit(ch) || (ch == '-' && _position + 1 < _source.Length && char.IsDigit(_source[_position + 1])))
        {
            return ReadNumber();
        }

        // Identifiers and keywords
        if (char.IsLetter(ch) || ch == '_')
        {
            return ReadIdentifier();
        }

        throw new InvalidOperationException($"Unexpected character '{ch}' at {line}:{column}");
    }

    private C99Token ReadStringLiteral()
    {
        var line = _line;
        var column = _column;
        var value = "";

        Advance(); // Skip opening quote
        while (_position < _source.Length && Current() != '"')
        {
            if (Current() == '\\' && _position + 1 < _source.Length)
            {
                Advance();
                value += Current() switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '\\' => '\\',
                    '"' => '"',
                    _ => Current()
                };
                Advance();
            }
            else
            {
                value += Current();
                Advance();
            }
        }

        if (_position < _source.Length)
        {
            Advance(); // Skip closing quote
        }

        return new C99Token(TokenType.StringLiteral, value, line, column);
    }

    private C99Token ReadNumber()
    {
        var line = _line;
        var column = _column;
        var value = "";

        while (_position < _source.Length && (char.IsDigit(Current()) || Current() == '.'))
        {
            value += Current();
            Advance();
        }

        var type = value.Contains('.') ? TokenType.FloatLiteral : TokenType.IntLiteral;
        return new C99Token(type, value, line, column);
    }

    private C99Token ReadIdentifier()
    {
        var line = _line;
        var column = _column;
        var value = "";

        while (_position < _source.Length && (char.IsLetterOrDigit(Current()) || Current() == '_'))
        {
            value += Current();
            Advance();
        }

        var type = Keywords.TryGetValue(value, out var keywordType) ? keywordType : TokenType.Identifier;
        return new C99Token(type, value, line, column);
    }

    private void SkipWhitespaceAndComments()
    {
        while (_position < _source.Length)
        {
            if (char.IsWhiteSpace(Current()))
            {
                if (Current() == '\n')
                {
                    _line++;
                    _column = 1;
                }
                else
                {
                    _column++;
                }
                _position++;
            }
            else if (_position + 1 < _source.Length && _source[_position..(_position + 2)] == "//")
            {
                // Single-line comment
                while (_position < _source.Length && Current() != '\n')
                {
                    _position++;
                }
                if (_position < _source.Length && Current() == '\n')
                {
                    _line++;
                    _column = 1;
                    _position++;
                }
            }
            else if (_position + 1 < _source.Length && _source[_position..(_position + 2)] == "/*")
            {
                // Multi-line comment
                _position += 2;
                _column += 2;
                while (_position + 1 < _source.Length && _source[_position..(_position + 2)] != "*/")
                {
                    if (Current() == '\n')
                    {
                        _line++;
                        _column = 1;
                    }
                    else
                    {
                        _column++;
                    }
                    _position++;
                }
                if (_position + 1 < _source.Length)
                {
                    _position += 2;
                    _column += 2;
                }
            }
            else
            {
                break;
            }
        }
    }

    private char Current()
    {
        return _position < _source.Length ? _source[_position] : '\0';
    }

    private void Advance()
    {
        if (_position < _source.Length)
        {
            if (_source[_position] == '\n')
            {
                _line++;
                _column = 1;
            }
            else
            {
                _column++;
            }
            _position++;
        }
    }
}
