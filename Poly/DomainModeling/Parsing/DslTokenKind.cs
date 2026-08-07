namespace Poly.DomainModeling.Parsing;

/// <summary>
/// Token kinds for the Poly DSL scanner. Mirrors the original
/// <see cref="PolyDslTokenizer"/>'s <c>TokenKind</c> exactly — the DSL surface
/// is unchanged; only the scanner host moved onto the grammar engine.
/// </summary>
public enum DslTokenKind {
    EndOfFile,
    Identifier,
    Number,
    StringLiteral,
    Colon,
    Comma,
    Dot,
    LParen,
    RParen,
    LBrace,
    RBrace,
    LBracket,
    RBracket,
    Arrow,          // ->
    Gt,             // >
    Gte,            // >=
    Lt,             // <
    Lte,            // <=
    Eq,             // ==
    Neq,            // !=
    Plus,           // +
    Minus,          // -
    Star,           // *
    Slash,          // /
    Is,
    Not,
    And,
    Or,
    Assign,
    To,
    Transition,
    When,
    Require,
    If,
    Else,
    Domain,
    Entity,
    Stage,
    Action,
    Policy,
    True,
    False,
    Null,
    Text,
    NumberType,
    BooleanType,
    DateTimeType,
    DateType,
    Required,
    Unique,
    Range,
    Length,
    Pattern,
    Enum,
    Equals,
    Relationship,
    From,
    One,
    Many,
    Owned,
    Create,
    In,
    Invoke,
    Entry,
    Exit,
    Delete,
    As,
}