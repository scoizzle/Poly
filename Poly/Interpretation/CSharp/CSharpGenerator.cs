namespace Poly.Interpretation.CSharp;

public sealed class CSharpGenerator {
    private readonly AnalysisResult? _analysisResult;

    public CSharpGenerator() {
    }

    public CSharpGenerator(AnalysisResult analysisResult) {
        ArgumentNullException.ThrowIfNull(analysisResult);
        _analysisResult = analysisResult;
    }

    public string Generate(Node node) {
        ArgumentNullException.ThrowIfNull(node);
        var sb = new StringBuilder();
        WriteStatement(sb, node, 0);
        return sb.ToString().TrimEnd();
    }

    public string Generate(IReadOnlyList<TypeDefinitionNode> typeDefs) {
        return Generate(typeDefs, testStatements: null);
    }

    public string Generate(IReadOnlyList<TypeDefinitionNode> typeDefs, IReadOnlyList<Node>? testStatements) {
        ArgumentNullException.ThrowIfNull(typeDefs);
        var sb = new StringBuilder();
        if (testStatements is not null) {
            foreach (var stmt in testStatements) {
                WriteStatement(sb, stmt, 0);
                sb.AppendLine();
            }
            sb.AppendLine();
        }
        else {
            WriteTestTopLevelStatement(sb, typeDefs);
        }
        foreach (var typeDef in typeDefs) {
            WriteStatement(sb, typeDef, 0);
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private void WriteTestTopLevelStatement(StringBuilder sb, IReadOnlyList<TypeDefinitionNode> typeDefs) {
        var testType = typeDefs.FirstOrDefault(t => t.Constructors?.Count > 0 && (t.Fields?.Count ?? 0) == 0 && t.Methods?.Any(m => (m.Parameters is null or []) && !m.IsStatic) == true);
        if (testType is null) {
            sb.AppendLine("Console.WriteLine(\"OK\");");
            sb.AppendLine();
            return;
        }

        var ctor = testType.Constructors![0];
        sb.Append("var _test = new ");
        sb.Append(testType.Name);
        sb.Append('(');
        if (ctor.Parameters != null) {
            for (int i = 0; i < ctor.Parameters.Count; i++) {
                if (i > 0) sb.Append(", ");
                WriteDefaultValue(sb, ctor.Parameters[i].TypeReference);
            }
        }
        sb.AppendLine(");");

        var method = testType.Methods!.First(m => (m.Parameters is null or []) && !m.IsStatic);
        sb.Append("_test.");
        sb.Append(method.Name);
        sb.AppendLine("();");

        sb.Append("Console.WriteLine(\"Test passed: ");
        sb.Append(testType.Name);
        sb.Append('.');
        sb.Append(method.Name);
        sb.AppendLine(" executed.\");");
        sb.AppendLine();
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
            default:
                Indent(sb, indent);
                WriteExpression(sb, node);
                sb.AppendLine(";");
                return;
        }
    }

    private void WriteBlock(StringBuilder sb, Block block, int indent) {
        Indent(sb, indent);
        sb.AppendLine("{");

        foreach (var v in block.Variables) {
            WriteStatement(sb, v, indent + 1);
        }

        foreach (var node in block.Nodes) {
            WriteStatement(sb, node, indent + 1);
        }

        Indent(sb, indent);
        sb.AppendLine("}");
    }

    private void WriteIfStatement(StringBuilder sb, IfStatement ifStmt, int indent) {
        Indent(sb, indent);
        sb.Append("if (");
        WriteExpression(sb, ifStmt.Condition);
        sb.AppendLine(")");

        WriteStatement(sb, ifStmt.ThenBranch, ifStmt.ThenBranch is Block ? indent : indent + 1);

        if (ifStmt.ElseBranch != null) {
            Indent(sb, indent);
            sb.AppendLine("else");
            WriteStatement(sb, ifStmt.ElseBranch, ifStmt.ElseBranch is Block ? indent : indent + 1);
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
        if (forLoop.Initializer != null) {
            WriteExpression(sb, forLoop.Initializer);
        }
        sb.Append("; ");
        if (forLoop.Condition != null) {
            WriteExpression(sb, forLoop.Condition);
        }
        sb.Append("; ");
        if (forLoop.Increment != null) {
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
        WriteStatement(sb, tryCatch.TryBlock, indent);

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
                WriteStatement(sb, clause.Body, indent);
            }
        }

        if (tryCatch.FinallyBlock != null) {
            Indent(sb, indent);
            sb.AppendLine("finally");
            WriteStatement(sb, tryCatch.FinallyBlock, indent);
        }
    }

    private void WriteUsingStatement(StringBuilder sb, UsingStatement usingStmt, int indent) {
        Indent(sb, indent);
        sb.Append("using (");
        WriteExpression(sb, usingStmt.Resource);
        sb.AppendLine(")");
        WriteStatement(sb, usingStmt.Body, indent);
    }

    private void WriteTypeDefinition(StringBuilder sb, TypeDefinitionNode typeDef, int indent) {
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

        Indent(sb, indent);
        WriteAccessModifier(sb, typeDef.AccessModifier);
        sb.Append("class ");
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

        if (typeDef.Properties != null) {
            foreach (var prop in typeDef.Properties) {
                WriteStatement(sb, prop, indent + 1);
            }
        }

        if (typeDef.Methods != null) {
            foreach (var method in typeDef.Methods) {
                WriteStatement(sb, method, indent + 1);
            }
        }

        Indent(sb, indent);
        sb.AppendLine("}");
    }

    private void WriteMethodDefinition(StringBuilder sb, MethodDefinitionNode method, int indent) {
        Indent(sb, indent);
        WriteAccessModifier(sb, method.AccessModifier);
        if (method.IsStatic) sb.Append("static ");
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
            WriteStatement(sb, method.Body, indent);
        }
        else {
            sb.AppendLine(";");
        }
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
        if (ctor.Body != null) {
            sb.AppendLine();
            WriteStatement(sb, ctor.Body, indent);
        }
        else {
            sb.AppendLine(";");
        }
    }

    private void WritePropertyDefinition(StringBuilder sb, PropertyDefinitionNode prop, int indent) {
        Indent(sb, indent);
        WriteAccessModifier(sb, prop.AccessModifier);
        WriteExpression(sb, prop.MemberType);
        sb.Append(' ');
        sb.Append(prop.Name);

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
            sb.AppendLine("}");
        }
        else if (prop.Initializer != null) {
            sb.Append(" = ");
            WriteExpression(sb, prop.Initializer.Value);
            sb.AppendLine(";");
        }
        else {
            sb.AppendLine(" { get; set; }");
        }
    }

    private void WriteFieldDefinition(StringBuilder sb, FieldDefinitionNode field, int indent) {
        Indent(sb, indent);
        WriteAccessModifier(sb, field.AccessModifier);
        if (field.IsStatic) sb.Append("static ");
        if (field.IsReadOnly) sb.Append("readonly ");
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
            case TypeReference typeRef:
                sb.Append(typeRef.TypeName);
                return;
            case PrimitiveTypeReference prim:
                sb.Append(prim.PrimitiveId.GetDisplayName());
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

            // Unary
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

            // Binary arithmetic
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

            // Comparison
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

            // Boolean
            case And and:
                WriteBinary(sb, and.LeftHandValue, " && ", and.RightHandValue);
                return;
            case Or or:
                WriteBinary(sb, or.LeftHandValue, " || ", or.RightHandValue);
                return;

            // Coalesce
            case Coalesce coalesce:
                WriteBinary(sb, coalesce.LeftHandValue, " ?? ", coalesce.RightHandValue);
                return;

            // Conditional (ternary)
            case Conditional cond:
                sb.Append('(');
                WriteExpression(sb, cond.Condition);
                sb.Append(" ? ");
                WriteExpression(sb, cond.IfTrue);
                sb.Append(" : ");
                WriteExpression(sb, cond.IfFalse);
                sb.Append(')');
                return;

            // Assignment
            case Assignment assign:
                WriteExpression(sb, assign.Destination);
                sb.Append(" = ");
                WriteExpression(sb, assign.Value);
                return;

            // Member access
            case Member member:
                if (member.Value is ParameterReference) {
                    sb.Append(member.MemberName);
                }
                else {
                    WriteExpression(sb, member.Value);
                    sb.Append('.');
                    sb.Append(member.MemberName);
                }
                return;

            // Index access
            case IndexAccess index:
                WriteExpression(sb, index.Value);
                sb.Append('[');
                WriteCommaSeparated(sb, index.Arguments);
                sb.Append(']');
                return;

            // Invocation
            case Invoke invoke:
                WriteExpression(sb, invoke.Delegate);
                sb.Append('(');
                WriteCommaSeparated(sb, invoke.Arguments);
                sb.Append(')');
                return;

            // Constructor
            case New @new:
                sb.Append("new ");
                WriteExpression(sb, @new.Type);
                sb.Append('(');
                WriteCommaSeparated(sb, @new.Arguments);
                sb.Append(')');
                return;

            // Type operations
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
                return;
            case TypeAs typeAs:
                WriteExpression(sb, typeAs.Operand);
                sb.Append(" as ");
                WriteExpression(sb, typeAs.TargetTypeReference);
                return;

            // Lambda
            case Lambda lambda:
                WriteLambda(sb, lambda);
                return;

            // Block used inline (e.g., as lambda body) - inline format
            case Block block:
                sb.Append('{');
                for (int i = 0; i < block.Nodes.Count; i++) {
                    if (i > 0) sb.Append(' ');
                    WriteExpression(sb, block.Nodes[i]);
                }
                sb.Append('}');
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
        WriteExpression(sb, left);
        sb.Append(op);
        WriteExpression(sb, right);
    }

    private void WriteLambda(StringBuilder sb, Lambda lambda) {
        if (lambda.Parameters.Count == 1) {
            WriteExpression(sb, lambda.Parameters[0]);
        }
        else {
            sb.Append('(');
            WriteCommaSeparated(sb, lambda.Parameters.Cast<Node>());
            sb.Append(')');
        }
        sb.Append(" => ");
        WriteExpression(sb, lambda.Body);
    }

    private void WriteCommaSeparated(StringBuilder sb, IEnumerable<Node> nodes) {
        var first = true;
        foreach (var node in nodes) {
            if (!first) sb.Append(", ");
            WriteExpression(sb, node);
            first = false;
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