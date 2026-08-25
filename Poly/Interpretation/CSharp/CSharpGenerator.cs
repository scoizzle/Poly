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
                // Top-level method: emit as local function without access modifier
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
