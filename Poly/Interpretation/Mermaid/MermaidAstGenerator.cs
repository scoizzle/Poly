using System.Text;

using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.AbstractSyntaxTree.Arithmetic;
using Poly.Interpretation.AbstractSyntaxTree.Boolean;
using Poly.Interpretation.AbstractSyntaxTree.Comparison;
using Poly.Interpretation.AbstractSyntaxTree.Equality;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;

namespace Poly.Interpretation.Mermaid;

/// <summary>
/// Generates Mermaid flowchart diagrams from analyzed AST nodes for visualization purposes.
/// </summary>
/// <remarks>
/// This class produces Mermaid markdown syntax that can be rendered in documentation,
/// GitHub/GitLab, or VS Code extensions to visualize the structure of abstract syntax trees.
/// </remarks>
public sealed class MermaidAstGenerator {
    private readonly AnalysisResult? _analysisResult;
    private readonly StringBuilder _output;
    private readonly HashSet<string> _visitedEdges;
    private readonly StringBuilder _scratch;

    /// <summary>
    /// Initializes a new instance without analysis metadata.
    /// </summary>
    public MermaidAstGenerator()
    {
        _output = new StringBuilder();
        _visitedEdges = new HashSet<string>();
        _scratch = new StringBuilder();
    }

    /// <summary>
    /// Initializes a new instance with semantic analysis results for enhanced output.
    /// </summary>
    /// <param name="analysisResult">The semantic analysis result containing type information.</param>
    public MermaidAstGenerator(AnalysisResult analysisResult)
    {
        ArgumentNullException.ThrowIfNull(analysisResult);
        _analysisResult = analysisResult;
        _output = new StringBuilder();
        _visitedEdges = new HashSet<string>();
        _scratch = new StringBuilder();
    }

    /// <summary>
    /// Generates a Mermaid flowchart diagram from an AST node.
    /// </summary>
    /// <param name="node">The root node to visualize.</param>
    /// <param name="direction">The flow direction: TB (top-bottom), LR (left-right), etc.</param>
    /// <returns>Mermaid markdown syntax as a string.</returns>
    public string Generate(Node node, string direction = "TB")
    {
        ArgumentNullException.ThrowIfNull(node);

        _output.Clear();
        _visitedEdges.Clear();

        _output.AppendLine($"graph {direction}");

        // First, collect and generate all Parameter nodes to put them at the beginning
        var parameterNodes = CollectParameterNodes(node);
        foreach (var paramNode in parameterNodes) {
            var nodeId = GetNodeId(paramNode);
            var shape = GetNodeShape(paramNode);
            AppendNodeDefinition(paramNode, nodeId, shape);

            // Add styling annotations if analysis result is available
            if (_analysisResult != null) {
                AddStyleAnnotations(nodeId, paramNode);
            }
        }

        GenerateNode(node);

        return _output.ToString();
    }

    private List<Parameter> CollectParameterNodes(Node node)
    {
        var parameters = new List<Parameter>();
        var visited = new HashSet<Node>();

        void Visit(Node n)
        {
            if (!visited.Add(n)) {
                return; // Already visited
            }

            if (n is Parameter param) {
                parameters.Add(param);
            }

            // Visit all children
            foreach (var (child, _) in GetChildren(n)) {
                Visit(child);
            }
        }

        Visit(node);
        return parameters;
    }

    private string GenerateNode(Node node)
    {
        var nodeId = GetNodeId(node);

        // Skip definition for Parameter nodes as they were already defined at the beginning
        if (!(node is Parameter)) {
            var shape = GetNodeShape(node);
            AppendNodeDefinition(node, nodeId, shape);

            // Add styling annotations if analysis result is available
            if (_analysisResult != null) {
                AddStyleAnnotations(nodeId, node);
            }
        }

        // Process children
        foreach (var (child, edgeLabel) in GetChildren(node)) {
            var childId = GenerateNode(child);
            var edgeKey = $"{nodeId}->{childId}";

            if (!_visitedEdges.Contains(edgeKey)) {
                _visitedEdges.Add(edgeKey);

                if (!string.IsNullOrEmpty(edgeLabel)) {
                    _output.AppendLine($"    {nodeId} -->|{edgeLabel}| {childId}");
                }
                else {
                    _output.AppendLine($"    {nodeId} --> {childId}");
                }
            }
        }

        return nodeId;
    }

    private void AppendNodeDefinition(Node node, string nodeId, NodeShape shape)
    {
        _scratch.Clear();
        // Add type information to label if available
        if (_analysisResult != null) {
            var resolvedType = _analysisResult.GetResolvedType(node);
            if (resolvedType != null) {
                AppendTypeName(_scratch, resolvedType);
                _scratch.Append(' ');
            }
        }

        AppendNodeLabel(_scratch, node);

        _output.Append("    ");
        _output.Append(nodeId);

        switch (shape) {
            case NodeShape.Rectangle:
                _output.Append("[\"");
                _output.Append(_scratch);
                _output.Append("\"]");
                break;
            case NodeShape.RoundedRectangle:
                _output.Append("(\"");
                _output.Append(_scratch);
                _output.Append("\")");
                break;
            case NodeShape.Circle:
                _output.Append("((\"");
                _output.Append(_scratch);
                _output.Append("\"))");
                break;
            case NodeShape.Rhombus:
                _output.Append("{\"");
                _output.Append(_scratch);
                _output.Append("\"}");
                break;
            case NodeShape.Hexagon:
                _output.Append("{{\"");
                _output.Append(_scratch);
                _output.Append("\"}}}");
                break;
            default:
                _output.Append("[\"");
                _output.Append(_scratch);
                _output.Append("\"]");
                break;
        }

        _output.AppendLine();
    }

    private void AddStyleAnnotations(string nodeId, Node node)
    {
        if (_analysisResult == null) {
            return;
        }

        // Check for diagnostics related to this node
        var nodeDiagnostics = _analysisResult.Diagnostics
            .Where(d => d.Node.Id == node.Id)
            .ToList();

        if (nodeDiagnostics.Count > 0) {
            // Apply error/warning styling
            var severity = nodeDiagnostics.Max(d => d.Severity);
            var styleColor = severity switch {
                DiagnosticSeverity.Error => "fill:#ffcccc,stroke:#cc0000,stroke-width:3px",
                DiagnosticSeverity.Warning => "fill:#fff4cc,stroke:#ff9900,stroke-width:2px",
                _ => "fill:#e6f3ff,stroke:#0066cc,stroke-width:1px"
            };
            _output.AppendLine($"    style {nodeId} {styleColor}");

            // Add diagnostic notes
            foreach (var diagnostic in nodeDiagnostics.Take(1)) // Show first diagnostic
            {
                var diagId = $"{nodeId}_diag";
                var message = diagnostic.Message.Replace("\"", "'");
                _output.AppendLine($"    {diagId}[\"⚠ {message}\"]");
                _output.AppendLine($"    {nodeId} -.- {diagId}");
                _output.AppendLine($"    style {diagId} fill:#fff,stroke:#999,stroke-dasharray: 5 5");
            }
        }
        else {
            // Apply default styling based on node type
            string? styleColor = node switch {
                Constant => "fill:#e8f5e9,stroke:#4caf50",
                Parameter => "fill:#e3f2fd,stroke:#2196f3",
                Variable => "fill:#fff3e0,stroke:#ff9800",
                _ => null
            };

            if (styleColor != null) {
                _output.AppendLine($"    style {nodeId} {styleColor}");
            }
        }
    }

    private string GetNodeId(Node node)
    {
        // Use the node's stable NodeId instead of auto-generated counter
        return node.Id.Value;
    }

    private void AppendNodeLabel(StringBuilder builder, Node node)
    {
        switch (node) {
            // Leaf nodes with values
            case Constant constant:
                builder.Append("Constant ");
                AppendValue(builder, constant.Value);
                break;
            case Parameter param:
                builder.Append("Parameter ");
                builder.Append(param.Name);
                break;
            case Variable variable:
                builder.Append("Variable ");
                builder.Append(variable.Name);
                break;

            // Binary arithmetic
            case Add:
                builder.Append("Add (+)");
                break;
            case Subtract:
                builder.Append("Subtract (-)");
                break;
            case Multiply:
                builder.Append("Multiply (*)");
                break;
            case Divide:
                builder.Append("Divide (/)");
                break;
            case Modulo:
                builder.Append("Modulo (%)");
                break;

            // Unary operations
            case UnaryMinus:
                builder.Append("Negate (-)");
                break;
            case Not:
                builder.Append("Not (!)");
                break;

            // Comparison
            case Equal:
                builder.Append("Equal (==)");
                break;
            case NotEqual:
                builder.Append("Not Equal (!=)");
                break;
            case LessThan:
                builder.Append("Less Than (<)");
                break;
            case LessThanOrEqual:
                builder.Append("Less Than or Equal (<=)");
                break;
            case GreaterThan:
                builder.Append("Greater Than (>)");
                break;
            case GreaterThanOrEqual:
                builder.Append("Greater Than or Equal (>=)");
                break;

            // Boolean operations
            case And:
                builder.Append("And (&&)");
                break;
            case Or:
                builder.Append("Or (||)");
                break;

            // Other operations
            case Conditional:
                builder.Append("Conditional (?:)");
                break;
            case Coalesce:
                builder.Append("Coalesce (??)");
                break;
            case TypeCast cast:
                builder.Append("Cast to ");
                builder.Append(cast.TargetTypeReference);
                break;
            case MemberAccess member:
                builder.Append("Member Access .");
                builder.Append(member.MemberName);
                break;
            case IndexAccess:
                builder.Append("Index Access");
                break;
            case MethodInvocation method:
                builder.Append("Method Call ");
                builder.Append(method.MethodName);
                builder.Append("()");
                break;

            // Control flow
            case Block:
                builder.Append("Block");
                break;
            case IfStatement:
                builder.Append("If Statement");
                break;
            case WhileLoop:
                builder.Append("While Loop");
                break;
            case DoWhileLoop:
                builder.Append("Do-While Loop");
                break;
            case ForLoop:
                builder.Append("For Loop");
                break;
            case SwitchStatement:
                builder.Append("Switch");
                break;

            // Assignments
            case Assignment:
                builder.Append("Assignment (=)");
                break;

            // Jumps
            case BreakStatement:
                builder.Append("Break");
                break;
            case ContinueStatement:
                builder.Append("Continue");
                break;
            case ReturnStatement:
                builder.Append("Return");
                break;
            case GotoStatement goto_:
                builder.Append("Goto ");
                builder.Append(goto_.Target);
                break;
            case LabelDeclaration label:
                builder.Append("Label ");
                builder.Append(label.Name);
                break;

            // Exception handling
            case ThrowStatement:
                builder.Append("Throw");
                break;
            case TryCatchFinally:
                builder.Append("Try-Catch-Finally");
                break;

            // Resource management
            case UsingStatement:
                builder.Append("Using");
                break;

            default:
                builder.Append(node.GetType().Name);
                break;
        }
    }

    private NodeShape GetNodeShape(Node node)
    {
        return node switch {
            // Leaf nodes - rounded rectangles
            Constant or Parameter or Variable => NodeShape.RoundedRectangle,

            // Conditionals - rhombus
            Conditional or IfStatement or SwitchStatement => NodeShape.Rhombus,

            // Loops - hexagon
            WhileLoop or DoWhileLoop or ForLoop => NodeShape.Hexagon,

            // Operations - default rectangle
            _ => NodeShape.Rectangle
        };
    }

    private IEnumerable<(Node Child, string EdgeLabel)> GetChildren(Node node)
    {
        return node switch {
            // Binary operations
            Add add => new[] { (add.LeftHandValue, "left"), (add.RightHandValue, "right") },
            Subtract sub => new[] { (sub.LeftHandValue, "left"), (sub.RightHandValue, "right") },
            Multiply mul => new[] { (mul.LeftHandValue, "left"), (mul.RightHandValue, "right") },
            Divide div => new[] { (div.LeftHandValue, "left"), (div.RightHandValue, "right") },
            Modulo mod => new[] { (mod.LeftHandValue, "left"), (mod.RightHandValue, "right") },

            Equal eq => new[] { (eq.LeftHandValue, "left"), (eq.RightHandValue, "right") },
            NotEqual neq => new[] { (neq.LeftHandValue, "left"), (neq.RightHandValue, "right") },
            LessThan lt => new[] { (lt.LeftHandValue, "left"), (lt.RightHandValue, "right") },
            LessThanOrEqual lte => new[] { (lte.LeftHandValue, "left"), (lte.RightHandValue, "right") },
            GreaterThan gt => new[] { (gt.LeftHandValue, "left"), (gt.RightHandValue, "right") },
            GreaterThanOrEqual gte => new[] { (gte.LeftHandValue, "left"), (gte.RightHandValue, "right") },

            And and => new[] { (and.LeftHandValue, "left"), (and.RightHandValue, "right") },
            Or or => new[] { (or.LeftHandValue, "left"), (or.RightHandValue, "right") },

            Coalesce coalesce => new[] { (coalesce.LeftHandValue, "value"), (coalesce.RightHandValue, "default") },

            // Unary operations
            UnaryMinus minus => new[] { (minus.Operand, "") },
            Not not => new[] { (not.Value, "") },

            // Conditional
            Conditional cond => new[] {
                (cond.Condition, "condition"),
                (cond.IfTrue, "true"),
                (cond.IfFalse, "false")
            },

            // Type operations
            TypeCast cast => new[] { (cast.Operand, "") },

            // Member access
            MemberAccess member => new[] { (member.Value, "") },
            IndexAccess index => new[] { (index.Value, "target") }
                .Concat(index.Arguments.Select((arg, i) => (arg, $"index{i}"))),

            // Method invocation
            MethodInvocation method => method.Target != null
                ? new[] { (method.Target, "target") }.Concat(
                    method.Arguments.Select((arg, i) => (arg, $"arg{i}")))
                : method.Arguments.Select((arg, i) => (arg, $"arg{i}")),

            // Block
            Block block => block.Nodes.Select((n, i) => (n, $"{i}")),

            // Assignment
            Assignment assign => new[] { (assign.Destination, "target"), (assign.Value, "value") },

            // Control flow
            IfStatement ifStmt => ifStmt.ElseBranch != null
                ? new[] { (ifStmt.Condition, "condition"), (ifStmt.ThenBranch, "then"), (ifStmt.ElseBranch, "else") }
                : new[] { (ifStmt.Condition, "condition"), (ifStmt.ThenBranch, "then") },

            WhileLoop whileLoop => new[] { (whileLoop.Condition, "condition"), (whileLoop.Body, "body") },
            DoWhileLoop doWhile => new[] { (doWhile.Body, "body"), (doWhile.Condition, "condition") },

            ForLoop forLoop => new[] {
                (forLoop.Initializer!, "init"),
                (forLoop.Condition!, "condition"),
                (forLoop.Increment!, "iterate"),
                (forLoop.Body, "body")
            }.Where(x => x.Item1 != null!),

            ReturnStatement ret => ret.Value != null ? new[] { (ret.Value, "") } : Array.Empty<(Node, string)>(),
            ThrowStatement throw_ => new[] { (throw_.Exception, "") },

            // Default: no children
            _ => Array.Empty<(Node, string)>()
        };
    }

    private static void AppendValue(StringBuilder builder, object? value)
    {
        switch (value) {
            case null:
                builder.Append("null");
                break;
            case string s:
                builder.Append("\\\"");
                builder.Append(s);
                builder.Append("\\\"");
                break;
            case char c:
                builder.Append("\\'");
                builder.Append(c);
                builder.Append("\\'");
                break;
            case bool b:
                builder.Append(b ? "true" : "false");
                break;
            default:
                builder.Append(value.ToString() ?? "null");
                break;
        }
    }

    private static void AppendTypeName(StringBuilder builder, ITypeDefinition type)
    {
        var name = type.Name ?? "Unknown";

        var index = name.IndexOf('`');
        var paramCount = type.GenericParameters.Count();

        if (index == -1 || paramCount == 0) {
            builder.Append(name);
            return;
        }

        builder.Append(name, 0, index);
        builder.Append('<');

        foreach (var (idx, param) in type.GenericParameters.Index()) {
            builder.Append(param.ParameterTypeDefinition.Name);

            if (idx < paramCount - 1)
                builder.Append(", ");
        }
        builder.Append('>');
    }

    private enum NodeShape {
        Rectangle,
        RoundedRectangle,
        Circle,
        Rhombus,
        Hexagon
    }
}