namespace Poly.Tests.Interpretation.C99Repl;

public enum TokenType
{
    // Literals
    IntLiteral,
    FloatLiteral,
    StringLiteral,
    Identifier,

    // Keywords
    Int,
    Float,
    If,
    Else,
    Return,
    While,
    For,
    Do,
    Break,
    Continue,

    // Operators
    Plus,
    Minus,
    Star,
    Slash,
    Percent,
    Assign,
    PlusAssign,
    MinusAssign,
    StarAssign,
    SlashAssign,
    Increment,
    Decrement,
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    And,
    Or,
    Not,
    BitwiseAnd,
    BitwiseOr,
    BitwiseXor,
    BitwiseNot,
    LeftShift,
    RightShift,
    Question,
    Colon,

    // Delimiters
    LeftParen,
    RightParen,
    LeftBrace,
    RightBrace,
    LeftBracket,
    RightBracket,
    Semicolon,
    Comma,
    Dot,
    Arrow,

    // Special
    EOF,
    Whitespace,
    Comment,
}

public sealed class C99Token
{
    public TokenType Type { get; }
    public string Value { get; }
    public int Line { get; }
    public int Column { get; }

    public C99Token(TokenType type, string value, int line, int column)
    {
        Type = type;
        Value = value;
        Line = line;
        Column = column;
    }

    public override string ToString() => $"{Type}({Value}) at {Line}:{Column}";
}
