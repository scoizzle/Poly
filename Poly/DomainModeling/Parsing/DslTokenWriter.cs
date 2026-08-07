using System.Text;

using Poly.Grammar;

namespace Poly.DomainModeling.Parsing;

/// <summary>
/// GI-6: product DSL <see cref="TokenWriter{TKind}"/> with canonical keyword/punct text.
/// <see cref="DomainDslPrinter"/> still walks the domain graph; this writer is the
/// emit surface for grammar <see cref="Printer{TKind}"/> and future printer ports.
/// </summary>
public sealed class DslTokenWriter : TokenWriter<DslTokenKind> {
    private readonly StringBuilder _sb = new();

    protected override void WriteCore(string text) => _sb.Append(text);

    public override string GetOutput() => _sb.ToString();

    public void Clear() => _sb.Clear();

    public override string CanonicalText(DslTokenKind kind) => kind switch {
        DslTokenKind.Domain => "domain",
        DslTokenKind.Entity => "entity",
        DslTokenKind.Stage => "stage",
        DslTokenKind.Action => "action",
        DslTokenKind.Policy => "policy",
        DslTokenKind.When => "when",
        DslTokenKind.Require => "require",
        DslTokenKind.If => "if",
        DslTokenKind.Else => "else",
        DslTokenKind.True => "true",
        DslTokenKind.False => "false",
        DslTokenKind.Null => "null",
        DslTokenKind.Text => "Text",
        DslTokenKind.NumberType => "Number",
        DslTokenKind.BooleanType => "Boolean",
        DslTokenKind.DateTimeType => "DateTime",
        DslTokenKind.DateType => "Date",
        DslTokenKind.Required => "required",
        DslTokenKind.Unique => "unique",
        DslTokenKind.Range => "range",
        DslTokenKind.Length => "length",
        DslTokenKind.Pattern => "pattern",
        DslTokenKind.Enum => "enum",
        DslTokenKind.Equals => "equals",
        DslTokenKind.One => "one",
        DslTokenKind.Many => "many",
        DslTokenKind.Owned => "owned",
        DslTokenKind.Create => "create",
        DslTokenKind.In => "in",
        DslTokenKind.Invoke => "invoke",
        DslTokenKind.Entry => "entry",
        DslTokenKind.Exit => "exit",
        DslTokenKind.Delete => "delete",
        DslTokenKind.As => "as",
        DslTokenKind.Is => "is",
        DslTokenKind.Not => "not",
        DslTokenKind.And => "and",
        DslTokenKind.Or => "or",
        DslTokenKind.Assign => "assign",
        DslTokenKind.To => "to",
        DslTokenKind.Transition => "transition",
        DslTokenKind.From => "from",
        DslTokenKind.Relationship => "relationship",
        DslTokenKind.Colon => ":",
        DslTokenKind.Comma => ",",
        DslTokenKind.Dot => ".",
        DslTokenKind.LParen => "(",
        DslTokenKind.RParen => ")",
        DslTokenKind.LBrace => "{",
        DslTokenKind.RBrace => "}",
        DslTokenKind.LBracket => "[",
        DslTokenKind.RBracket => "]",
        DslTokenKind.Arrow => "->",
        DslTokenKind.Gt => ">",
        DslTokenKind.Gte => ">=",
        DslTokenKind.Lt => "<",
        DslTokenKind.Lte => "<=",
        DslTokenKind.Eq => "==",
        DslTokenKind.Neq => "!=",
        DslTokenKind.Plus => "+",
        DslTokenKind.Minus => "-",
        DslTokenKind.Star => "*",
        DslTokenKind.Slash => "/",
        _ => kind.ToString()!.ToLowerInvariant(),
    };

    public override void WriteValue(DslTokenKind kind, string value) {
        if (kind == DslTokenKind.StringLiteral) {
            WriteCore("\"");
            WriteCore(DomainDslPrinter.EscapeStringLiteral(value));
            WriteCore("\"");
            return;
        }
        WriteCore(value);
    }
}