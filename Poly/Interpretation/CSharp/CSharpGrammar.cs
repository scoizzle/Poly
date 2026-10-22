using Poly.Grammar;

namespace Poly.Interpretation.CSharp;

/// <summary>
/// The C# language table: the read/write surface for the C# subset the project
/// emits. One <see cref="Grammar{TToken,TTokenKind}"/> drives both directions —
/// the <see cref="CSharpWriter"/> resolves an AST node to a rule + pattern and
/// then prints the pattern's fixed skeleton, and the <see cref="CSharpReader"/>
/// matches the same patterns and folds the result tree back to AST nodes.
///
/// Covered subset: file-scoped namespaces, using directives, attribute-annotated
/// class/record/interface/enum declarations with generic parameters, primary
/// constructors and lineage, fields, properties (auto, accessor-bodied,
/// initializer), methods (block, expression-bodied, abstract), constructors
/// (including <c>: base(...)</c>), and the statement/expression layer the
/// exporter prints (see <see cref="CSharpWriter"/> for the node set).
///
/// Shift operators are matched as adjacent <see cref="CSharpTokenKind.Lt"/> /
/// <see cref="CSharpTokenKind.Gt"/> pairs (the tokenizer never merges them), so
/// nested generic type arguments close cleanly.
/// </summary>
public static class CSharpGrammar {
    public static Grammar<CSharpToken, CSharpTokenKind> Table { get; } = Build();

    /// <summary>
    /// Canonical text for fixed kinds — the printer's inverse of the tokenizer
    /// for everything whose spelling is not content-bearing.
    /// </summary>
    public static string CanonicalText(CSharpTokenKind kind) => kind switch {
        CSharpTokenKind.KwUsing => "using",
        CSharpTokenKind.KwNamespace => "namespace",
        CSharpTokenKind.KwClass => "class",
        CSharpTokenKind.KwRecord => "record",
        CSharpTokenKind.KwInterface => "interface",
        CSharpTokenKind.KwEnum => "enum",
        CSharpTokenKind.KwPublic => "public",
        CSharpTokenKind.KwPrivate => "private",
        CSharpTokenKind.KwProtected => "protected",
        CSharpTokenKind.KwInternal => "internal",
        CSharpTokenKind.KwStatic => "static",
        CSharpTokenKind.KwConst => "const",
        CSharpTokenKind.KwReadonly => "readonly",
        CSharpTokenKind.KwVolatile => "volatile",
        CSharpTokenKind.KwOverride => "override",
        CSharpTokenKind.KwAsync => "async",
        CSharpTokenKind.KwAwait => "await",
        CSharpTokenKind.KwNew => "new",
        CSharpTokenKind.KwThis => "this",
        CSharpTokenKind.KwBase => "base",
        CSharpTokenKind.KwReturn => "return",
        CSharpTokenKind.KwThrow => "throw",
        CSharpTokenKind.KwIf => "if",
        CSharpTokenKind.KwElse => "else",
        CSharpTokenKind.KwWhile => "while",
        CSharpTokenKind.KwDo => "do",
        CSharpTokenKind.KwFor => "for",
        CSharpTokenKind.KwForeach => "foreach",
        CSharpTokenKind.KwIn => "in",
        CSharpTokenKind.KwSwitch => "switch",
        CSharpTokenKind.KwCase => "case",
        CSharpTokenKind.KwDefault => "default",
        CSharpTokenKind.KwTry => "try",
        CSharpTokenKind.KwCatch => "catch",
        CSharpTokenKind.KwFinally => "finally",
        CSharpTokenKind.KwGoto => "goto",
        CSharpTokenKind.KwBreak => "break",
        CSharpTokenKind.KwContinue => "continue",
        CSharpTokenKind.KwGet => "get",
        CSharpTokenKind.KwSet => "set",
        CSharpTokenKind.KwInit => "init",
        CSharpTokenKind.KwVar => "var",
        CSharpTokenKind.KwVoid => "void",
        CSharpTokenKind.KwTypeof => "typeof",
        CSharpTokenKind.KwIs => "is",
        CSharpTokenKind.KwAs => "as",
        CSharpTokenKind.KwNull => "null",
        CSharpTokenKind.KwTrue => "true",
        CSharpTokenKind.KwFalse => "false",
        CSharpTokenKind.KwBool => "bool",
        CSharpTokenKind.KwByte => "byte",
        CSharpTokenKind.KwSbyte => "sbyte",
        CSharpTokenKind.KwShort => "short",
        CSharpTokenKind.KwUshort => "ushort",
        CSharpTokenKind.KwInt => "int",
        CSharpTokenKind.KwUint => "uint",
        CSharpTokenKind.KwLong => "long",
        CSharpTokenKind.KwUlong => "ulong",
        CSharpTokenKind.KwFloat => "float",
        CSharpTokenKind.KwDouble => "double",
        CSharpTokenKind.KwDecimal => "decimal",
        CSharpTokenKind.KwChar => "char",
        CSharpTokenKind.KwString => "string",
        CSharpTokenKind.KwObject => "object",
        CSharpTokenKind.LBrace => "{",
        CSharpTokenKind.RBrace => "}",
        CSharpTokenKind.LParen => "(",
        CSharpTokenKind.RParen => ")",
        CSharpTokenKind.LBracket => "[",
        CSharpTokenKind.RBracket => "]",
        CSharpTokenKind.Semicolon => ";",
        CSharpTokenKind.Comma => ",",
        CSharpTokenKind.Dot => ".",
        CSharpTokenKind.Colon => ":",
        CSharpTokenKind.Question => "?",
        CSharpTokenKind.Assign => "=",
        CSharpTokenKind.PlusAssign => "+=",
        CSharpTokenKind.MinusAssign => "-=",
        CSharpTokenKind.StarAssign => "*=",
        CSharpTokenKind.SlashAssign => "/=",
        CSharpTokenKind.PercentAssign => "%=",
        CSharpTokenKind.AmpAssign => "&=",
        CSharpTokenKind.PipeAssign => "|=",
        CSharpTokenKind.CaretAssign => "^=",
        CSharpTokenKind.QuestionQuestionAssign => "??=",
        CSharpTokenKind.LtLtAssign => "<<=",
        CSharpTokenKind.Arrow => "=>",
        CSharpTokenKind.Plus => "+",
        CSharpTokenKind.Minus => "-",
        CSharpTokenKind.Star => "*",
        CSharpTokenKind.Slash => "/",
        CSharpTokenKind.Percent => "%",
        CSharpTokenKind.Amp => "&",
        CSharpTokenKind.AmpAmp => "&&",
        CSharpTokenKind.Pipe => "|",
        CSharpTokenKind.PipePipe => "||",
        CSharpTokenKind.Caret => "^",
        CSharpTokenKind.Tilde => "~",
        CSharpTokenKind.Bang => "!",
        CSharpTokenKind.EqEq => "==",
        CSharpTokenKind.NotEq => "!=",
        CSharpTokenKind.Lt => "<",
        CSharpTokenKind.LtEq => "<=",
        CSharpTokenKind.Gt => ">",
        CSharpTokenKind.GtEq => ">=",
        CSharpTokenKind.QuestionQuestion => "??",
        CSharpTokenKind.QuestionDot => "?.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No canonical text for content-bearing kind."),
    };

    private static Ref<CSharpToken, CSharpTokenKind> R(string rule) => new(rule);
    private static Value<CSharpToken, CSharpTokenKind> V(CSharpTokenKind kind, string name) => new(kind, name);

    private static Grammar<CSharpToken, CSharpTokenKind> Build() {
        var b = new GrammarBuilder<CSharpToken, CSharpTokenKind>();

        // ── names ────────────────────────────────────────────────────────────
        b.Define("name")
            .Pattern("simple").Value(CSharpTokenKind.Identifier, "name").Commit()
            .Pattern("qualified").Value(CSharpTokenKind.Identifier, "head").Kind(CSharpTokenKind.Dot).Ref("name").Commit();

        // ── using / namespace ────────────────────────────────────────────────
        b.Define("using")
            .Pattern("directive").Kind(CSharpTokenKind.KwUsing).Ref("name").Kind(CSharpTokenKind.Semicolon).Commit();
        b.Define("namespace")
            .Pattern("file-scoped").Kind(CSharpTokenKind.KwNamespace).Ref("name").Kind(CSharpTokenKind.Semicolon).Commit();

        // ── attributes ───────────────────────────────────────────────────────
        b.Define("attributes")
            .Pattern("list").Repeat("attribute", min: 1).Commit();
        b.Define("attribute")
            .Pattern("bare").Value(CSharpTokenKind.LBracket, "open").Ref("name").Value(CSharpTokenKind.RBracket, "close").Commit()
            .Pattern("args").Value(CSharpTokenKind.LBracket, "open").Ref("name").Ref("args").Value(CSharpTokenKind.RBracket, "close").Commit();

        // ── modifiers ────────────────────────────────────────────────────────
        b.Define("modifier")
            .Pattern("public").Kind(CSharpTokenKind.KwPublic).Commit()
            .Pattern("private").Kind(CSharpTokenKind.KwPrivate).Commit()
            .Pattern("protected").Kind(CSharpTokenKind.KwProtected).Commit()
            .Pattern("internal").Kind(CSharpTokenKind.KwInternal).Commit()
            .Pattern("static").Kind(CSharpTokenKind.KwStatic).Commit()
            .Pattern("const").Kind(CSharpTokenKind.KwConst).Commit()
            .Pattern("readonly").Kind(CSharpTokenKind.KwReadonly).Commit()
            .Pattern("volatile").Kind(CSharpTokenKind.KwVolatile).Commit()
            .Pattern("override").Kind(CSharpTokenKind.KwOverride).Commit()
            .Pattern("async").Kind(CSharpTokenKind.KwAsync).Commit();

        // ── types (read surface; the writer renders types through these too) ──
        b.Define("type")
            .Pattern("core").Ref("type-core").Repeat("type-suffix").Commit();
        b.Define("type-core")
            .Pattern("void").Kind(CSharpTokenKind.KwVoid).Commit()
            .Pattern("bool").Kind(CSharpTokenKind.KwBool).Commit()
            .Pattern("byte").Kind(CSharpTokenKind.KwByte).Commit()
            .Pattern("sbyte").Kind(CSharpTokenKind.KwSbyte).Commit()
            .Pattern("short").Kind(CSharpTokenKind.KwShort).Commit()
            .Pattern("ushort").Kind(CSharpTokenKind.KwUshort).Commit()
            .Pattern("int").Kind(CSharpTokenKind.KwInt).Commit()
            .Pattern("uint").Kind(CSharpTokenKind.KwUint).Commit()
            .Pattern("long").Kind(CSharpTokenKind.KwLong).Commit()
            .Pattern("ulong").Kind(CSharpTokenKind.KwUlong).Commit()
            .Pattern("float").Kind(CSharpTokenKind.KwFloat).Commit()
            .Pattern("double").Kind(CSharpTokenKind.KwDouble).Commit()
            .Pattern("decimal").Kind(CSharpTokenKind.KwDecimal).Commit()
            .Pattern("char").Kind(CSharpTokenKind.KwChar).Commit()
            .Pattern("string").Kind(CSharpTokenKind.KwString).Commit()
            .Pattern("object").Kind(CSharpTokenKind.KwObject).Commit()
            .Pattern("named").Ref("name").Optional(R("type-args")).Commit();
        b.Define("type-suffix")
            .Pattern("optional").Kind(CSharpTokenKind.Question).Commit()
            .Pattern("array").Value(CSharpTokenKind.LBracket, "open").Value(CSharpTokenKind.RBracket, "close").Commit();
        b.Define("type-list")
            .Pattern("list").Ref("type").Repeat("type-tail").Commit();
        b.Define("type-tail")
            .Pattern("tail").Kind(CSharpTokenKind.Comma).Ref("type").Commit();

        // ── type declarations ────────────────────────────────────────────────
        b.Define("type-decl")
            .Pattern("class")
                .Optional(R("attributes")).Optional(R("modifiers")).Kind(CSharpTokenKind.KwClass)
                .Value(CSharpTokenKind.Identifier, "name").Optional(R("generic-params")).Optional(R("lineage")).Ref("type-body").Commit()
            .Pattern("record")
                .Optional(R("attributes")).Optional(R("modifiers")).Kind(CSharpTokenKind.KwRecord)
                .Value(CSharpTokenKind.Identifier, "name").Optional(R("generic-params")).Optional(R("params")).Optional(R("lineage")).Ref("type-body").Commit()
            .Pattern("record-semi")
                .Optional(R("attributes")).Optional(R("modifiers")).Kind(CSharpTokenKind.KwRecord)
                .Value(CSharpTokenKind.Identifier, "name").Optional(R("generic-params")).Optional(R("params")).Optional(R("lineage")).Kind(CSharpTokenKind.Semicolon).Commit()
            .Pattern("interface")
                .Optional(R("attributes")).Optional(R("modifiers")).Kind(CSharpTokenKind.KwInterface)
                .Value(CSharpTokenKind.Identifier, "name").Optional(R("generic-params")).Optional(R("lineage")).Ref("type-body").Commit()
            .Pattern("enum")
                .Optional(R("attributes")).Optional(R("modifiers")).Kind(CSharpTokenKind.KwEnum)
                .Value(CSharpTokenKind.Identifier, "name").Ref("enum-body").Commit();
        b.Define("modifiers")
            .Pattern("list").Repeat("modifier", min: 1).Commit();
        b.Define("generic-params")
            .Pattern("list").Value(CSharpTokenKind.Lt, "open").Repeat("generic-param", min: 1).Value(CSharpTokenKind.Gt, "close").Commit();
        b.Define("generic-param")
            .Pattern("param").Value(CSharpTokenKind.Identifier, "name").Commit();
        b.Define("lineage")
            .Pattern("list").Kind(CSharpTokenKind.Colon).Ref("type").Repeat("lineage-tail").Commit();
        b.Define("lineage-tail")
            .Pattern("tail").Kind(CSharpTokenKind.Comma).Ref("type").Commit();
        b.Define("type-body")
            .Pattern("body").Value(CSharpTokenKind.LBrace, "open").Repeat("member", min: 0).Value(CSharpTokenKind.RBrace, "close").Commit();
        b.Define("enum-body")
            .Pattern("body").Value(CSharpTokenKind.LBrace, "open").Ref("enum-member").Repeat("enum-member-tail").Value(CSharpTokenKind.RBrace, "close").Commit();
        b.Define("enum-member")
            .Pattern("member").Value(CSharpTokenKind.Identifier, "name").Kind(CSharpTokenKind.Assign).Value(CSharpTokenKind.NumberLiteral, "value").Commit();
        b.Define("enum-member-tail")
            .Pattern("tail").Kind(CSharpTokenKind.Comma).Ref("enum-member").Commit();

        // ── members ──────────────────────────────────────────────────────────
        b.Define("member")
            .Pattern("field")
                .Optional(R("attributes")).Optional(R("modifiers")).Ref("type").Value(CSharpTokenKind.Identifier, "name")
                .Optional(R("field-init")).Kind(CSharpTokenKind.Semicolon).Commit()
            .Pattern("property")
                .Optional(R("attributes")).Optional(R("modifiers")).Ref("type").Value(CSharpTokenKind.Identifier, "name")
                .Ref("property-body").Optional(R("property-init")).Commit()
            .Pattern("method")
                .Optional(R("attributes")).Optional(R("modifiers")).Ref("type").Value(CSharpTokenKind.Identifier, "name")
                .Optional(R("generic-params")).Ref("params").Ref("method-body").Commit()
            .Pattern("ctor")
                .Optional(R("attributes")).Optional(R("modifiers")).Value(CSharpTokenKind.Identifier, "name").Ref("params")
                .Optional(R("ctor-base")).Ref("ctor-body").Commit();
        b.Define("field-init")
            .Pattern("init").Kind(CSharpTokenKind.Assign).Ref("expr").Commit();
        b.Define("property-body")
            .Pattern("body").Value(CSharpTokenKind.LBrace, "open").Repeat("accessor", min: 1).Value(CSharpTokenKind.RBrace, "close").Commit();
        b.Define("property-init")
            .Pattern("init").Kind(CSharpTokenKind.Assign).Ref("expr").Kind(CSharpTokenKind.Semicolon).Commit();
        b.Define("accessor")
            .Pattern("get-semi").Kind(CSharpTokenKind.KwGet).Kind(CSharpTokenKind.Semicolon).Commit()
            .Pattern("set-semi").Kind(CSharpTokenKind.KwSet).Kind(CSharpTokenKind.Semicolon).Commit()
            .Pattern("init-semi").Kind(CSharpTokenKind.KwInit).Kind(CSharpTokenKind.Semicolon).Commit()
            .Pattern("get-arrow").Kind(CSharpTokenKind.KwGet).Kind(CSharpTokenKind.Arrow).Ref("expr").Kind(CSharpTokenKind.Semicolon).Commit()
            .Pattern("set-arrow").Kind(CSharpTokenKind.KwSet).Kind(CSharpTokenKind.Arrow).Ref("expr").Kind(CSharpTokenKind.Semicolon).Commit()
            .Pattern("set-mod-semi").Ref("modifier").Kind(CSharpTokenKind.KwSet).Kind(CSharpTokenKind.Semicolon).Commit()
            .Pattern("set-mod-arrow").Ref("modifier").Kind(CSharpTokenKind.KwSet).Kind(CSharpTokenKind.Arrow).Ref("expr").Kind(CSharpTokenKind.Semicolon).Commit();
        b.Define("method-body")
            .Pattern("block").Ref("block").Commit()
            .Pattern("arrow").Kind(CSharpTokenKind.Arrow).Ref("expr").Kind(CSharpTokenKind.Semicolon).Commit()
            .Pattern("semi").Kind(CSharpTokenKind.Semicolon).Commit();
        b.Define("ctor-body")
            .Pattern("block").Ref("block").Commit()
            .Pattern("empty").Value(CSharpTokenKind.LBrace, "open").Value(CSharpTokenKind.RBrace, "close").Commit();
        b.Define("ctor-base")
            .Pattern("base").Kind(CSharpTokenKind.Colon).Kind(CSharpTokenKind.KwBase).Ref("args").Commit();

        // ── parameters / arguments ───────────────────────────────────────────
        b.Define("params")
            .Pattern("list").Kind(CSharpTokenKind.LParen).Optional(R("param-list")).Kind(CSharpTokenKind.RParen).Commit();
        b.Define("param-list")
            .Pattern("list").Ref("param").Repeat("param-tail").Commit();
        b.Define("param-tail")
            .Pattern("tail").Kind(CSharpTokenKind.Comma).Ref("param").Commit();
        b.Define("param")
            .Pattern("param").Ref("type").Value(CSharpTokenKind.Identifier, "name").Optional(R("param-default")).Commit();
        b.Define("param-default")
            .Pattern("default").Kind(CSharpTokenKind.Assign).Ref("expr").Commit();
        b.Define("args")
            .Pattern("list").Kind(CSharpTokenKind.LParen).Optional(R("arg-list")).Kind(CSharpTokenKind.RParen).Commit();
        b.Define("arg-list")
            .Pattern("list").Ref("expr").Repeat("arg-tail").Commit();
        b.Define("arg-tail")
            .Pattern("tail").Kind(CSharpTokenKind.Comma).Ref("expr").Commit();

        // ── statements ───────────────────────────────────────────────────────
        b.Define("block")
            .Pattern("block").Value(CSharpTokenKind.LBrace, "open").Repeat("statement", min: 0).Value(CSharpTokenKind.RBrace, "close").Commit();
        b.Define("statement")
            .Pattern("block").Ref("block").Commit()
            .Pattern("if").Kind(CSharpTokenKind.KwIf).Kind(CSharpTokenKind.LParen).Ref("expr").Kind(CSharpTokenKind.RParen)
                .Ref("statement").Optional(R("else")).Commit()
            .Pattern("while").Kind(CSharpTokenKind.KwWhile).Kind(CSharpTokenKind.LParen).Ref("expr").Kind(CSharpTokenKind.RParen)
                .Ref("statement").Commit()
            .Pattern("do").Kind(CSharpTokenKind.KwDo).Ref("statement").Kind(CSharpTokenKind.KwWhile)
                .Kind(CSharpTokenKind.LParen).Ref("expr").Kind(CSharpTokenKind.RParen).Kind(CSharpTokenKind.Semicolon).Commit()
            .Pattern("for").Kind(CSharpTokenKind.KwFor).Kind(CSharpTokenKind.LParen)
                .Optional(R("for-init")).Value(CSharpTokenKind.Semicolon, "semi1").Optional(R("for-cond"))
                .Value(CSharpTokenKind.Semicolon, "semi2").Optional(R("for-incr")).Kind(CSharpTokenKind.RParen).Ref("statement").Commit()
            .Pattern("foreach").Kind(CSharpTokenKind.KwForeach).Kind(CSharpTokenKind.LParen)
                .Ref("foreach-decl").Value(CSharpTokenKind.Identifier, "name")
                .Kind(CSharpTokenKind.KwIn).Ref("expr").Kind(CSharpTokenKind.RParen).Ref("statement").Commit()
            .Pattern("switch").Kind(CSharpTokenKind.KwSwitch).Kind(CSharpTokenKind.LParen).Ref("expr").Kind(CSharpTokenKind.RParen)
                .Value(CSharpTokenKind.LBrace, "open").Repeat("switch-case", min: 0).Value(CSharpTokenKind.RBrace, "close").Commit()
            .Pattern("try").Kind(CSharpTokenKind.KwTry).Ref("block").Repeat("catch", min: 0).Optional(R("finally")).Commit()
            .Pattern("using").Kind(CSharpTokenKind.KwUsing).Kind(CSharpTokenKind.LParen).Ref("using-resource")
                .Kind(CSharpTokenKind.RParen).Ref("statement").Commit()
            .Pattern("local-function").Ref("local-function").Commit()
            .Pattern("return").Kind(CSharpTokenKind.KwReturn).Optional(R("expr")).Kind(CSharpTokenKind.Semicolon).Commit()
            .Pattern("throw").Kind(CSharpTokenKind.KwThrow).Ref("expr").Kind(CSharpTokenKind.Semicolon).Commit()
            .Pattern("break").Kind(CSharpTokenKind.KwBreak).Kind(CSharpTokenKind.Semicolon).Commit()
            .Pattern("continue").Kind(CSharpTokenKind.KwContinue).Kind(CSharpTokenKind.Semicolon).Commit()
            .Pattern("goto").Kind(CSharpTokenKind.KwGoto).Value(CSharpTokenKind.Identifier, "target").Kind(CSharpTokenKind.Semicolon).Commit()
            .Pattern("label").Value(CSharpTokenKind.Identifier, "label").Value(CSharpTokenKind.Colon, "colon").Ref("statement").Commit()
            .Pattern("var-decl").Kind(CSharpTokenKind.KwVar).Value(CSharpTokenKind.Identifier, "name")
                .Kind(CSharpTokenKind.Assign).Ref("expr").Kind(CSharpTokenKind.Semicolon).Commit()
            .Pattern("const-decl").Kind(CSharpTokenKind.KwConst).Ref("type").Value(CSharpTokenKind.Identifier, "name")
                .Kind(CSharpTokenKind.Assign).Ref("expr").Kind(CSharpTokenKind.Semicolon).Commit()
            .Pattern("typed-decl").Ref("type").Value(CSharpTokenKind.Identifier, "name")
                .Kind(CSharpTokenKind.Assign).Ref("expr").Kind(CSharpTokenKind.Semicolon).Commit()
            .Pattern("typed-decl-bare").Ref("type").Value(CSharpTokenKind.Identifier, "name")
                .Kind(CSharpTokenKind.Semicolon).Commit()
            .Pattern("expr-stmt").Ref("expr").Kind(CSharpTokenKind.Semicolon).Commit()
            .Pattern("empty").Kind(CSharpTokenKind.Semicolon).Commit();
        b.Define("else")
            .Pattern("else").Kind(CSharpTokenKind.KwElse).Ref("statement").Commit();
        b.Define("foreach-decl")
            .Pattern("var").Kind(CSharpTokenKind.KwVar).Commit()
            .Pattern("typed").Ref("type").Commit();
        b.Define("local-function")
            .Pattern("method").Optional(R("attributes")).Optional(R("modifiers")).Ref("type")
                .Value(CSharpTokenKind.Identifier, "name").Optional(R("generic-params")).Ref("params").Ref("method-body").Commit();
        b.Define("for-init")
            .Pattern("var-decl").Kind(CSharpTokenKind.KwVar).Value(CSharpTokenKind.Identifier, "name")
                .Kind(CSharpTokenKind.Assign).Ref("expr").Commit()
            .Pattern("typed-decl").Ref("type").Value(CSharpTokenKind.Identifier, "name")
                .Kind(CSharpTokenKind.Assign).Ref("expr").Commit()
            .Pattern("init").Ref("expr").Commit();
        b.Define("for-cond")
            .Pattern("cond").Ref("expr").Commit();
        b.Define("for-incr")
            .Pattern("incr").Ref("expr").Commit();
        b.Define("switch-case")
            .Pattern("case-break").Kind(CSharpTokenKind.KwCase).Ref("expr").Value(CSharpTokenKind.Colon, "colon")
                .Ref("statement").Kind(CSharpTokenKind.KwBreak).Kind(CSharpTokenKind.Semicolon).Commit()
            .Pattern("case").Kind(CSharpTokenKind.KwCase).Ref("expr").Value(CSharpTokenKind.Colon, "colon").Ref("statement").Commit()
            .Pattern("default-break").Kind(CSharpTokenKind.KwDefault).Value(CSharpTokenKind.Colon, "colon")
                .Ref("statement").Kind(CSharpTokenKind.KwBreak).Kind(CSharpTokenKind.Semicolon).Commit()
            .Pattern("default").Kind(CSharpTokenKind.KwDefault).Value(CSharpTokenKind.Colon, "colon").Ref("statement").Commit();
        b.Define("catch")
            .Pattern("catch").Kind(CSharpTokenKind.KwCatch).Kind(CSharpTokenKind.LParen).Optional(R("catch-decl")).Kind(CSharpTokenKind.RParen).Ref("block").Commit();
        b.Define("catch-decl")
            .Pattern("typed").Ref("type").Value(CSharpTokenKind.Identifier, "name").Commit()
            .Pattern("typed-bare").Ref("type").Commit();
        b.Define("finally")
            .Pattern("finally").Kind(CSharpTokenKind.KwFinally).Ref("block").Commit();
        b.Define("using-resource")
            .Pattern("declare").Kind(CSharpTokenKind.KwVar).Value(CSharpTokenKind.Identifier, "name")
                .Kind(CSharpTokenKind.Assign).Ref("expr").Commit()
            .Pattern("expr").Ref("expr").Commit();

        // ── expressions ──────────────────────────────────────────────────────
        b.Define("expr")
            .Pattern("value").Ref("expr-assignment").Commit();
        b.Define("expr-assignment")
            .Pattern("assign").Ref("expr-conditional").Optional(R("assign-tail")).Commit();
        b.Define("assign-tail")
            .Pattern("tail").Kind(CSharpTokenKind.Assign).Ref("expr-assignment").Commit()
            .Pattern("compound-add").Kind(CSharpTokenKind.PlusAssign).Ref("expr").Commit()
            .Pattern("compound-sub").Kind(CSharpTokenKind.MinusAssign).Ref("expr").Commit()
            .Pattern("compound-mul").Kind(CSharpTokenKind.StarAssign).Ref("expr").Commit()
            .Pattern("compound-div").Kind(CSharpTokenKind.SlashAssign).Ref("expr").Commit()
            .Pattern("compound-mod").Kind(CSharpTokenKind.PercentAssign).Ref("expr").Commit()
            .Pattern("compound-and").Kind(CSharpTokenKind.AmpAssign).Ref("expr").Commit()
            .Pattern("compound-or").Kind(CSharpTokenKind.PipeAssign).Ref("expr").Commit()
            .Pattern("compound-xor").Kind(CSharpTokenKind.CaretAssign).Ref("expr").Commit()
            .Pattern("compound-coalesce").Kind(CSharpTokenKind.QuestionQuestionAssign).Ref("expr").Commit()
            .Pattern("compound-shift-left").Kind(CSharpTokenKind.LtLtAssign).Ref("expr").Commit();
        b.Define("expr-conditional")
            .Pattern("conditional").Ref("expr-coalesce").Optional(R("cond-tail")).Commit()
            .Pattern("throw").Kind(CSharpTokenKind.KwThrow).Ref("expr").Commit();
        b.Define("cond-tail")
            .Pattern("tail").Kind(CSharpTokenKind.Question).Ref("expr").Value(CSharpTokenKind.Colon, "colon").Ref("expr").Commit();
        b.Define("expr-coalesce").Pattern("coalesce").LeftAssoc("expr-or", CSharpTokenKind.QuestionQuestion).Commit();
        b.Define("expr-or").Pattern("or").LeftAssoc("expr-and", CSharpTokenKind.PipePipe).Commit();
        b.Define("expr-and").Pattern("and").LeftAssoc("expr-bitor", CSharpTokenKind.AmpAmp).Commit();
        b.Define("expr-bitor").Pattern("bitor").LeftAssoc("expr-bitxor", CSharpTokenKind.Pipe).Commit();
        b.Define("expr-bitxor").Pattern("bitxor").LeftAssoc("expr-bitand", CSharpTokenKind.Caret).Commit();
        b.Define("expr-bitand").Pattern("bitand").LeftAssoc("expr-equality", CSharpTokenKind.Amp).Commit();
        b.Define("expr-equality").Pattern("equality").LeftAssoc("expr-relational", CSharpTokenKind.EqEq, CSharpTokenKind.NotEq).Commit();
        b.Define("expr-relational")
            .Pattern("relational").LeftAssoc("expr-shift",
                CSharpTokenKind.Lt, CSharpTokenKind.Gt, CSharpTokenKind.LtEq, CSharpTokenKind.GtEq).Commit()
            .Pattern("type-is").Ref("expr-shift").Kind(CSharpTokenKind.KwIs).Ref("type")
                .Optional(V(CSharpTokenKind.Identifier, "var")).Commit()
            .Pattern("type-as").Ref("expr-shift").Kind(CSharpTokenKind.KwAs).Ref("type").Commit();
        b.Define("expr-shift")
            .Pattern("shift").Ref("expr-additive").Repeat("shift-tail").Commit();
        b.Define("shift-tail")
            .Pattern("left").Kind(CSharpTokenKind.Lt).Kind(CSharpTokenKind.Lt).Ref("expr-additive").Commit()
            .Pattern("right").Kind(CSharpTokenKind.Gt).Kind(CSharpTokenKind.Gt).Ref("expr-additive").Commit();
        b.Define("expr-additive").Pattern("additive").LeftAssoc("expr-multiplicative", CSharpTokenKind.Plus, CSharpTokenKind.Minus).Commit();
        b.Define("expr-multiplicative").Pattern("multiplicative").LeftAssoc("expr-unary", CSharpTokenKind.Star, CSharpTokenKind.Slash, CSharpTokenKind.Percent).Commit();
        b.Define("expr-unary")
            .Pattern("unary-minus").Kind(CSharpTokenKind.Minus).Ref("expr-unary").Commit()
            .Pattern("not").Kind(CSharpTokenKind.Bang).Ref("expr-unary").Commit()
            .Pattern("bitwise-not").Kind(CSharpTokenKind.Tilde).Ref("expr-unary").Commit()
            .Pattern("await").Kind(CSharpTokenKind.KwAwait).Ref("expr-unary").Commit()
            .Pattern("postfix").Ref("expr-postfix").Commit();
        b.Define("expr-postfix")
            .Pattern("postfix").Ref("expr-primary").Repeat("postfix-tail").Commit();
        b.Define("postfix-tail")
            .Pattern("member").Kind(CSharpTokenKind.Dot).Value(CSharpTokenKind.Identifier, "name").Commit()
            .Pattern("conditional-member").Kind(CSharpTokenKind.QuestionDot).Value(CSharpTokenKind.Identifier, "name").Commit()
            .Pattern("conditional-index").Kind(CSharpTokenKind.Question).Value(CSharpTokenKind.LBracket, "open")
                .Optional(R("arg-list")).Value(CSharpTokenKind.RBracket, "close").Commit()
            .Pattern("invoke").Ref("args").Commit()
            .Pattern("generic-invoke").Ref("type-args").Ref("args").Commit()
            .Pattern("index").Value(CSharpTokenKind.LBracket, "open").Optional(R("arg-list")).Value(CSharpTokenKind.RBracket, "close").Commit()
            .Pattern("forgiving").Kind(CSharpTokenKind.Bang).Commit();
        b.Define("type-args")
            .Pattern("list").Value(CSharpTokenKind.Lt, "open").Ref("type-list").Value(CSharpTokenKind.Gt, "close").Commit();
        b.Define("expr-primary")
            .Pattern("number").Value(CSharpTokenKind.NumberLiteral, "value").Commit()
            .Pattern("string").Value(CSharpTokenKind.StringLiteral, "value").Commit()
            .Pattern("interpolated").Value(CSharpTokenKind.InterpolatedStringLiteral, "value").Commit()
            .Pattern("char").Value(CSharpTokenKind.CharLiteral, "value").Commit()
            .Pattern("true").Kind(CSharpTokenKind.KwTrue).Commit()
            .Pattern("false").Kind(CSharpTokenKind.KwFalse).Commit()
            .Pattern("null").Kind(CSharpTokenKind.KwNull).Commit()
            .Pattern("ident").Value(CSharpTokenKind.Identifier, "name").Commit()
            .Pattern("this").Kind(CSharpTokenKind.KwThis).Commit()
            .Pattern("base").Kind(CSharpTokenKind.KwBase).Commit()
            .Pattern("parenthesized").Value(CSharpTokenKind.LParen, "open").Ref("expr").Value(CSharpTokenKind.RParen, "close").Commit()
            .Pattern("cast").Value(CSharpTokenKind.LParen, "open").Ref("type").Value(CSharpTokenKind.RParen, "close").Ref("expr-unary").Commit()
            .Pattern("typeof").Kind(CSharpTokenKind.KwTypeof).Kind(CSharpTokenKind.LParen).Ref("type").Kind(CSharpTokenKind.RParen).Commit()
            .Pattern("default-typed").Kind(CSharpTokenKind.KwDefault).Kind(CSharpTokenKind.LParen).Ref("type").Kind(CSharpTokenKind.RParen).Commit()
            .Pattern("default-bare").Kind(CSharpTokenKind.KwDefault).Commit()
            .Pattern("new").Kind(CSharpTokenKind.KwNew).Ref("type").Ref("args").Commit()
            .Pattern("new-array").Kind(CSharpTokenKind.KwNew).Ref("type").Value(CSharpTokenKind.LBracket, "open").Ref("expr").Value(CSharpTokenKind.RBracket, "close").Commit()
            .Pattern("lambda-ident").Optional(R("async-kw")).Value(CSharpTokenKind.Identifier, "param").Kind(CSharpTokenKind.Arrow).Ref("expr").Commit()
            .Pattern("lambda-parens").Optional(R("async-kw")).Kind(CSharpTokenKind.LParen)
                .Optional(R("lambda-param-list")).Kind(CSharpTokenKind.RParen).Kind(CSharpTokenKind.Arrow).Ref("expr").Commit()
            .Pattern("lambda-block-ident").Optional(R("async-kw")).Value(CSharpTokenKind.Identifier, "param").Kind(CSharpTokenKind.Arrow).Ref("block").Commit()
            .Pattern("lambda-block-parens").Optional(R("async-kw")).Kind(CSharpTokenKind.LParen)
                .Optional(R("lambda-param-list")).Kind(CSharpTokenKind.RParen).Kind(CSharpTokenKind.Arrow).Ref("block").Commit()
            .Pattern("generic-name").Value(CSharpTokenKind.Identifier, "name").Ref("type-args").Commit()
            .Pattern("type-keyword").Ref("type-keyword").Commit();
        b.Define("type-keyword")
            .Pattern("bool").Kind(CSharpTokenKind.KwBool).Commit()
            .Pattern("byte").Kind(CSharpTokenKind.KwByte).Commit()
            .Pattern("sbyte").Kind(CSharpTokenKind.KwSbyte).Commit()
            .Pattern("short").Kind(CSharpTokenKind.KwShort).Commit()
            .Pattern("ushort").Kind(CSharpTokenKind.KwUshort).Commit()
            .Pattern("int").Kind(CSharpTokenKind.KwInt).Commit()
            .Pattern("uint").Kind(CSharpTokenKind.KwUint).Commit()
            .Pattern("long").Kind(CSharpTokenKind.KwLong).Commit()
            .Pattern("ulong").Kind(CSharpTokenKind.KwUlong).Commit()
            .Pattern("float").Kind(CSharpTokenKind.KwFloat).Commit()
            .Pattern("double").Kind(CSharpTokenKind.KwDouble).Commit()
            .Pattern("decimal").Kind(CSharpTokenKind.KwDecimal).Commit()
            .Pattern("char").Kind(CSharpTokenKind.KwChar).Commit()
            .Pattern("string").Kind(CSharpTokenKind.KwString).Commit()
            .Pattern("object").Kind(CSharpTokenKind.KwObject).Commit();
        b.Define("async-kw")
            .Pattern("async").Kind(CSharpTokenKind.KwAsync).Commit();
        b.Define("lambda-param")
            .Pattern("typed").Ref("type").Value(CSharpTokenKind.Identifier, "name").Optional(R("param-default")).Commit()
            .Pattern("bare").Value(CSharpTokenKind.Identifier, "name").Optional(R("param-default")).Commit();
        b.Define("lambda-param-list")
            .Pattern("list").Ref("lambda-param").Repeat("lambda-param-tail").Commit();
        b.Define("lambda-param-tail")
            .Pattern("tail").Kind(CSharpTokenKind.Comma).Ref("lambda-param").Commit();

        return b.Build();
    }
}