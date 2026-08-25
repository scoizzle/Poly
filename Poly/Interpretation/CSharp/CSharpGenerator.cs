using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Interpretation.CSharp;

/// <summary>C# source code generator. Produces readable C# code from analyzed
/// AST nodes — intended for codegen, pretty-printing, and debugging.</summary>
/// <remarks>This is a secondary backend, NOT the canonical semantics path.
/// See <see cref="DirectVmAbiEmitter"/> for the authoritative execution path.
/// The generated code uses modern C# features (nullable enable, switch expressions,
/// pattern matching) and is formatted with standard indentation.</remarks>
public sealed class CSharpGenerator {
    private readonly AnalysisResult? _analysisResult;

    /// <summary>Creates a generator without analysis metadata. Some type-aware
    /// features (e.g. resolved member names) may be unavailable.</summary>
    public CSharpGenerator() {
    }

    /// <summary>Creates a generator with semantic analysis results for
    /// type-aware code generation.</summary>
    /// <param name="analysisResult">The semantic analysis result.</param>
    public CSharpGenerator(AnalysisResult analysisResult) {
        ArgumentNullException.ThrowIfNull(analysisResult);
        _analysisResult = analysisResult;
    }

    /// <summary>Generates C# source code for a single AST node.</summary>
    /// <param name="node">The node to generate (expression, statement, block,
    /// or type definition).</param>
    /// <returns>Formatted C# source code.</returns>
    public string Generate(Node node) {
        ArgumentNullException.ThrowIfNull(node);
        var sb = new StringBuilder();
        WriteStatement(sb, node, 0);
        return sb.ToString().TrimEnd();
    }

    /// <summary>Generates C# source code for a complete compilation unit.</summary>
    /// <param name="unit">The compilation unit to generate.</param>
    /// <returns>Formatted C# source code.</returns>
    public string Generate(CompilationUnitNode unit) {
        ArgumentNullException.ThrowIfNull(unit);
        var sb = new StringBuilder();
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        foreach (var usingNs in unit.Usings) {
            sb.Append("using ");
            sb.Append(usingNs);
            sb.AppendLine(";");
        }
        sb.AppendLine();
        if (unit.Namespace != null) {
            sb.Append("namespace ");
            sb.Append(unit.Namespace);
            sb.AppendLine(";");
            sb.AppendLine();
        }
        // Emit top-level statements BEFORE type definitions (C# top-level statement order)
        if (unit.TopLevelStatements != null) {
            foreach (var stmt in unit.TopLevelStatements) {
                WriteStatement(sb, stmt, 0);
                sb.AppendLine();
            }
            sb.AppendLine();
        }
        foreach (var typeDef in unit.Types) {
            WriteStatement(sb, typeDef, 0);
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Generates C# source code for a collection of type definitions.</summary>
    /// <param name="typeDefs">The type definitions to emit.</param>
    /// <returns>Formatted C# source code with nullable enabled.</returns>
    public string Generate(IReadOnlyList<TypeDefinitionNode> typeDefs) {
        return Generate(typeDefs, testStatements: null);
    }

    /// <summary>Generates C# source code for type definitions preceded by
    /// optional test statements (e.g. usage examples or setup code).</summary>
    /// <param name="typeDefs">The type definitions to emit.</param>
    /// <param name="testStatements">Optional statements to emit before the
    /// type definitions (typically usage or setup code).</param>
    /// <returns>Formatted C# source code.</returns>
    public string Generate(IReadOnlyList<TypeDefinitionNode> typeDefs, IReadOnlyList<Node>? testStatements) {
        ArgumentNullException.ThrowIfNull(typeDefs);
        var sb = new StringBuilder();
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine();
        if (testStatements is not null) {
            foreach (var stmt in testStatements) {
                WriteStatement(sb, stmt, 0);
                sb.AppendLine();
            }
            sb.AppendLine();
        }
        foreach (var typeDef in typeDefs) {
            WriteStatement(sb, typeDef, 0);
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static void WriteDefaultValue(StringBuilder sb, Node? typeRef) {
        if (typeRef is PrimitiveTypeReference prim) {
            switch (prim.PrimitiveId) {
                case PrimitiveType.String: sb.Append("\"\""); return;
                case PrimitiveType.Boolean: sb.Append("false"); return;
                case PrimitiveType.Int64: sb.Append("0"); return;
                case PrimitiveType.Int32: sb.Append("0"); return;
                case PrimitiveType.Decimal: sb.Append("0m"); return;
                case PrimitiveType.Float64: sb.Append("0.0"); return;
                case PrimitiveType.Guid: sb.Append("default(Guid)"); return;
                case PrimitiveType.ByteArray: sb.Append("Array.Empty<byte>()"); return;
            }
        }
        sb.Append("default");
    }

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
                WriteMethodDefinition(sb, method, indent);
                return;
            case ConstructorDefinitionNode ctor:
                WriteConstructorDefinition(sb, ctor, indent, "ctor");
                return;
            case PropertyDefinitionNode prop:
                WritePropertyDefinition(sb, prop, indent);
                return;
            case FieldDefinitionNode field:
                WriteFieldDefinition(sb, field, indent);
                return;
            case Block block:
                WriteBlock(sb, block, indent);
                return;
            case IfStatement ifStmt:
                WriteIfStatement(sb, ifStmt, indent);
                return;
            case WhileLoop whileLoop:
                WriteWhileLoop(sb, whileLoop, indent);
                return;
            case DoWhileLoop doWhile:
                WriteDoWhileLoop(sb, doWhile, indent);
                return;
            case ForLoop forLoop:
                WriteForLoop(sb, forLoop, indent);
                return;
            case ForEachLoop forEach:
                WriteForEachLoop(sb, forEach, indent);
                return;
            case SwitchStatement switchStmt:
                WriteSwitchStatement(sb, switchStmt, indent);
                return;
            case TryCatchFinally tryCatch:
                WriteTryCatchFinally(sb, tryCatch, indent);
                return;
            case UsingStatement usingStmt:
                WriteUsingStatement(sb, usingStmt, indent);
                return;
            case BreakStatement:
                Indent(sb, indent);
                sb.AppendLine("break;");
                return;
            case ContinueStatement:
                Indent(sb, indent);
                sb.AppendLine("continue;");
                return;
            case GotoStatement gotoStmt:
                Indent(sb, indent);
                sb.Append("goto ");
                sb.Append(gotoStmt.Target);
                sb.AppendLine(";");
                return;
            case LabelDeclaration label:
                Indent(sb, indent);
                sb.Append(label.Name);
                sb.AppendLine(":");
                WriteStatement(sb, label.Statement, indent);
                return;
            case Return ret:
                Indent(sb, indent);
                if (ret.Value != null) {
                    sb.Append("return ");
                    WriteExpression(sb, ret.Value);
                    sb.AppendLine(";");
                }
                else {
                    sb.AppendLine("return;");
                }
                return;
            case ThrowStatement throwStmt:
                Indent(sb, indent);
                sb.Append("throw ");
                WriteExpression(sb, throwStmt.Exception);
                sb.AppendLine(";");
                return;
            case Variable variable:
                Indent(sb, indent);
                sb.Append("var ");
                sb.Append(variable.Name);
                if (variable.Value != null) {
                    sb.Append(" = ");
                    WriteExpression(sb, variable.Value);
                }
                sb.AppendLine(";");
                return;
            case Comment c:
                Indent(sb, indent);
                sb.AppendLine(c.Text.EndsWith('.') ? $"// {c.Text}" : $"// {c.Text}.");
                return;
            default:
                Indent(sb, indent);
                WriteExpression(sb, node);
                sb.AppendLine(";");
                return;
        }
    }

    private void WriteBlock(StringBuilder sb, Block block, int indent) {
        Indent(sb, indent);
        if (block.Nodes.Count == 0 && block.Variables.Count == 0) {
            sb.AppendLine("{ }");
            return;
        }
        sb.AppendLine("{");
        foreach (var v in block.Variables) {
            WriteStatement(sb, v, indent + 1);
        }
        for (int i = 0; i < block.Nodes.Count; i++) {
            var node = block.Nodes[i];
            if (i == block.Nodes.Count - 1 || _analysisResult == null || !_analysisResult.CanElide(node)) {
                WriteStatement(sb, node, indent + 1);
            }
        }
        Indent(sb, indent);
        sb.AppendLine("}");
    }

    private void WriteIfStatement(StringBuilder sb, IfStatement ifStmt, int indent) {
        Indent(sb, indent);
        sb.Append("if (");
        WriteExpression(sb, ifStmt.Condition);
        sb.AppendLine(")");
        if (_analysisResult == null || !_analysisResult.CanElide(ifStmt.ThenBranch)) {
            WriteIfBody(sb, ifStmt.ThenBranch, indent);
        }
        else {
            Indent(sb, indent + 1);
            sb.AppendLine("{}");
        }
        if (ifStmt.ElseBranch != null) {
            if (_analysisResult == null || !_analysisResult.CanElide(ifStmt.ElseBranch)) {
                AppendElse(sb, ifStmt.ElseBranch, indent);
            }
        }
    }

    private void WriteIfBody(StringBuilder sb, Node body, int indent) {
        if (body is Block block) {
            WriteStatement(sb, block, indent);
        }
        else {
            Indent(sb, indent);
            sb.AppendLine("{");
            WriteStatement(sb, body, indent + 1);
            Indent(sb, indent);
            sb.AppendLine("}");
        }
    }

    private void AppendElse(StringBuilder sb, Node elseBranch, int indent) {
        if (elseBranch is IfStatement elseIf) {
            Indent(sb, indent);
            sb.Append("else if (");
            WriteExpression(sb, elseIf.Condition);
            sb.AppendLine(")");
            WriteIfBody(sb, elseIf.ThenBranch, indent);
            if (elseIf.ElseBranch != null)
                AppendElse(sb, elseIf.ElseBranch, indent);
        }
        else {
            Indent(sb, indent);
            sb.AppendLine("else");
            WriteIfBody(sb, elseBranch, indent);
        }
    }

    private void WriteWhileLoop(StringBuilder sb, WhileLoop whileLoop, int indent) {
        Indent(sb, indent);
        sb.Append("while (");
        WriteExpression(sb, whileLoop.Condition);
        sb.AppendLine(")");
        WriteStatement(sb, whileLoop.Body, whileLoop.Body is Block ? indent : indent + 1);
    }

    private void WriteDoWhileLoop(StringBuilder sb, DoWhileLoop doWhile, int indent) {
        Indent(sb, indent);
        sb.AppendLine("do");
        WriteStatement(sb, doWhile.Body, doWhile.Body is Block ? indent : indent + 1);
        Indent(sb, indent);
        sb.Append("while (");
        WriteExpression(sb, doWhile.Condition);
        sb.AppendLine(");");
    }

    private void WriteForLoop(StringBuilder sb, ForLoop forLoop, int indent) {
        Indent(sb, indent);
        sb.Append("for (");
        if (forLoop.Initializer != null && (_analysisResult == null || !_analysisResult.CanElide(forLoop.Initializer))) {
            WriteExpression(sb, forLoop.Initializer);
        }
        sb.Append("; ");
        if (forLoop.Condition != null) {
            WriteExpression(sb, forLoop.Condition);
        }
        sb.Append("; ");
        if (forLoop.Increment != null && (_analysisResult == null || !_analysisResult.CanElide(forLoop.Increment))) {
            WriteExpression(sb, forLoop.Increment);
        }
        sb.AppendLine(")");
        WriteStatement(sb, forLoop.Body, forLoop.Body is Block ? indent : indent + 1);
    }

    private void WriteForEachLoop(StringBuilder sb, ForEachLoop forEach, int indent) {
        Indent(sb, indent);
        sb.Append("foreach (var ");
        sb.Append(forEach.LoopVariable.Name);
        sb.Append(" in ");
        WriteExpression(sb, forEach.Collection);
        sb.AppendLine(")");
        WriteStatement(sb, forEach.Body, forEach.Body is Block ? indent : indent + 1);
    }

    private void WriteSwitchStatement(StringBuilder sb, SwitchStatement switchStmt, int indent) {
        Indent(sb, indent);
        sb.Append("switch (");
        WriteExpression(sb, switchStmt.Value);
        sb.AppendLine(")");
        Indent(sb, indent);
        sb.AppendLine("{");
        foreach (var caseNode in switchStmt.Cases) {
            Indent(sb, indent + 1);
            sb.Append("case ");
            WriteExpression(sb, caseNode.Pattern);
            sb.AppendLine(":");
            WriteStatement(sb, caseNode.Body, indent + 1);
        }
        if (switchStmt.DefaultCase != null) {
            Indent(sb, indent + 1);
            sb.AppendLine("default:");
            WriteStatement(sb, switchStmt.DefaultCase, indent + 1);
        }
        Indent(sb, indent);
        sb.AppendLine("}");
    }

    private void WriteTryCatchFinally(StringBuilder sb, TryCatchFinally tryCatch, int indent) {
        Indent(sb, indent);
        sb.AppendLine("try");
        WriteBracedBody(sb, tryCatch.TryBlock, indent);
        if (tryCatch.CatchClauses != null) {
            foreach (var clause in tryCatch.CatchClauses) {
                Indent(sb, indent);
                sb.Append("catch (");
                if (clause.ExceptionType != null) {
                    WriteExpression(sb, clause.ExceptionType);
                    if (clause.VariableName != null) {
                        sb.Append(' ');
                        sb.Append(clause.VariableName);
                    }
                }
                else if (clause.VariableName != null) {
                    sb.Append("Exception ");
                    sb.Append(clause.VariableName);
                }
                sb.AppendLine(")");
                WriteBracedBody(sb, clause.Body, indent);
            }
        }
        if (tryCatch.FinallyBlock != null) {
            Indent(sb, indent);
            sb.AppendLine("finally");
            WriteBracedBody(sb, tryCatch.FinallyBlock, indent);
        }
    }

    private void WriteBracedBody(StringBuilder sb, Node body, int indent) {
        if (body is Block) {
            WriteStatement(sb, body, indent);
            return;
        }
        Indent(sb, indent);
        sb.AppendLine("{");
        WriteStatement(sb, body, indent + 1);
        Indent(sb, indent);
        sb.AppendLine("}");
    }

    private void WriteUsingStatement(StringBuilder sb, UsingStatement usingStmt, int indent) {
        Indent(sb, indent);
        sb.Append("using (");
        if (usingStmt.Resource is Variable v && v.Value != null) {
            sb.Append("var ");
            sb.Append(v.Name);
            sb.Append(" = ");
            WriteExpression(sb, v.Value);
        }
        else {
            WriteExpression(sb, usingStmt.Resource);
        }
        sb.AppendLine(")");
        WriteStatement(sb, usingStmt.Body, indent);
    }

    private void WriteTypeDefinition(StringBuilder sb, TypeDefinitionNode typeDef, int indent) {
        WriteAttributes(sb, typeDef.Attributes, indent);
        var isEnum = typeDef.Fields?.All(f => f.DefaultValue is Constant) == true
                     && (typeDef.Methods?.Count ?? 0) == 0
                     && (typeDef.Constructors?.Count ?? 0) == 0;
        if (isEnum) {
            Indent(sb, indent);
            WriteAccessModifier(sb, typeDef.AccessModifier);
            sb.Append("enum ");
            sb.Append(typeDef.Name);
            sb.AppendLine();
            Indent(sb, indent);
            sb.AppendLine("{");
            if (typeDef.Fields != null) {
                for (int i = 0; i < typeDef.Fields.Count; i++) {
                    Indent(sb, indent + 1);
                    sb.Append(typeDef.Fields[i].Name);
                    if (typeDef.Fields[i].DefaultValue is Constant c && c.Value != null) {
                        sb.Append(" = ");
                        WriteExpression(sb, c);
                    }
                    if (i < typeDef.Fields.Count - 1) sb.Append(',');
                    sb.AppendLine();
                }
            }
            Indent(sb, indent);
            sb.AppendLine("}");
            return;
        }
        if (typeDef.IsInterface) {
            Indent(sb, indent);
            WriteAccessModifier(sb, typeDef.AccessModifier);
            sb.Append("interface ");
            sb.Append(typeDef.Name);
            if (typeDef.GenericParameters is { Count: > 0 }) {
                sb.Append('<');
                WriteCommaSeparated(sb, typeDef.GenericParameters.Select(static p => new NamedTypeReference(p.Name)));
                sb.Append('>');
            }
            WriteTypeLineage(sb, typeDef);
            sb.AppendLine();
            Indent(sb, indent);
            sb.AppendLine("{");
            if (typeDef.Properties != null) {
                foreach (var property in typeDef.Properties) {
                    WriteInterfacePropertyDefinition(sb, property, indent + 1);
                }
            }
            if (typeDef.Methods != null) {
                foreach (var method in typeDef.Methods) {
                    WriteInterfaceMethodDefinition(sb, method, indent + 1);
                }
            }
            Indent(sb, indent);
            sb.AppendLine("}");
            return;
        }
        Indent(sb, indent);
        WriteAccessModifier(sb, typeDef.AccessModifier);
        sb.Append(typeDef.EffectiveSemantics.HasValueEquality ? "record " : "class ");
        sb.Append(typeDef.Name);
        if (typeDef.GenericParameters is { Count: > 0 }) {
            sb.Append('<');
            WriteCommaSeparated(sb, typeDef.GenericParameters.Select(static p => new NamedTypeReference(p.Name)));
            sb.Append('>');
        }
        if (typeDef.PrimaryConstructorParameters is { Count: > 0 }) {
            sb.Append('(');
            WriteParameterDeclarations(sb, typeDef.PrimaryConstructorParameters);
            sb.Append(')');
        }
        WriteTypeLineage(sb, typeDef);
        if (typeDef.EffectiveSemantics.HasValueEquality && !TypeDefinitionHasBody(typeDef)) {
            sb.AppendLine(";");
            return;
        }
        sb.AppendLine();
        Indent(sb, indent);
        sb.AppendLine("{");
        if (typeDef.Fields != null) {
            foreach (var field in typeDef.Fields) {
                WriteStatement(sb, field, indent + 1);
            }
        }
        if (typeDef.Constructors != null) {
            foreach (var ctor in typeDef.Constructors) {
                WriteConstructorDefinition(sb, ctor, indent + 1, typeDef.Name);
            }
        }
        foreach (var prop in GetBodyProperties(typeDef)) {
            WriteStatement(sb, prop, indent + 1);
        }
        if (typeDef.Methods != null) {
            foreach (var method in typeDef.Methods) {
                WriteStatement(sb, method, indent + 1);
            }
        }
        Indent(sb, indent);
        sb.AppendLine("}");
    }

    private void WriteInterfaceMethodDefinition(StringBuilder sb, MethodDefinitionNode method, int indent) {
        Indent(sb, indent);
        WriteExpression(sb, method.ReturnType);
        sb.Append(' ');
        sb.Append(method.Name);
        sb.Append('(');
        if (method.Parameters != null) {
            WriteParameterDeclarations(sb, method.Parameters);
        }
        sb.AppendLine(");");
    }

    private void WriteInterfacePropertyDefinition(StringBuilder sb, PropertyDefinitionNode prop, int indent) {
        Indent(sb, indent);
        WriteExpression(sb, prop.MemberType);
        sb.Append(' ');
        sb.Append(prop.Name);
        sb.Append(" { ");
        if (prop.Getter != null) {
            sb.Append("get; ");
        }
        if (prop.Setter != null) {
            sb.Append("set; ");
        }
        sb.AppendLine("}");
    }

    private static bool TypeDefinitionHasBody(TypeDefinitionNode typeDef) {
        return (typeDef.Fields?.Count ?? 0) > 0
               || (typeDef.Constructors?.Count ?? 0) > 0
               || GetBodyProperties(typeDef).Count > 0
               || (typeDef.Methods?.Count ?? 0) > 0;
    }

    private static IReadOnlyList<PropertyDefinitionNode> GetBodyProperties(TypeDefinitionNode typeDef) {
        if (typeDef.Properties is null || !typeDef.EffectiveSemantics.HasValueEquality || typeDef.PrimaryConstructorParameters is not { Count: > 0 }) {
            return typeDef.Properties ?? [];
        }
        var primaryParameterNames = typeDef.PrimaryConstructorParameters
            .Where(static parameter => parameter.TypeReference is not null)
            .Select(static parameter => parameter.Name)
            .ToHashSet(StringComparer.Ordinal);
        return typeDef.Properties
            .Where(property => !primaryParameterNames.Contains(property.Name))
            .ToArray();
    }

    private void WriteMethodDefinition(StringBuilder sb, MethodDefinitionNode method, int indent) {
        WriteAttributes(sb, method.Attributes, indent);
        Indent(sb, indent);
        WriteAccessModifier(sb, method.AccessModifier);
        if (method.IsOverride) sb.Append("override ");
        if (method.IsStatic) sb.Append("static ");
        if (method.IsAsync) sb.Append("async ");
        WriteExpression(sb, method.ReturnType);
        sb.Append(' ');
        sb.Append(method.Name);
        sb.Append('(');
        if (method.Parameters != null) {
            WriteParameterDeclarations(sb, method.Parameters);
        }
        sb.Append(')');
        if (method.Body is Block { Nodes: [Return r] }) {
            if (r.Value is null) {
                sb.AppendLine(" { return; }");
                return;
            }
            sb.Append(" => ");
            WriteExpression(sb, r.Value);
            sb.AppendLine(";");
            return;
        }
        if (method.Body != null) {
            sb.AppendLine();
            WriteStatement(sb, method.Body, indent);
        }
        else {
            sb.AppendLine(";");
        }
    }

    private void WriteLocalFunction(StringBuilder sb, MethodDefinitionNode method) {
        if (method.IsStatic) sb.Append("static ");
        if (method.IsAsync) sb.Append("async ");
        WriteExpression(sb, method.ReturnType);
        sb.Append(' ');
        sb.Append(method.Name);
        sb.Append('(');
        if (method.Parameters != null) {
            WriteParameterDeclarations(sb, method.Parameters);
        }
        sb.Append(')');
        if (method.Body != null) {
            sb.AppendLine();
            WriteStatement(sb, method.Body, 0);
        }
        else {
            sb.AppendLine(";");
        }
        sb.AppendLine();
    }

    private void WriteConstructorDefinition(StringBuilder sb, ConstructorDefinitionNode ctor, int indent, string className) {
        Indent(sb, indent);
        WriteAccessModifier(sb, ctor.AccessModifier);
        sb.Append(className);
        sb.Append('(');
        if (ctor.Parameters != null) {
            WriteParameterDeclarations(sb, ctor.Parameters);
        }
        sb.Append(')');
        if (ctor.BaseConstructorInvocation != null) {
            sb.Append(" : ");
            WriteExpression(sb, ctor.BaseConstructorInvocation);
        }
        else if (ctor.BaseCall is { Count: > 0 }) {
            sb.Append(" : base(");
            for (int i = 0; i < ctor.BaseCall.Count; i++) {
                if (i > 0) sb.Append(", ");
                WriteExpression(sb, ctor.BaseCall[i]);
            }
            sb.Append(')');
        }
        if (ctor.Body != null) {
            sb.AppendLine();
            WriteStatement(sb, ctor.Body, indent);
        }
        else {
            sb.AppendLine(" { }");
        }
    }

    private void WritePropertyDefinition(StringBuilder sb, PropertyDefinitionNode prop, int indent) {
        WriteAttributes(sb, prop.Attributes, indent);
        Indent(sb, indent);
        WriteAccessModifier(sb, prop.AccessModifier);
        WriteExpression(sb, prop.MemberType);
        sb.Append(' ');
        sb.Append(prop.Name);
        if (IsAutoProperty(prop)) {
            sb.Append(" { ");
            if (prop.Getter != null) {
                sb.Append("get; ");
            }
            if (prop.Setter != null) {
                if (prop.Setter.AccessModifier.HasValue) {
                    WriteAccessModifier(sb, prop.Setter.AccessModifier.Value);
                }
                sb.Append("set; ");
            }
            else if (prop.Initializer != null) {
                sb.Append("init; ");
            }
            sb.Append('}');
            if (prop.Initializer?.Value is { } initializerValue) {
                sb.Append(" = ");
                WriteExpression(sb, initializerValue);
                sb.AppendLine(";");
            }
            else {
                sb.AppendLine();
            }
            return;
        }
        if (prop.Getter != null || prop.Setter != null) {
            sb.AppendLine();
            Indent(sb, indent);
            sb.AppendLine("{");
            if (prop.Getter != null) {
                Indent(sb, indent + 1);
                if (prop.Getter.Body != null) {
                    sb.Append("get => ");
                    WriteExpression(sb, prop.Getter.Body);
                    sb.AppendLine(";");
                }
                else {
                    sb.AppendLine("get;");
                }
            }
            if (prop.Setter != null) {
                Indent(sb, indent + 1);
                if (prop.Setter.AccessModifier.HasValue) {
                    WriteAccessModifier(sb, prop.Setter.AccessModifier.Value);
                }
                if (prop.Setter.Body != null) {
                    sb.Append("set => ");
                    WriteExpression(sb, prop.Setter.Body);
                    sb.AppendLine(";");
                }
                else {
                    sb.AppendLine("set;");
                }
            }
            Indent(sb, indent);
            sb.Append("}");
            if (prop.Initializer?.Value is { } initializerValue) {
                sb.Append(" = ");
                WriteExpression(sb, initializerValue);
                sb.AppendLine(";");
            }
            else {
                sb.AppendLine();
            }
        }
        else if (prop.Initializer?.Value is { } initializerValue) {
            sb.Append(" = ");
            WriteExpression(sb, initializerValue);
            sb.AppendLine(";");
        }
        else {
            sb.AppendLine(" { get; set; }");
        }
    }

    private static bool IsAutoProperty(PropertyDefinitionNode prop) {
        return (prop.Getter is null || prop.Getter.Body is null)
               && (prop.Setter is null || prop.Setter.Body is null)
               && (prop.Getter is not null || prop.Setter is not null);
    }

    private void WriteFieldDefinition(StringBuilder sb, FieldDefinitionNode field, int indent) {
        WriteAttributes(sb, field.Attributes, indent);
        Indent(sb, indent);
        WriteAccessModifier(sb, field.AccessModifier);
        if (field.IsStatic) sb.Append("static ");
        var mut = field.Mutability;
        if (mut.HasFlag(Mutability.CompileTimeConst)) sb.Append("const ");
        else if (mut.HasFlag(Mutability.ReadOnlyAfterInit)) sb.Append("readonly ");
        if (mut.HasFlag(Mutability.VolatileAccess)) sb.Append("volatile ");
        WriteExpression(sb, field.FieldType);
        sb.Append(' ');
        sb.Append(field.Name);
        if (field.DefaultValue != null) {
            sb.Append(" = ");
            WriteExpression(sb, field.DefaultValue);
        }
        sb.AppendLine(";");
    }

    private void WriteTypeLineage(StringBuilder sb, TypeDefinitionNode typeDef) {
        var lineage = Enumerable.Empty<Node>()
            .Concat(typeDef.BaseType is null ? [] : [typeDef.BaseType])
            .Concat(typeDef.Interfaces ?? [])
            .ToArray();
        if (lineage.Length == 0) {
            return;
        }
        sb.Append(" : ");
        WriteCommaSeparated(sb, lineage);
    }

    private void WriteParameterDeclarations(StringBuilder sb, IReadOnlyList<Parameter> parameters) {
        var first = true;
        foreach (var param in parameters) {
            if (!first) sb.Append(", ");
            if (param.TypeReference != null) {
                WriteExpression(sb, param.TypeReference);
                sb.Append(' ');
            }
            sb.Append(param.Name);
            if (param.DefaultValue != null) {
                sb.Append(" = ");
                WriteExpression(sb, param.DefaultValue);
            }
            first = false;
        }
    }

    private void WriteExpression(StringBuilder sb, Node node) {
        switch (node) {
            case Constant constant:
                WriteConstant(sb, constant);
                return;
            case Variable variable:
                sb.Append(variable.Name);
                return;
            case Parameter parameter:
                sb.Append(parameter.Name);
                return;
            case ThisReference:
                sb.Append("this");
                return;
            case TypeOf typeOf:
                sb.Append("typeof(");
                WriteExpression(sb, typeOf.Type);
                sb.Append(')');
                return;
            case TypeReference typeRef:
                sb.Append(typeRef.TypeName);
                return;
            case PrimitiveTypeReference prim:
                sb.Append(prim.PrimitiveId.GetCSharpKeyword());
                if (prim.IsNullable) sb.Append('?');
                return;
            case NamedTypeReference named:
                sb.Append(named.TypeName);
                if (named.TypeArguments is { Count: > 0 }) {
                    sb.Append('<');
                    WriteCommaSeparated(sb, named.TypeArguments);
                    sb.Append('>');
                }
                return;
            case OptionalTypeReference opt:
                WriteExpression(sb, opt.InnerType);
                sb.Append('?');
                return;
            case CollectionTypeReference coll:
                if (coll.Kind == CollectionKind.Array) {
                    WriteExpression(sb, coll.ElementType);
                    sb.Append("[]");
                }
                else {
                    sb.Append(coll.Kind == CollectionKind.Set ? "ISet<" : "List<");
                    WriteExpression(sb, coll.ElementType);
                    sb.Append('>');
                }
                return;
            case MapTypeReference map:
                sb.Append("Dictionary<");
                WriteExpression(sb, map.KeyType);
                sb.Append(", ");
                WriteExpression(sb, map.ValueType);
                sb.Append('>');
                return;
            case UnaryMinus minus:
                sb.Append('-');
                WriteExpression(sb, minus.Operand);
                return;
            case Not not:
                sb.Append('!');
                var needsParens = not.Value is not (Constant or Variable or Parameter or ThisReference
                    or Member or IndexAccess or Invoke or New
                    or TypeCast or TypeIs or TypeAs);
                if (needsParens) sb.Append('(');
                WriteExpression(sb, not.Value);
                if (needsParens) sb.Append(')');
                return;
            case NullForgiving nf:
                WriteExpression(sb, nf.Operand);
                sb.Append('!');
                return;
            case Default def:
                sb.Append("default");
                if (def.TargetType is not null) {
                    sb.Append('(');
                    WriteExpression(sb, def.TargetType);
                    sb.Append(')');
                }
                return;
            case Add add:
                WriteBinary(sb, add.LeftHandValue, " + ", add.RightHandValue);
                return;
            case Subtract sub:
                WriteBinary(sb, sub.LeftHandValue, " - ", sub.RightHandValue);
                return;
            case Multiply mul:
                WriteBinary(sb, mul.LeftHandValue, " * ", mul.RightHandValue);
                return;
            case Divide div:
                WriteBinary(sb, div.LeftHandValue, " / ", div.RightHandValue);
                return;
            case Modulo mod:
                WriteBinary(sb, mod.LeftHandValue, " % ", mod.RightHandValue);
                return;
            case Equal eq:
                WriteBinary(sb, eq.LeftHandValue, " == ", eq.RightHandValue);
                return;
            case NotEqual neq:
                WriteBinary(sb, neq.LeftHandValue, " != ", neq.RightHandValue);
                return;
            case LessThan lt:
                WriteBinary(sb, lt.LeftHandValue, " < ", lt.RightHandValue);
                return;
            case LessThanOrEqual lte:
                WriteBinary(sb, lte.LeftHandValue, " <= ", lte.RightHandValue);
                return;
            case GreaterThan gt:
                WriteBinary(sb, gt.LeftHandValue, " > ", gt.RightHandValue);
                return;
            case GreaterThanOrEqual gte:
                WriteBinary(sb, gte.LeftHandValue, " >= ", gte.RightHandValue);
                return;
            case And and:
                WriteBinary(sb, and.LeftHandValue, " && ", and.RightHandValue);
                return;
            case Or or:
                WriteBinary(sb, or.LeftHandValue, " || ", or.RightHandValue);
                return;
            case Coalesce coalesce:
                WriteBinary(sb, coalesce.LeftHandValue, " ?? ", coalesce.RightHandValue);
                return;
            case ThrowExpression throwExpression:
                sb.Append("throw ");
                WriteExpression(sb, throwExpression.Value);
                return;
            case Conditional cond:
                sb.Append('(');
                WriteExpression(sb, cond.Condition);
                sb.Append(" ? ");
                WriteExpression(sb, cond.IfTrue);
                sb.Append(" : ");
                WriteExpression(sb, cond.IfFalse);
                sb.Append(')');
                return;
            case Assignment assign:
                WriteExpression(sb, assign.Destination);
                sb.Append(" = ");
                WriteExpression(sb, assign.Value);
                return;
            case Member member:
                if (member.Value is ParameterReference) {
                    sb.Append(member.MemberName);
                }
                else {
                    var memberNeedsParens = member.Value is TypeCast or TypeAs or Conditional or Coalesce;
                    if (memberNeedsParens) sb.Append('(');
                    WriteExpression(sb, member.Value);
                    if (memberNeedsParens) sb.Append(')');
                    sb.Append('.');
                    sb.Append(member.MemberName);
                }
                return;
            case IndexAccess index:
                WriteExpression(sb, index.Value);
                sb.Append('[');
                WriteCommaSeparated(sb, index.Arguments);
                sb.Append(']');
                return;
            case Await awaitNode:
                sb.Append("await ");
                WriteExpression(sb, awaitNode.Operand);
                return;
            case Invoke invoke:
                WriteExpression(sb, invoke.Delegate);
                if (invoke.TypeArguments is { Count: > 0 }) {
                    sb.Append('<');
                    WriteCommaSeparated(sb, invoke.TypeArguments);
                    sb.Append('>');
                }
                sb.Append('(');
                WriteCommaSeparated(sb, invoke.Arguments);
                sb.Append(')');
                return;
            case New @new:
                sb.Append("new ");
                WriteExpression(sb, @new.Type);
                sb.Append('(');
                WriteCommaSeparated(sb, @new.Arguments);
                sb.Append(')');
                return;
            case TypeCast cast:
                sb.Append('(');
                WriteExpression(sb, cast.TargetTypeReference);
                sb.Append(')');
                WriteExpression(sb, cast.Operand);
                return;
            case TypeIs typeIs:
                WriteExpression(sb, typeIs.Operand);
                sb.Append(" is ");
                WriteExpression(sb, typeIs.TargetTypeReference);
                if (typeIs.VariableName != null) {
                    sb.Append(' ');
                    sb.Append(typeIs.VariableName);
                }
                return;
            case TypeAs typeAs:
                WriteExpression(sb, typeAs.Operand);
                sb.Append(" as ");
                WriteExpression(sb, typeAs.TargetTypeReference);
                return;
            case Lambda lambda:
                WriteLambda(sb, lambda);
                return;
            case Block block:
                sb.Append('{');
                if (block.Nodes.Count == 0 && block.Variables.Count == 0) {
                    sb.Append(' ');
                }
                else {
                    for (int i = 0; i < block.Nodes.Count; i++) {
                        var n = block.Nodes[i];
                        if (i == block.Nodes.Count - 1 || _analysisResult == null || !_analysisResult.CanElide(n)) {
                            sb.Append(' ');
                            WriteStatement(sb, n, 0);
                        }
                    }
                    sb.Append(' ');
                }
                sb.Append('}');
                return;
            case BitwiseAnd bitwiseAnd:
                WriteBinary(sb, bitwiseAnd.LeftHandValue, " & ", bitwiseAnd.RightHandValue);
                return;
            case BitwiseOr bitwiseOr:
                WriteBinary(sb, bitwiseOr.LeftHandValue, " | ", bitwiseOr.RightHandValue);
                return;
            case BitwiseXor bitwiseXor:
                WriteBinary(sb, bitwiseXor.LeftHandValue, " ^ ", bitwiseXor.RightHandValue);
                return;
            case BitwiseNot bitwiseNot:
                sb.Append('~');
                WriteExpression(sb, bitwiseNot.Operand);
                return;
            case ShiftLeft shiftLeft:
                WriteBinary(sb, shiftLeft.LeftHandValue, " << ", shiftLeft.RightHandValue);
                return;
            case ShiftRight shiftRight:
                WriteBinary(sb, shiftRight.LeftHandValue, " >> ", shiftRight.RightHandValue);
                return;
            case PopCount popCount:
                sb.Append("System.Numerics.BitOperations.PopCount((ulong)");
                WriteExpression(sb, popCount.Operand);
                sb.Append(')');
                return;
            case StridedSetBits:
                sb.Append("/* StridedSetBits */");
                return;
            case NewArray newArray:
                sb.Append('(');
                WriteExpression(sb, newArray.ElementType);
                sb.Append("[])(new ");
                WriteExpression(sb, newArray.ElementType);
                sb.Append('[');
                WriteExpression(sb, newArray.Length);
                sb.Append("])");
                return;
            case SuspendNode suspend:
                WriteExpression(sb, suspend.Inner);
                return;
            case BaseConstructorInvocationNode baseCtor:
                sb.Append("base(");
                WriteCommaSeparated(sb, baseCtor.Arguments);
                sb.Append(')');
                return;
            case Comment c:
                sb.Append(c.Text.EndsWith('.') ? $"/* {c.Text} */" : $"/* {c.Text} */");
                return;
            default:
                sb.Append(node.ToString());
                return;
        }
    }

    private void WriteConstant(StringBuilder sb, Constant constant) {
        if (constant.Value == null) {
            sb.Append("null");
        }
        else if (constant.Value is string s) {
            sb.Append('"');
            sb.Append(s.Replace("\\", "\\\\").Replace("\"", "\\\""));
            sb.Append('"');
        }
        else if (constant.Value is char c) {
            sb.Append('\'');
            if (c == '\'') sb.Append("\\'");
            else if (c == '\\') sb.Append("\\\\");
            else sb.Append(c);
            sb.Append('\'');
        }
        else if (constant.Value is bool b) {
            sb.Append(b ? "true" : "false");
        }
        else if (constant.Value is float f) {
            sb.Append(f.ToString("G"));
            sb.Append('f');
        }
        else if (constant.Value is double d) {
            sb.Append(d.ToString("G"));
        }
        else if (constant.Value is decimal m) {
            sb.Append(m.ToString("G"));
            sb.Append('m');
        }
        else if (constant.Value is long l) {
            sb.Append(l.ToString());
            sb.Append('L');
        }
        else if (constant.Value is uint ui) {
            sb.Append(ui.ToString());
            sb.Append("u");
        }
        else if (constant.Value is ulong ul) {
            sb.Append(ul.ToString());
            sb.Append("UL");
        }
        else {
            sb.Append(constant.Value.ToString());
        }
    }

    private void WriteBinary(StringBuilder sb, Node left, string op, Node right) {
        WriteBinaryOperand(sb, left);
        sb.Append(op);
        WriteBinaryOperand(sb, right);
    }

    private void WriteBinaryOperand(StringBuilder sb, Node node) {
        if (node is Coalesce or Conditional) { sb.Append('('); WriteExpression(sb, node); sb.Append(')'); return; }
        if (node is Or) { sb.Append('('); WriteExpression(sb, node); sb.Append(')'); return; }
        WriteExpression(sb, node);
    }

    private void WriteLambda(StringBuilder sb, Lambda lambda) {
        if (BodyContainsAwait(lambda.Body))
            sb.Append("async ");
        if (lambda.Parameters.Count == 1 && lambda.Parameters[0].TypeReference is null) {
            WriteExpression(sb, lambda.Parameters[0]);
        }
        else {
            sb.Append('(');
            for (int i = 0; i < lambda.Parameters.Count; i++) {
                if (i > 0) sb.Append(", ");
                var p = lambda.Parameters[i];
                if (p.TypeReference != null) {
                    WriteExpression(sb, p.TypeReference);
                    sb.Append(' ');
                }
                sb.Append(p.Name);
            }
            sb.Append(')');
        }
        sb.Append(" => ");
        WriteExpression(sb, lambda.Body);
    }

    private static bool BodyContainsAwait(Node node) {
        if (node is Await) return true;
        foreach (var child in node.Children)
            if (child != null && BodyContainsAwait(child))
                return true;
        return false;
    }

    private void WriteCommaSeparated(StringBuilder sb, IEnumerable<Node> nodes) {
        var first = true;
        foreach (var node in nodes) {
            if (!first) sb.Append(", ");
            WriteExpression(sb, node);
            first = false;
        }
    }

    private void WriteAttributes(StringBuilder sb, IReadOnlyList<AttributeNode> attributes, int indent) {
        foreach (var attr in attributes) {
            Indent(sb, indent);
            sb.Append('[');
            sb.Append(attr.Name);
            if (attr.Arguments.Count > 0) {
                sb.Append('(');
                WriteCommaSeparated(sb, attr.Arguments);
                sb.Append(')');
            }
            sb.AppendLine("]");
        }
    }

    private static void WriteAccessModifier(StringBuilder sb, AccessModifier modifier) {
        switch (modifier) {
            case AccessModifier.Public: sb.Append("public "); break;
            case AccessModifier.Private: sb.Append("private "); break;
            case AccessModifier.Internal: sb.Append("internal "); break;
            case AccessModifier.Protected: sb.Append("protected "); break;
        }
    }

    private static void Indent(StringBuilder sb, int level) {
        for (int i = 0; i < level; i++) {
            sb.Append("    ");
        }
    }
}