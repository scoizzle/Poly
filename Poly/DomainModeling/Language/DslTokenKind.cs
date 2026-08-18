using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;
using Poly.DomainModeling.Runtime;

using Action = Poly.DomainModeling.Ontology.Action;
using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using PrimitiveType = Poly.DomainModeling.Ontology.PrimitiveType;
using Subtract = Poly.DomainModeling.Ontology.Subtract;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Language;

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
    Uses,
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
    For,
    Entry,
    Exit,
    As,
}