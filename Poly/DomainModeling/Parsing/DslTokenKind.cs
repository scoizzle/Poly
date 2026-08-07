namespace Poly.DomainModeling.Parsing;

/// <summary>
/// Token kinds for the product Poly DSL scanner (<see cref="DslTokenReader"/>).
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