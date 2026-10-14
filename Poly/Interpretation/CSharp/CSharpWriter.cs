using System.Globalization;

using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;

namespace Poly.Interpretation.CSharp;

/// <summary>
/// Prints Poly Syntax AST nodes as C# source through the <see cref="CSharpGrammar"/>
/// table — the write half of the C# language surface. Declarations and statements
/// print by looking up their rule + pattern and emitting the pattern's fixed
/// skeleton around runtime content; expression wording (operators, grouping, type
/// spelling) is composed by the writer because it is content, not table structure.
///
/// This replaces the former StringBuilder-only generator. Output is validated
/// against Roslyn and read back by <see cref="CSharpReader"/> in the test suite.
/// </summary>
public sealed class CSharpWriter {
    private readonly AnalysisResult? _analysisResult;
    private readonly Stack<HashSet<string>> _declScopes = new();
    private bool _fuseUndeclaredName;

    /// <summary>Creates a writer without analysis metadata. Some type-aware
    /// features (e.g. resolved member names) may be unavailable.</summary>
    public CSharpWriter() {
    }

    /// <summary>Creates a writer with semantic analysis results for
    /// type-aware code generation.</summary>
    public CSharpWriter(AnalysisResult analysisResult) {
        ArgumentNullException.ThrowIfNull(analysisResult);
        _analysisResult = analysisResult;
    }

    /// <summary>Prints a single AST node as a statement (expression nodes print
    /// as expression statements).</summary>
    public string Write(Node node) {
        ArgumentNullException.ThrowIfNull(node);
        return WithDeclScope(() => {
            var sb = new StringBuilder();
            WriteStatement(sb, node, 0);
            return sb.ToString().TrimEnd();
        });
    }

    /// <summary>Prints a complete compilation unit: nullable header, usings,
    /// file-scoped namespace, top-level statements, and types.</summary>
    public string Write(CompilationUnitNode unit) {
        ArgumentNullException.ThrowIfNull(unit);
        return WithDeclScope(() => {
            var sb = new StringBuilder();
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            foreach (var usingNs in unit.Usings)
                sb.AppendLine(Render("using", "directive", usingNs));
            sb.AppendLine();
            if (unit.Namespace != null) {
                sb.AppendLine(Render("namespace", "file-scoped", unit.Namespace));
                sb.AppendLine();
            }
            if (unit.TopLevelStatements != null) {
                _fuseUndeclaredName = true;
                foreach (var stmt in unit.TopLevelStatements) {
                    WriteStatement(sb, stmt, 0);
                    sb.AppendLine();
                }
                _fuseUndeclaredName = false;
                sb.AppendLine();
            }
            foreach (var typeDef in unit.Types) {
                WriteTypeDefinition(sb, typeDef, 0);
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        });
    }

    /// <summary>Prints type definitions with the standard header.</summary>
    public string Write(IReadOnlyList<TypeDefinitionNode> typeDefs) => Write(typeDefs, testStatements: null);

    /// <summary>Prints optional leading statements (test scaffolding) followed by
    /// type definitions.</summary>
    public string Write(IReadOnlyList<TypeDefinitionNode> typeDefs, IReadOnlyList<Node>? testStatements) {
        ArgumentNullException.ThrowIfNull(typeDefs);
        return WithDeclScope(() => {
            var sb = new StringBuilder();
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine();
            if (testStatements is not null) {
                _fuseUndeclaredName = true;
                foreach (var stmt in testStatements) {
                    WriteStatement(sb, stmt, 0);
                    sb.AppendLine();
                }
                _fuseUndeclaredName = false;
                sb.AppendLine();
            }
            foreach (var typeDef in typeDefs) {
                WriteTypeDefinition(sb, typeDef, 0);
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        });
    }

    private string WithDeclScope(Func<string> write) {
        _declScopes.Clear();
        _fuseUndeclaredName = false;
        _declScopes.Push(new HashSet<string>(StringComparer.Ordinal));
        try {
            return write();
        }
        finally {
            _declScopes.Clear();
            _fuseUndeclaredName = false;
        }
    }

    private bool DeclaredHere(string name) =>
        _declScopes.Count > 0 && _declScopes.Peek().Contains(name);

    private void MarkDeclared(string name) {
        if (_declScopes.Count == 0)
            _declScopes.Push(new HashSet<string>(StringComparer.Ordinal));
        _declScopes.Peek().Add(name);
    }

    // ── printer plumbing ─────────────────────────────────────────────────────

    /// <summary>Prints a named pattern. Positional content strings land on the
    /// pattern's content positions (unnamed values, refs, repeats, optionals) in
    /// element order; named holes come from <paramref name="fills"/>.</summary>
    private static string Render(string rule, string pattern, params string?[] content) =>
        RenderFills(rule, pattern, fills: null, content);

    private static string RenderFills(
        string rule,
        string pattern,
        IReadOnlyDictionary<string, string>? fills,
        params string?[] content) {
        var index = 0;
        return CSharpLanguage.Printer.Print(
            rule,
            pattern,
            ctx => {
                var i = index++;
                if (i >= content.Length)
                    throw new InvalidOperationException(
                        $"Pattern '{rule}/{pattern}' has more content positions than the writer supplied.");
                if (content[i] is { Length: > 0 } text)
                    ctx.Emit(text);
            },
            fills);
    }

    private static Dictionary<string, string> Names(params (string Name, string Value)[] entries) {
        var fills = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in entries)
            fills[name] = value;
        return fills;
    }

    private static Dictionary<string, string> Braces() => Names(("open", "{"), ("close", "}"));

    private static string Pad(int indent) => new(' ', indent * 4);

    // ── statements ───────────────────────────────────────────────────────────

    private void WriteStatement(StringBuilder sb, Node node, int indent) {
        switch (node) {
            case AttributedNode attrNode:
                WriteAttributes(sb, attrNode.Attributes, indent);
                WriteStatement(sb, attrNode.Inner, indent);
                return;
            case MethodDefinitionNode method when indent == 0:
                WriteLocalFunction(sb, method);
                return;
            case TypeDefinitionNode typeDef:
                WriteTypeDefinition(sb, typeDef, indent);
                return;
            case MethodDefinitionNode method:
                sb.AppendLine(MethodText(method, indent));
                return;
            case ConstructorDefinitionNode ctor:
                sb.AppendLine(ConstructorText(ctor, indent, "ctor"));
                return;
            case PropertyDefinitionNode prop:
                sb.AppendLine(PropertyText(prop, indent));
                return;
            case FieldDefinitionNode field:
                sb.AppendLine(FieldText(field, indent));
                return;
            case Assignment assign:
                sb.Append(Pad(indent));
                sb.AppendLine(Render("statement", "expr-stmt", Expr(assign)));
                return;
            case Block block:
                sb.AppendLine(BlockText(block, indent));
                return;
            case IfStatement ifStmt:
                sb.Append(Pad(indent));
                sb.AppendLine(IfText(ifStmt, indent));
                return;
            case WhileLoop whileLoop:
                sb.Append(Pad(indent));
                sb.AppendLine(WhileText(whileLoop, indent));
                return;
            case DoWhileLoop doWhile:
                sb.Append(Pad(indent));
                sb.AppendLine(DoWhileText(doWhile, indent));
                return;
            case ForLoop forLoop:
                sb.Append(Pad(indent));
                sb.AppendLine(ForText(forLoop, indent));
                return;
            case ForEachLoop forEach:
                sb.Append(Pad(indent));
                sb.AppendLine(ForEachText(forEach, indent));
                return;
            case SwitchStatement switchStmt:
                sb.Append(Pad(indent));
                sb.AppendLine(SwitchText(switchStmt, indent));
                return;
            case TryCatchFinally tryCatch:
                sb.AppendLine(TryText(tryCatch, indent));
                return;
            case UsingStatement usingStmt:
                sb.Append(Pad(indent));
                sb.AppendLine(UsingText(usingStmt, indent));
                return;
            case BreakStatement:
                sb.Append(Pad(indent));
                sb.AppendLine(Render("statement", "break"));
                return;
            case ContinueStatement:
                sb.Append(Pad(indent));
                sb.AppendLine(Render("statement", "continue"));
                return;
            case GotoStatement gotoStmt:
                sb.Append(Pad(indent));
                sb.AppendLine(RenderFills("statement", "goto", Names(("target", gotoStmt.Target))));
                return;
            case LabelDeclaration label:
                sb.Append(Pad(indent));
                sb.AppendLine(RenderFills("statement", "label",
                    Names(("label", label.Name), ("colon", ":")), new string?[] { null }));
                WriteStatement(sb, label.Statement, indent);
                return;
            case Return ret:
                sb.Append(Pad(indent));
                sb.AppendLine(Render("statement", "return", ret.Value is null ? null : " " + Expr(ret.Value)));
                return;
            case ThrowStatement throwStmt:
                sb.Append(Pad(indent));
                sb.AppendLine(Render("statement", "throw", " " + Expr(throwStmt.Exception)));
                return;
            case Variable variable:
                sb.Append(Pad(indent));
                sb.AppendLine(Render("statement", "expr-stmt", EscapeIdent(variable.Name)));
                return;
            case Comment c:
                sb.Append(Pad(indent));
                sb.AppendLine(c.Text.EndsWith('.') ? $"// {c.Text}" : $"// {c.Text}.");
                return;
            default:
                sb.Append(Pad(indent));
                sb.AppendLine(Render("statement", "expr-stmt", Expr(node)));
                return;
        }
    }

    private string StatementText(Node node, int indent) {
        var sb = new StringBuilder();
        WriteStatement(sb, node, indent);
        return sb.ToString().TrimEnd('\n', '\r');
    }

    private string BlockText(Block block, int indent) {
        if (block.Nodes.Count == 0 && block.Variables.Count == 0)
            return Pad(indent) + RenderFills("block", "block", Braces(), " ");
        return Pad(indent) + RenderFills("block", "block", Braces(),
            "\n" + BlockInner(block, indent) + Pad(indent));
    }

    private string BlockInner(Block block, int indent) {
        var inner = new StringBuilder();
        _declScopes.Push(new HashSet<string>(StringComparer.Ordinal));
        var fuse = new HashSet<Variable>(ReferenceEqualityComparer.Instance);
        foreach (var v in block.Variables) {
            if (v is not Variable variable)
                continue;
            var assignIndex = -1;
            for (var i = 0; i < block.Nodes.Count; i++) {
                if (block.Nodes[i] is Assignment a && ReferenceEquals(a.Destination, variable)) {
                    assignIndex = i;
                    break;
                }
            }
            if (assignIndex >= 0 && !MentionsVariableBefore(block, variable, assignIndex))
                fuse.Add(variable);
            else {
                inner.Append(Pad(indent + 1));
                inner.AppendLine(DeclaredLocalText(variable, block));
            }
        }
        for (var i = 0; i < block.Nodes.Count; i++) {
            var node = block.Nodes[i];
            if (i != block.Nodes.Count - 1 && _analysisResult != null && _analysisResult.CanElide(node))
                continue;
            if (node is Assignment a
                && a.Destination is Variable dest
                && fuse.Remove(dest)
                && !MentionsVariableBefore(block, dest, i)) {
                inner.Append(Pad(indent + 1));
                inner.Append(RenderFills("statement", "var-decl", Names(("name", EscapeIdent(dest.Name))), Expr(a.Value)));
                inner.AppendLine();
                MarkDeclared(dest.Name);
                continue;
            }
            WriteStatement(inner, node, indent + 1);
        }
        _declScopes.Pop();
        return inner.ToString();
    }

    private string DeclaredLocalText(Variable variable, Block block) {
        var typeName = TypeNameForVariable(variable, block);
        MarkDeclared(variable.Name);
        return $"var {EscapeIdent(variable.Name)} = default({typeName});";
    }

    private static bool MentionsVariableBefore(Block block, Variable variable, int exclusiveEnd) {
        for (var i = 0; i < exclusiveEnd; i++) {
            if (MentionsVariable(block.Nodes[i], variable))
                return true;
        }
        return false;
    }

    private static bool MentionsVariable(Node node, Variable variable) {
        if (ReferenceEquals(node, variable))
            return true;
        if (node is Assignment a && ReferenceEquals(a.Destination, variable))
            return true;
        foreach (var child in node.Children) {
            if (child is not null && MentionsVariable(child, variable))
                return true;
        }
        return false;
    }

    private string TypeNameForVariable(Variable variable, Block block) {
        var td = _analysisResult?.GetResolvedType(variable);
        if (td is not null) {
            var runtime = td.GetRuntimeType();
            if (runtime is not null)
                return ToCSharpTypeName(runtime);
            if (td.PrimitiveType is { } prim)
                return prim.GetCSharpKeyword();
            if (!string.IsNullOrEmpty(td.Name))
                return td.Name.Replace('`', '_');
        }
        var inferred = InferTypeNameFromFirstAssignment(block, variable);
        return inferred ?? "object";
    }

    private static string? InferTypeNameFromFirstAssignment(Node node, Variable variable) {
        if (node is Assignment a && ReferenceEquals(a.Destination, variable))
            return InferTypeNameFromValue(a.Value);
        foreach (var child in node.Children) {
            if (child is null)
                continue;
            var inferred = InferTypeNameFromFirstAssignment(child, variable);
            if (inferred is not null)
                return inferred;
        }
        return null;
    }

    private static string? InferTypeNameFromValue(Node value) => value switch {
        Constant { Value: not null } c => ToCSharpTypeName(c.Value.GetType()),
        New n => TypeRefName(n.Type),
        NewArray arr => (TypeRefName(arr.ElementType) ?? "object") + "[]",
        TypeCast tc => TypeRefName(tc.TargetTypeReference),
        TypeAs ta => TypeRefName(ta.TargetTypeReference),
        _ => null,
    };

    private static string? TypeRefName(Node? type) => type switch {
        PrimitiveTypeReference p => p.PrimitiveId.GetCSharpKeyword() + (p.IsNullable ? "?" : ""),
        ClrTypeReference c when c.RuntimeType.IsGenericType || c.RuntimeType == typeof(object) =>
            ToCSharpTypeName(c.RuntimeType),
        NamedTypeReference n when n.TypeArguments is { Count: > 0 } args =>
            n.TypeName + "<" + string.Join(", ", args.Select(a => TypeRefName(a) ?? "object")) + ">",
        NamedTypeReference n => n.TypeName,
        TypeReference t => t.TypeName,
        OptionalTypeReference o => (TypeRefName(o.InnerType) ?? "object") + "?",
        CollectionTypeReference { Kind: CollectionKind.Array } c =>
            (TypeRefName(c.ElementType) ?? "object") + "[]",
        CollectionTypeReference c =>
            (c.Kind == CollectionKind.Set ? "ISet<" : "List<")
            + (TypeRefName(c.ElementType) ?? "object") + ">",
        _ => null,
    };

    private static string ToCSharpTypeName(Type type) {
        if (type.IsArray) {
            var elem = type.GetElementType();
            return elem is null ? "object[]" : $"{ToCSharpTypeName(elem)}[]";
        }
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null)
            return $"{ToCSharpTypeName(nullable)}?";
        if (type.IsGenericType) {
            var raw = type.Name;
            var tick = raw.IndexOf('`');
            var head = tick >= 0 ? raw[..tick] : raw;
            var args = type.GetGenericArguments().Select(ToCSharpTypeName);
            return $"{head}<{string.Join(", ", args)}>";
        }
        return Type.GetTypeCode(type) switch {
            TypeCode.Boolean => "bool",
            TypeCode.Byte => "byte",
            TypeCode.SByte => "sbyte",
            TypeCode.Int16 => "short",
            TypeCode.UInt16 => "ushort",
            TypeCode.Int32 => "int",
            TypeCode.UInt32 => "uint",
            TypeCode.Int64 => "long",
            TypeCode.UInt64 => "ulong",
            TypeCode.Single => "float",
            TypeCode.Double => "double",
            TypeCode.Decimal => "decimal",
            TypeCode.Char => "char",
            TypeCode.String => "string",
            _ => type == typeof(object) ? "object" : type.Name,
        };
    }

    private string IfText(IfStatement ifStmt, int indent) {
        var body = _analysisResult is null || !_analysisResult.CanElide(ifStmt.ThenBranch)
            ? BodyText(ifStmt.ThenBranch, indent)
            : "\n" + Pad(indent) + RenderFills("block", "block", Braces(), " ");
        var elseText = ifStmt.ElseBranch is null ? null : ElseText(ifStmt.ElseBranch, indent);
        return Render("statement", "if", Expr(ifStmt.Condition), body, elseText);
    }

    private string ElseText(Node elseBranch, int indent) {
        if (elseBranch is IfStatement elseIf)
            return "\n" + Pad(indent) + Render("else", "else", IfText(elseIf, indent));
        return "\n" + Pad(indent) + Render("else", "else", BodyText(elseBranch, indent));
    }

    private string BodyText(Node body, int indent) {
        if (body is Block block)
            return "\n" + BlockText(block, indent);
        var inner = new StringBuilder();
        WriteStatement(inner, body, indent + 1);
        return "\n" + Pad(indent) + RenderFills("block", "block", Braces(), "\n" + inner + Pad(indent));
    }

    private string WhileText(WhileLoop whileLoop, int indent) =>
        Render("statement", "while", Expr(whileLoop.Condition), BodyText(whileLoop.Body, indent));

    private string DoWhileText(DoWhileLoop doWhile, int indent) =>
        Render("statement", "do",
            BodyText(doWhile.Body, indent) + "\n" + Pad(indent),
            Expr(doWhile.Condition));

    private string ForText(ForLoop forLoop, int indent) {
        var init = forLoop.Initializer != null && (_analysisResult is null || !_analysisResult.CanElide(forLoop.Initializer))
            ? Expr(forLoop.Initializer) : null;
        var condition = forLoop.Condition != null ? Expr(forLoop.Condition) : null;
        var increment = forLoop.Increment != null && (_analysisResult is null || !_analysisResult.CanElide(forLoop.Increment))
            ? Expr(forLoop.Increment) : null;
        return RenderFills("statement", "for", Names(("semi1", "; "), ("semi2", "; ")),
            init, condition, increment, BodyText(forLoop.Body, indent));
    }

    private string ForEachText(ForEachLoop forEach, int indent) =>
        RenderFills("statement", "foreach", Names(("name", forEach.LoopVariable.Name)),
            Expr(forEach.Collection), BodyText(forEach.Body, indent));

    private string SwitchText(SwitchStatement switchStmt, int indent) {
        var cases = new StringBuilder();
        foreach (var caseNode in switchStmt.Cases) {
            cases.Append('\n');
            cases.Append(Pad(indent + 1));
            cases.Append(RenderFills("switch-case", "case", Names(("colon", ":")),
                Expr(caseNode.Pattern), "\n" + StatementText(caseNode.Body, indent + 1)));
        }
        if (switchStmt.DefaultCase != null) {
            cases.Append('\n');
            cases.Append(Pad(indent + 1));
            cases.Append(RenderFills("switch-case", "default", Names(("colon", ":")),
                "\n" + StatementText(switchStmt.DefaultCase, indent + 1)));
        }
        return RenderFills("statement", "switch",
            Names(("open", "\n" + Pad(indent) + "{"), ("close", "}")),
            Expr(switchStmt.Value), cases.Length == 0 ? null : cases + "\n" + Pad(indent));
    }

    private string TryText(TryCatchFinally tryCatch, int indent) {
        var catches = new StringBuilder();
        if (tryCatch.CatchClauses != null) {
            foreach (var clause in tryCatch.CatchClauses) {
                catches.Append('\n');
                catches.Append(Pad(indent));
                var decl = clause.ExceptionType != null
                    ? clause.VariableName != null
                        ? RenderFills("catch-decl", "typed", Names(("name", clause.VariableName)), TypeText(clause.ExceptionType))
                        : Render("catch-decl", "typed-bare", TypeText(clause.ExceptionType))
                    : clause.VariableName != null ? "Exception " + clause.VariableName : "";
                catches.Append(Render("catch", "catch", decl, "\n" + BlockOrWrappedText(clause.Body, indent)));
            }
        }
        var finallyText = tryCatch.FinallyBlock != null
            ? "\n" + Pad(indent) + Render("finally", "finally", "\n" + BlockOrWrappedText(tryCatch.FinallyBlock, indent))
            : null;
        return Pad(indent) + Render("statement", "try",
            "\n" + BlockOrWrappedText(tryCatch.TryBlock, indent), catches.ToString(), finallyText);
    }

    private string BlockOrWrappedText(Node body, int indent) =>
        body is Block block
            ? BlockText(block, indent)
            : Pad(indent) + RenderFills("block", "block", Braces(),
                "\n" + StatementText(body, indent + 1) + "\n" + Pad(indent));

    private string UsingText(UsingStatement usingStmt, int indent) {
        if (usingStmt.Resource is Assignment { Destination: Variable v } a) {
            var resource = RenderFills("using-resource", "declare", Names(("name", v.Name)), Expr(a.Value));
            return Render("statement", "using", resource, BodyText(usingStmt.Body, indent));
        }
        return Render("statement", "using", Expr(usingStmt.Resource), BodyText(usingStmt.Body, indent));
    }

    // ── type definitions ─────────────────────────────────────────────────────

    private void WriteTypeDefinition(StringBuilder sb, TypeDefinitionNode typeDef, int indent) {
        var attrs = AttributesText(typeDef.Attributes, indent);
        var mods = AccessModifierText(typeDef.AccessModifier).Trim();
        sb.Append(attrs);

        if (typeDef.TypeCategory.Is(TypeCategory.Enumeration)) {
            var members = new StringBuilder();
            if (typeDef.Fields != null) {
                for (var i = 0; i < typeDef.Fields.Count; i++) {
                    members.Append('\n');
                    members.Append(Pad(indent + 1));
                    members.Append(RenderFills("enum-member", "member", Names(
                        ("name", typeDef.Fields[i].Name),
                        ("value", i.ToString(CultureInfo.InvariantCulture)))));
                    if (i < typeDef.Fields.Count - 1)
                        members.Append(',');
                }
                members.Append('\n');
                members.Append(Pad(indent));
            }
            var body = Pad(indent) + RenderFills("enum-body", "body", Braces(),
                members.Length == 0 ? " " : members.ToString(), null);
            sb.Append(Pad(indent));
            sb.AppendLine(RenderFills("type-decl", "enum", TypeNameFill(typeDef.Name), null, mods,
                "\n" + body));
            return;
        }

        if (typeDef.IsInterface) {
            var body = new StringBuilder();
            if (typeDef.Properties != null) {
                foreach (var property in typeDef.Properties) {
                    body.Append('\n');
                    body.Append(Pad(indent + 1));
                    body.Append(InterfacePropertyText(property));
                }
            }
            if (typeDef.Methods != null) {
                foreach (var method in typeDef.Methods) {
                    body.Append('\n');
                    body.Append(Pad(indent + 1));
                    body.Append(InterfaceMethodText(method));
                }
            }
            body.Append('\n');
            body.Append(Pad(indent));
            sb.Append(Pad(indent));
            sb.AppendLine(RenderFills("type-decl", "interface", TypeNameFill(typeDef.Name), null, mods,
                GenericParamsText(typeDef.GenericParameters), LineageText(typeDef), body.ToString()));
            return;
        }

        var isRecord = typeDef.EffectiveSemantics.HasValueEquality;
        if (isRecord && !TypeDefinitionHasBody(typeDef)) {
            sb.Append(Pad(indent));
            sb.AppendLine(RenderFills("type-decl", "record-semi", TypeNameFill(typeDef.Name), null, mods,
                GenericParamsText(typeDef.GenericParameters), PrimaryParamsText(typeDef.PrimaryConstructorParameters),
                LineageText(typeDef)));
            return;
        }

        var membersBlock = new StringBuilder();
        void AddMember(Node member, string? name = null) {
            membersBlock.Append('\n');
            membersBlock.Append(name is null ? MemberText(member, indent + 1) : MemberText(member, indent + 1, name));
        }
        if (typeDef.Fields != null) {
            foreach (var field in typeDef.Fields)
                AddMember(field);
        }
        if (typeDef.Constructors != null) {
            foreach (var ctor in typeDef.Constructors)
                AddMember(ctor, typeDef.Name);
        }
        foreach (var prop in GetBodyProperties(typeDef))
            AddMember(prop);
        if (typeDef.Methods != null) {
            foreach (var method in typeDef.Methods)
                AddMember(method);
        }
        var bodyText = "\n" + Pad(indent) + RenderFills("type-body", "body", Braces(), membersBlock + "\n" + Pad(indent));

        sb.Append(Pad(indent));
        sb.AppendLine(isRecord
            ? RenderFills("type-decl", "record", TypeNameFill(typeDef.Name),
                null, mods, GenericParamsText(typeDef.GenericParameters),
                PrimaryParamsText(typeDef.PrimaryConstructorParameters), LineageText(typeDef), bodyText)
            : RenderFills("type-decl", "class", TypeNameFill(typeDef.Name),
                null, mods, GenericParamsText(typeDef.GenericParameters),
                LineageText(typeDef), bodyText));
    }

    private static Dictionary<string, string> TypeNameFill(string name) => Names(("name", name));

    private string InterfaceMethodText(MethodDefinitionNode method) =>
        TypeText(method.ReturnType) + " " + method.Name + "(" + ParamListText(method.Parameters) + ");";

    private string InterfacePropertyText(PropertyDefinitionNode prop) {
        var accessors = new StringBuilder();
        if (prop.Getter != null) accessors.Append("get; ");
        if (prop.Setter != null) accessors.Append("set; ");
        return $"{TypeText(prop.MemberType)} {prop.Name} {{ {accessors.ToString().TrimEnd()} }}";
    }

    private static bool TypeDefinitionHasBody(TypeDefinitionNode typeDef) =>
        (typeDef.Fields?.Count ?? 0) > 0
        || (typeDef.Constructors?.Count ?? 0) > 0
        || GetBodyProperties(typeDef).Count > 0
        || (typeDef.Methods?.Count ?? 0) > 0;

    private static IReadOnlyList<PropertyDefinitionNode> GetBodyProperties(TypeDefinitionNode typeDef) {
        if (typeDef.Properties is null || !typeDef.EffectiveSemantics.HasValueEquality || typeDef.PrimaryConstructorParameters is not { Count: > 0 })
            return typeDef.Properties ?? [];
        var primaryParameterNames = typeDef.PrimaryConstructorParameters
            .Where(static parameter => parameter.TypeReference is not null)
            .Select(static parameter => parameter.Name)
            .ToHashSet(StringComparer.Ordinal);
        return typeDef.Properties
            .Where(property => !primaryParameterNames.Contains(property.Name))
            .ToArray();
    }

    // ── members ──────────────────────────────────────────────────────────────

    private string MemberText(Node member, int indent, string? ctorName = null) {
        var sb = new StringBuilder();
        switch (member) {
            case AttributedNode attrNode:
                WriteAttributes(sb, attrNode.Attributes, indent);
                return sb.ToString() + MemberText(attrNode.Inner, indent, ctorName);
            case MethodDefinitionNode method:
                return MethodText(method, indent);
            case ConstructorDefinitionNode ctor:
                return ConstructorText(ctor, indent, ctorName ?? "ctor");
            case PropertyDefinitionNode prop:
                return PropertyText(prop, indent);
            case FieldDefinitionNode field:
                return FieldText(field, indent);
            default:
                WriteStatement(sb, member, indent);
                return sb.ToString().TrimEnd('\n', '\r');
        }
    }

    private string FieldText(FieldDefinitionNode field, int indent) {
        var attrs = AttributesText(field.Attributes, indent);
        var mut = field.Mutability;
        var mods = AccessModifierText(field.AccessModifier)
            + (field.IsStatic ? "static " : "")
            + (mut.HasFlag(Mutability.CompileTimeConst) ? "const " : mut.HasFlag(Mutability.ReadOnlyAfterInit) ? "readonly " : "")
            + (mut.HasFlag(Mutability.VolatileAccess) ? "volatile " : "");
        return attrs + Pad(indent) + RenderFills("member", "field", Names(("name", field.Name)),
            null, mods.Trim(),
            TypeText(field.FieldType),
            field.DefaultValue != null ? " = " + Expr(field.DefaultValue) : null);
    }

    private string MethodText(MethodDefinitionNode method, int indent) {
        var attrs = AttributesText(method.Attributes, indent);
        var mods = AccessModifierText(method.AccessModifier)
            + (method.IsOverride ? "override " : "")
            + (method.IsStatic ? "static " : "")
            + (method.IsAsync ? "async " : "");
        var body = method.Body switch {
            Block { Nodes: [Return r] } when r.Value is null => " { return; }",
            Block { Nodes: [Return r] } => " " + Render("method-body", "arrow", Expr(r.Value!)),
            Block block => "\n" + BlockText(block, indent),
            null => Render("method-body", "semi"),
            _ => "\n" + BodyText(method.Body!, indent),
        };
        return attrs + Pad(indent) + RenderFills("member", "method", Names(("name", method.Name)),
            null, mods.Trim(),
            TypeText(method.ReturnType),
            GenericParamsText(method.GenericParameters),
            ParamsText(method.Parameters),
            body);
    }

    private void WriteLocalFunction(StringBuilder sb, MethodDefinitionNode method) {
        var mods = ((method.IsStatic ? "static " : "") + (method.IsAsync ? "async " : "")).Trim();
        var body = method.Body switch {
            Block block => "\n" + BlockText(block, 0),
            null => Render("method-body", "semi"),
            _ => "\n" + BodyText(method.Body, 0),
        };
        sb.Append(RenderFills("member", "method", Names(("name", method.Name)),
            null, mods, TypeText(method.ReturnType), GenericParamsText(method.GenericParameters),
            ParamsText(method.Parameters), body));
        sb.AppendLine();
        sb.AppendLine();
    }

    private string ConstructorText(ConstructorDefinitionNode ctor, int indent, string className) {
        var baseText = ctor.BaseConstructorInvocation != null
            ? " " + Render("ctor-base", "base", ArgsText(ctor.BaseConstructorInvocation.Arguments))
            : ctor.BaseCall is { Count: > 0 } ? " " + Render("ctor-base", "base", ArgsText(ctor.BaseCall)) : null;
        var body = ctor.Body switch {
            Block block => "\n" + BlockText(block, indent),
            null => " " + RenderFills("ctor-body", "empty", Names(("open", "{ "), ("close", "}"))),
            _ => "\n" + BodyText(ctor.Body, indent),
        };
        return Pad(indent) + RenderFills("member", "ctor", Names(("name", className)),
            null, AccessModifierText(ctor.AccessModifier).Trim(),
            ParamsText(ctor.Parameters), baseText, body);
    }

    private string PropertyText(PropertyDefinitionNode prop, int indent) {
        var attrs = AttributesText(prop.Attributes, indent);
        var mods = (prop.IsStatic ? "static " : "") + AccessModifierText(prop.AccessModifier);
        var init = prop.Initializer?.Value is { } initializerValue ? " = " + Expr(initializerValue) + ";" : null;
        var isAuto = IsAutoProperty(prop);

        if (isAuto || prop.Getter != null || prop.Setter != null) {
            string accessorsBody;
            if (isAuto) {
                var inline = new StringBuilder();
                if (prop.Getter != null) inline.Append("get; ");
                if (prop.Setter != null) {
                    if (prop.Setter.AccessModifier.HasValue)
                        inline.Append(AccessModifierText(prop.Setter.AccessModifier.Value));
                    inline.Append("set; ");
                }
                else if (prop.Initializer != null) {
                    inline.Append("init; ");
                }
                accessorsBody = " " + RenderFills("property-body", "body", Braces(), " " + inline.ToString().TrimEnd() + " ");
            }
            else {
                var block = new StringBuilder();
                if (prop.Getter != null) {
                    block.Append('\n');
                    block.Append(Pad(indent + 1));
                    block.Append(prop.Getter.Body != null
                        ? Render("accessor", "get-arrow", Expr(prop.Getter.Body))
                        : Render("accessor", "get-semi"));
                }
                if (prop.Setter != null) {
                    block.Append('\n');
                    block.Append(Pad(indent + 1));
                    var setterMod = prop.Setter.AccessModifier.HasValue
                        ? AccessModifierText(prop.Setter.AccessModifier.Value).Trim()
                        : null;
                    block.Append(prop.Setter.Body != null
                        ? setterMod is null
                            ? Render("accessor", "set-arrow", Expr(prop.Setter.Body))
                            : Render("accessor", "set-mod-arrow", setterMod, Expr(prop.Setter.Body))
                        : setterMod is null
                            ? Render("accessor", "set-semi")
                            : Render("accessor", "set-mod-semi", setterMod));
                }
                block.Append('\n');
                block.Append(Pad(indent));
                accessorsBody = "\n" + Pad(indent) + RenderFills("property-body", "body", Braces(), block.ToString());
            }
            return attrs + Pad(indent) + RenderFills("member", "property", Names(("name", prop.Name)),
                null, mods.Trim(), TypeText(prop.MemberType), accessorsBody, init);
        }

        if (init != null) {
            // No accessors, initializer only: syntactically a field.
            return attrs + Pad(indent) + RenderFills("member", "field", Names(("name", prop.Name)),
                null, mods.Trim(), TypeText(prop.MemberType), init);
        }
        var body = " " + RenderFills("property-body", "body", Braces(), " get; set; ");
        return attrs + Pad(indent) + RenderFills("member", "property", Names(("name", prop.Name)),
            null, mods.Trim(), TypeText(prop.MemberType), body, null);
    }

    private static bool IsAutoProperty(PropertyDefinitionNode prop) =>
        (prop.Getter is null || prop.Getter.Body is null)
        && (prop.Setter is null || prop.Setter.Body is null)
        && (prop.Getter is not null || prop.Setter is not null);

    // ── expressions ──────────────────────────────────────────────────────────

    private string Expr(Node node) {
        switch (node) {
            case Constant constant:
                return ConstantText(constant);
            case Variable variable:
                return IdentText(variable.Name);
            case Parameter parameter:
                return IdentText(parameter.Name);
            case ThisReference:
                return Render("expr-primary", "this");
            case TypeOf typeOf:
                return Render("expr-primary", "typeof", TypeText(typeOf.Type));
            case ClrTypeReference clr when clr.RuntimeType.IsGenericType || clr.RuntimeType == typeof(object):
                return ToCSharpTypeName(clr.RuntimeType);
            case TypeReference typeRef:
                return typeRef.TypeName;
            case PrimitiveTypeReference prim:
                return prim.PrimitiveId.GetCSharpKeyword() + (prim.IsNullable ? "?" : "");
            case NamedTypeReference named:
                return named.FullName + (named.TypeArguments is { Count: > 0 }
                    ? "<" + string.Join(", ", named.TypeArguments.Select(TypeText)) + ">"
                    : "");
            case OptionalTypeReference opt:
                return TypeText(opt.InnerType) + "?";
            case CollectionTypeReference coll:
                return coll.Kind == CollectionKind.Array
                    ? TypeText(coll.ElementType) + "[]"
                    : (coll.Kind == CollectionKind.Set ? "ISet<" : "List<") + TypeText(coll.ElementType) + ">";
            case MapTypeReference map:
                return "Dictionary<" + TypeText(map.KeyType) + ", " + TypeText(map.ValueType) + ">";
            case UnaryMinus minus:
                return Render("expr-unary", "unary-minus", Expr(minus.Operand));
            case Not not:
                var notNeedsParens = not.Value is not (Constant or Variable or Parameter or ThisReference
                    or Member or IndexAccess or Invoke or New
                    or TypeCast or TypeIs or TypeAs);
                return Render("expr-unary", "not", notNeedsParens ? "(" + Expr(not.Value) + ")" : Expr(not.Value));
            case NullForgiving nf:
                return Expr(nf.Operand) + Render("postfix-tail", "forgiving");
            case Default def:
                return def.TargetType is not null
                    ? Render("expr-primary", "default-typed", TypeText(def.TargetType))
                    : Render("expr-primary", "default-bare");
            case Add add:
                return BinaryChain(add, "expr-additive", "additive", " + ");
            case Subtract sub:
                return BinaryChain(sub, "expr-additive", "additive", " - ");
            case Multiply mul:
                return BinaryChain(mul, "expr-multiplicative", "multiplicative", " * ");
            case Divide div:
                return BinaryChain(div, "expr-multiplicative", "multiplicative", " / ");
            case Modulo mod:
                return BinaryChain(mod, "expr-multiplicative", "multiplicative", " % ");
            case Equal eq:
                return BinaryChain(eq, "expr-equality", "equality", " == ");
            case NotEqual neq:
                return BinaryChain(neq, "expr-equality", "equality", " != ");
            case LessThan lt:
                return BinaryChain(lt, "expr-relational", "relational", " < ");
            case LessThanOrEqual lte:
                return BinaryChain(lte, "expr-relational", "relational", " <= ");
            case GreaterThan gt:
                return BinaryChain(gt, "expr-relational", "relational", " > ");
            case GreaterThanOrEqual gte:
                return BinaryChain(gte, "expr-relational", "relational", " >= ");
            case BitwiseAnd bitwiseAnd:
                return BinaryChain(bitwiseAnd, "expr-bitand", "bitand", " & ");
            case BitwiseOr bitwiseOr:
                return BinaryChain(bitwiseOr, "expr-bitor", "bitor", " | ");
            case BitwiseXor bitwiseXor:
                return BinaryChain(bitwiseXor, "expr-bitxor", "bitxor", " ^ ");
            case ShiftLeft shiftLeft:
                return BinaryChain(shiftLeft, "expr-shift", "shift", " << ");
            case ShiftRight shiftRight:
                return BinaryChain(shiftRight, "expr-shift", "shift", " >> ");
            case And and:
                return BinaryChain(and, "expr-and", "and", " && ");
            case Or or:
                return BinaryChain(or, "expr-or", "or", " || ");
            case Coalesce coalesce:
                return BinaryChain(coalesce, "expr-coalesce", "coalesce", " ?? ");
            case BitwiseNot bitwiseNot:
                return Render("expr-unary", "bitwise-not", Expr(bitwiseNot.Operand));
            case ThrowExpression throwExpression:
                return "throw " + Expr(throwExpression.Value);
            case Conditional cond:
                return "(" + Render("expr-conditional", "conditional",
                    Expr(cond.Condition),
                    " ? " + Expr(cond.IfTrue) + " : " + Expr(cond.IfFalse)) + ")";
            case Assignment assign:
                return AssignmentText(assign);
            case Member member:
                return MemberAccessText(member);
            case IndexAccess index:
                return Expr(index.Value) + RenderFills("postfix-tail", "index",
                    Names(("open", "["), ("close", "]")), ArgListText(index.Arguments));
            case Await awaitNode:
                return Render("expr-unary", "await", Expr(awaitNode.Operand));
            case Invoke invoke:
                var typeArgs = invoke.TypeArguments is { Count: > 0 }
                    ? RenderFills("type-args", "list", Names(("open", "<"), ("close", ">")),
                        string.Join(", ", invoke.TypeArguments.Select(TypeText)))
                    : "";
                return Expr(invoke.Delegate) + typeArgs + Render("postfix-tail", "invoke", ArgsText(invoke.Arguments));
            case New @new:
                return Render("expr-primary", "new", TypeText(@new.Type), ArgsText(@new.Arguments));
            case TypeCast cast:
                var operand = Expr(cast.Operand);
                var type = TypeText(cast.TargetTypeReference);
                // (int)-7 is a cast. (Customer)-c is subtraction to the C# compiler.
                if ((operand.StartsWith('-') || operand.StartsWith('+'))
                    && CastOfSignedOperandIsSubtraction(type))
                    operand = "(" + operand + ")";
                return "(" + type + ")" + operand;
            case TypeIs typeIs:
                return Expr(typeIs.Operand) + " is " + TypeText(typeIs.TargetTypeReference)
                    + (typeIs.VariableName != null ? " " + typeIs.VariableName : "");
            case TypeAs typeAs:
                return Expr(typeAs.Operand) + " as " + TypeText(typeAs.TargetTypeReference);
            case Lambda lambda:
                return LambdaText(lambda);
            case Block block:
                return BlockText(block, 0);
            case PopCount popCount:
                return "System.Numerics.BitOperations.PopCount((ulong)" + Expr(popCount.Operand) + ")";
            case StridedSetBits:
                return "/* StridedSetBits */";
            case NewArray newArray:
                return "(" + TypeText(newArray.ElementType) + "[])(new " + TypeText(newArray.ElementType)
                    + "[" + Expr(newArray.Length) + "])";
            case SuspendNode suspend:
                return Expr(suspend.Inner);
            case BaseConstructorInvocationNode baseCtor:
                return Render("ctor-base", "base", ArgsText(baseCtor.Arguments));
            case Comment c:
                return $"/* {c.Text} */";
            default:
                return node.ToString() ?? "";
        }
    }

    private static string IdentText(string name) =>
        RenderFills("expr-primary", "ident", Names(("name", EscapeIdent(name))));

    private string AssignmentText(Assignment assign) {
        if (assign.Destination is Variable dest && _fuseUndeclaredName && !DeclaredHere(dest.Name)) {
            MarkDeclared(dest.Name);
            return "var " + EscapeIdent(dest.Name) + " = " + Expr(assign.Value);
        }
        return Render("expr-assignment", "assign", Expr(assign.Destination), " = " + Expr(assign.Value));
    }

    private string MemberAccessText(Member member) {
        if (member.Value is ParameterReference)
            return IdentText(member.MemberName);
        var value = Expr(member.Value);
        if (member.Value is TypeCast or TypeAs or Conditional or Coalesce)
            value = "(" + value + ")";
        return value + RenderFills("postfix-tail", "member", Names(("name", member.MemberName)));
    }

    private string LambdaText(Lambda lambda) {
        var async = BodyContainsAwait(lambda.Body) ? Render("async-kw", "async") + " " : null;
        if (lambda.Parameters.Count == 1 && lambda.Parameters[0].TypeReference is null)
            return RenderFills("expr-primary", "lambda-ident",
                Names(("param", EscapeIdent(lambda.Parameters[0].Name))),
                async, Expr(lambda.Body));
        return Render("expr-primary", "lambda-parens",
            async, string.Join(", ", lambda.Parameters.Select(ParamText)), Expr(lambda.Body));
    }

    private static bool BodyContainsAwait(Node node) {
        if (node is Await) return true;
        foreach (var child in node.Children) {
            if (child != null && BodyContainsAwait(child))
                return true;
        }
        return false;
    }

    private string BinaryChain(Node node, string rule, string pattern, string op) {
        var inner = BinaryOperand(LeftOperand(node), op, isRight: false)
            + op
            + BinaryOperand(RightOperand(node), op, isRight: true);
        // Two slots: the shift pattern has an operand + a repeat tail, the
        // LeftAssoc levels have a single chain slot; the spare is ignored.
        return Render(rule, pattern, inner, null);
    }

    private static Node LeftOperand(Node node) => node switch {
        Add n => n.LeftHandValue,
        Subtract n => n.LeftHandValue,
        Multiply n => n.LeftHandValue,
        Divide n => n.LeftHandValue,
        Modulo n => n.LeftHandValue,
        Equal n => n.LeftHandValue,
        NotEqual n => n.LeftHandValue,
        LessThan n => n.LeftHandValue,
        LessThanOrEqual n => n.LeftHandValue,
        GreaterThan n => n.LeftHandValue,
        GreaterThanOrEqual n => n.LeftHandValue,
        BitwiseAnd n => n.LeftHandValue,
        BitwiseOr n => n.LeftHandValue,
        BitwiseXor n => n.LeftHandValue,
        ShiftLeft n => n.LeftHandValue,
        ShiftRight n => n.LeftHandValue,
        And n => n.LeftHandValue,
        Or n => n.LeftHandValue,
        Coalesce n => n.LeftHandValue,
        _ => throw new InvalidOperationException($"Node '{node.GetType().Name}' is not a binary chain."),
    };

    private static Node RightOperand(Node node) => node switch {
        Add n => n.RightHandValue,
        Subtract n => n.RightHandValue,
        Multiply n => n.RightHandValue,
        Divide n => n.RightHandValue,
        Modulo n => n.RightHandValue,
        Equal n => n.RightHandValue,
        NotEqual n => n.RightHandValue,
        LessThan n => n.RightHandValue,
        LessThanOrEqual n => n.RightHandValue,
        GreaterThan n => n.RightHandValue,
        GreaterThanOrEqual n => n.RightHandValue,
        BitwiseAnd n => n.RightHandValue,
        BitwiseOr n => n.RightHandValue,
        BitwiseXor n => n.RightHandValue,
        ShiftLeft n => n.RightHandValue,
        ShiftRight n => n.RightHandValue,
        And n => n.RightHandValue,
        Or n => n.RightHandValue,
        Coalesce n => n.RightHandValue,
        _ => throw new InvalidOperationException($"Node '{node.GetType().Name}' is not a binary chain."),
    };

    private string BinaryOperand(Node node, string op, bool isRight) {
        if (node is Coalesce or Conditional)
            return "(" + Expr(node) + ")";

        var parentPrec = BinaryOpPrecedence(op);
        var childPrec = BinaryNodePrecedence(node);
        var trimmed = op.Trim();
        var sameRank = childPrec == parentPrec;
        // Left-assoc: parenthesize a same-rank right child (`a << (b << c)`).
        // Right-assoc (`??`): parenthesize a same-rank left child.
        var parenthesize = childPrec < parentPrec
            || (sameRank && isRight && trimmed is not "??")
            || (sameRank && !isRight && trimmed is "??");
        return parenthesize ? "(" + Expr(node) + ")" : Expr(node);
    }

    private static int BinaryNodePrecedence(Node node) => node switch {
        Multiply or Divide or Modulo => 9,
        Add or Subtract => 8,
        ShiftLeft or ShiftRight => 7,
        LessThan or LessThanOrEqual or GreaterThan or GreaterThanOrEqual => 6,
        Equal or NotEqual => 5,
        BitwiseAnd => 4,
        BitwiseXor => 3,
        BitwiseOr => 2,
        And => 1,
        Or => 0,
        Coalesce => -1,
        Conditional => -2,
        _ => 10,
    };

    private static int BinaryOpPrecedence(string op) => op.Trim() switch {
        "*" or "/" or "%" => 9,
        "+" or "-" => 8,
        "<<" or ">>" => 7,
        "<" or "<=" or ">" or ">=" => 6,
        "==" or "!=" => 5,
        "&" => 4,
        "^" => 3,
        "|" => 2,
        "&&" => 1,
        "||" => 0,
        "??" => -1,
        _ => 10,
    };

    private string ConstantText(Constant constant) {
        if (constant.Value == null)
            return Render("expr-primary", "null");
        if (constant.Value is bool b)
            return Render("expr-primary", b ? "true" : "false");
        if (constant.Value is string s)
            return RenderFills("expr-primary", "string", Names(("value", StringLiteral(s))));
        if (constant.Value is char c)
            return RenderFills("expr-primary", "char", Names(("value", CharLiteral(c))));
        return NumberLiteral(constant.Value);
    }

    private static string NumberLiteral(object value) => value switch {
        float f => f.ToString("G", CultureInfo.InvariantCulture) + "f",
        double d => d.ToString("G", CultureInfo.InvariantCulture),
        decimal m => m.ToString("G", CultureInfo.InvariantCulture) + "m",
        long l => l.ToString(CultureInfo.InvariantCulture) + "L",
        uint ui => ui.ToString(CultureInfo.InvariantCulture) + "u",
        ulong ul => ul.ToString(CultureInfo.InvariantCulture) + "UL",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null",
    };

    private static string StringLiteral(string value) {
        var sb = new StringBuilder("\"");
        foreach (var ch in value) {
            sb.Append(ch switch {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\0' => "\\0",
                _ when char.IsControl(ch) => "\\u" + ((int)ch).ToString("x4", CultureInfo.InvariantCulture),
                _ => ch.ToString(),
            });
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static string CharLiteral(char value) => value switch {
        '\\' => "'\\\\'",
        '\'' => "'\\''",
        '\n' => "'\\n'",
        '\r' => "'\\r'",
        '\t' => "'\\t'",
        '\0' => "'\\0'",
        _ when char.IsControl(value) => "'\\u" + ((int)value).ToString("x4", CultureInfo.InvariantCulture) + "'",
        _ => $"'{value}'",
    };

    // ── types / text helpers ─────────────────────────────────────────────────

    private string TypeText(Node type) => type switch {
        PrimitiveTypeReference p => p.PrimitiveId.GetCSharpKeyword() + (p.IsNullable ? "?" : ""),
        ClrTypeReference c when c.RuntimeType.IsGenericType || c.RuntimeType == typeof(object) => ToCSharpTypeName(c.RuntimeType),
        TypeDefinitionReference t => t.TypeName,
        TypeReference t => t.TypeName,
        NamedTypeReference n => n.FullName + (n.TypeArguments is { Count: > 0 }
            ? "<" + string.Join(", ", n.TypeArguments.Select(TypeText)) + ">"
            : ""),
        OptionalTypeReference o => TypeText(o.InnerType) + "?",
        CollectionTypeReference { Kind: CollectionKind.Array } c => TypeText(c.ElementType) + "[]",
        CollectionTypeReference c => (c.Kind == CollectionKind.Set ? "ISet<" : "List<") + TypeText(c.ElementType) + ">",
        MapTypeReference m => "Dictionary<" + TypeText(m.KeyType) + ", " + TypeText(m.ValueType) + ">",
        ResolvedTypeReference r => r.TypeDefinition.FullName,
        _ => type.ToString() ?? "object",
    };

    private string ParamsText(IReadOnlyList<Parameter>? parameters) =>
        Render("params", "list", ParamListText(parameters));

    private string PrimaryParamsText(IReadOnlyList<Parameter>? parameters) =>
        parameters is { Count: > 0 } ? ParamsText(parameters) : "";

    private string ParamListText(IReadOnlyList<Parameter>? parameters) =>
        parameters is null ? "" : string.Join(", ", parameters.Select(ParamText));

    private string ParamText(Parameter param) =>
        (param.TypeReference != null ? TypeText(param.TypeReference) + " " : "")
        + param.Name
        + (param.DefaultValue != null ? " = " + Expr(param.DefaultValue) : "");

    private string ArgsText(IReadOnlyList<Node> arguments) =>
        Render("args", "list", ArgListText(arguments));

    private string ArgListText(IReadOnlyList<Node> arguments) =>
        arguments.Count == 0 ? "" : string.Join(", ", arguments.Select(Expr));

    private string GenericParamsText(IReadOnlyList<Parameter>? parameters) =>
        parameters is { Count: > 0 }
            ? RenderFills("generic-params", "list", Names(("open", "<"), ("close", ">")),
                string.Join(", ", parameters.Select(p => p.Name)))
            : "";

    private string LineageText(TypeDefinitionNode typeDef) {
        var lineage = Enumerable.Empty<Node>()
            .Concat(typeDef.BaseType is null ? [] : [typeDef.BaseType])
            .Concat(typeDef.Interfaces ?? [])
            .ToArray();
        return lineage.Length == 0 ? "" : " : " + string.Join(", ", lineage.Select(TypeText));
    }

    private string AttributesText(IReadOnlyList<AttributeNode> attributes, int indent) {
        if (attributes.Count == 0)
            return "";
        var sb = new StringBuilder();
        foreach (var attr in attributes)
            sb.AppendLine(AttributeText(attr, indent));
        return sb.ToString();
    }

    private string AttributeText(AttributeNode attr, int indent) =>
        Pad(indent) + RenderFills("attribute", attr.Arguments.Count > 0 ? "args" : "bare",
            Names(("open", "["), ("close", "]")),
            attr.Name,
            attr.Arguments.Count > 0
                ? Render("args", "list", string.Join(", ", attr.Arguments.Select(Expr)))
                : null);

    private void WriteAttributes(StringBuilder sb, IReadOnlyList<AttributeNode> attributes, int indent) {
        foreach (var attr in attributes)
            sb.AppendLine(AttributeText(attr, indent));
    }

    private static string AccessModifierText(AccessModifier modifier) => modifier switch {
        AccessModifier.Public => "public ",
        AccessModifier.Private => "private ",
        AccessModifier.Internal => "internal ",
        AccessModifier.Protected => "protected ",
        _ => "",
    };

    /// <summary>
    /// A bare or dotted name before a signed operand is a binary operator:
    /// <c>(Customer)-c</c>. Keyword and constructed types stay casts:
    /// <c>(int)-7</c>, <c>(List&lt;int&gt;)-x</c>.
    /// </summary>
    private static bool CastOfSignedOperandIsSubtraction(string type) {
        if (type.IndexOfAny(['<', '>', '?', '[', ']']) >= 0)
            return false;
        var head = type.Split('.')[0];
        return !CSharpKeywords.Contains(head);
    }

    private static string EscapeIdent(string name) =>
        CSharpKeywords.Contains(name) ? "@" + name : name;

    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal) {
        "abstract","as","base","bool","break","byte","case","catch","char","checked",
        "class","const","continue","decimal","default","delegate","do","double","else",
        "enum","event","explicit","extern","false","finally","fixed","float","for",
        "foreach","goto","if","implicit","in","int","interface","internal","is","lock",
        "long","namespace","new","null","object","operator","out","override","params",
        "private","protected","public","readonly","ref","return","sbyte","sealed","short",
        "sizeof","stackalloc","static","string","struct","switch","this","throw","true",
        "try","typeof","uint","ulong","unchecked","unsafe","ushort","using","virtual",
        "void","volatile","while","add","and","alias","ascending","async","await",
        "by","descending","dynamic","equals","file","from","get","global","group","init",
        "into","join","let","managed","nameof","nint","not","notnull","nuint","on",
        "or","orderby","partial","record","remove","required","scoped","select","set",
        "unmanaged","value","var","when","where","with","yield",
    };
}