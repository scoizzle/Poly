namespace Poly.Interpretation.CSharp;

/// <summary>
/// Token kinds for the C# language surface. Keywords are individual kinds (the
/// language has a fixed keyword set); identifiers and literals are value kinds.
/// Nothing here is domain-specific — the table describes the C# subset the
/// exporter prints and reads back.
///
/// Shift operators are <em>not</em> kinds: the tokenizer emits single
/// <see cref="CSharpTokenKind.Lt"/> / <see cref="CSharpTokenKind.Gt"/> tokens so
/// nested type arguments (<c>Dictionary&lt;string, List&lt;Book&gt;&gt;</c>) close cleanly,
/// and the grammar's shift rules match the adjacent pair.
/// </summary>
public enum CSharpTokenKind {
    EndOfFile,

    // Value-bearing kinds.
    Identifier,
    NumberLiteral,
    StringLiteral,
    CharLiteral,
    /// <summary>A plain interpolated string <c>$"…"</c>. Its text is the raw body
    /// between the quotes (escapes and <c>{…}</c> holes undecoded); the reader
    /// splits it into literals and holes.</summary>
    InterpolatedStringLiteral,

    // Declaration nouns.
    KwUsing,
    KwNamespace,
    KwClass,
    KwRecord,
    KwInterface,
    KwEnum,

    // Modifiers.
    KwPublic,
    KwPrivate,
    KwProtected,
    KwInternal,
    KwStatic,
    KwConst,
    KwReadonly,
    KwVolatile,
    KwOverride,
    KwAsync,

    // Statement / expression keywords.
    KwAwait,
    KwNew,
    KwThis,
    KwBase,
    KwReturn,
    KwThrow,
    KwIf,
    KwElse,
    KwWhile,
    KwDo,
    KwFor,
    KwForeach,
    KwIn,
    KwSwitch,
    KwCase,
    KwDefault,
    KwTry,
    KwCatch,
    KwFinally,
    KwGoto,
    KwBreak,
    KwContinue,
    KwGet,
    KwSet,
    KwInit,
    KwVar,
    KwVoid,
    KwTypeof,
    KwIs,
    KwAs,
    KwNull,
    KwTrue,
    KwFalse,

    // Primitive type keywords.
    KwBool,
    KwByte,
    KwSbyte,
    KwShort,
    KwUshort,
    KwInt,
    KwUint,
    KwLong,
    KwUlong,
    KwFloat,
    KwDouble,
    KwDecimal,
    KwChar,
    KwString,
    KwObject,

    // Delimiters / separators.
    LBrace,
    RBrace,
    LParen,
    RParen,
    LBracket,
    RBracket,
    Semicolon,
    Comma,
    Dot,
    Colon,
    Question,

    // Operators.
    Assign,
    PlusAssign,
    MinusAssign,
    StarAssign,
    SlashAssign,
    PercentAssign,
    AmpAssign,
    PipeAssign,
    CaretAssign,
    Arrow,
    Plus,
    Minus,
    Star,
    Slash,
    Percent,
    Amp,
    AmpAmp,
    Pipe,
    PipePipe,
    Caret,
    Tilde,
    Bang,
    EqEq,
    NotEq,
    Lt,
    LtEq,
    Gt,
    GtEq,
    QuestionQuestion,
    QuestionDot,
}