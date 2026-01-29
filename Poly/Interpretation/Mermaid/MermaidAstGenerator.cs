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
    private int _nodeCounter;

    /// <summary>
    /// Initializes a new instance without analysis metadata.
    /// </summary>
    public MermaidAstGenerator()
    {
        _output = new StringBuilder();
        _visitedEdges = new HashSet<string>();
        _nodeCounter = 0;
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
        _nodeCounter = 0;
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
        _nodeCounter = 0;

        _output.AppendLine($"graph {direction}");

        GenerateNode(node);

        return _output.ToString();
    }

    private string GenerateNode(Node node)
    {
        var nodeId = GetNodeId(node);
        var label = GetNodeLabel(node);
        var shape = GetNodeShape(node);

        // Add type information to label if available
        if (_analysisResult != null) {
            var resolvedType = _analysisResult.GetResolvedType(node);
            if (resolvedType != null) {
                var typeLabel = FormatTypeName(resolvedType);
                label = $"{typeLabel} {label}";
            }
        }

        // Define the node with its label and shape
        var nodeDefinition = shape switch {
            NodeShape.Rectangle => $"{nodeId}[\"{label}\"]",
            NodeShape.RoundedRectangle => $"{nodeId}(\"{label}\")",
            NodeShape.Circle => $"{nodeId}((\"{label}\"))",
            NodeShape.Rhombus => $"{nodeId}{{\"{label}\"}}",
            NodeShape.Hexagon => $"{nodeId}{{{{\"{label}\"}}}}",
            _ => $"{nodeId}[\"{label}\"]"
        };

        _output.AppendLine($"    {nodeDefinition}");

        // Add styling annotations if analysis result is available
        if (_analysisResult != null) {
            AddStyleAnnotations(nodeId, node);
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
        return $"n{_nodeCounter++}";
    }

    private string GetNodeLabel(Node node)
    {
        return node switch {
            // Leaf nodes with values
            Constant constant => $"Constant {FormatValue(constant.Value)}",
            Parameter param => $"Parameter {param.Name}",
            Variable variable => $"Variable {variable.Name}",

            // Binary arithmetic
            Add => "Add (+)",
            Subtract => "Subtract (-)",
            Multiply => "Multiply (*)",
            Divide => "Divide (/)",
            Modulo => "Modulo (%)",

            // Unary operations
            UnaryMinus => "Negate (-)",
            Not => "Not (!)",

            // Comparison
            Equal => "Equal (==)",
            NotEqual => "Not Equal (!=)",
            LessThan => "Less Than (<)",
            LessThanOrEqual => "Less Than or Equal (<=)",
            GreaterThan => "Greater Than (>)",
            GreaterThanOrEqual => "Greater Than or Equal (>=)",

            // Boolean operations
            And => "And (&&)",
            Or => "Or (||)",

            // Other operations
            Conditional => "Conditional (?:)",
            Coalesce => "Coalesce (??)",
            TypeCast cast => $"Cast to {cast.TargetTypeReference}",
            MemberAccess member => $"Member Access .{member.MemberName}",
            IndexAccess => "Index Access",
            MethodInvocation method => $"Method Call {method.MethodName}()",

            // Control flow
            Block => "Block",
            IfStatement => "If Statement",
            WhileLoop => "While Loop",
            DoWhileLoop => "Do-While Loop",
            ForLoop => "For Loop",
            SwitchStatement => "Switch",

            // Assignments
            Assignment => "Assignment (=)",

            // Jumps
            BreakStatement => "Break",
            ContinueStatement => "Continue",
            ReturnStatement => "Return",
            GotoStatement goto_ => $"Goto {goto_.Target}",
            LabelDeclaration label => $"Label {label.Name}",

            // Exception handling
            ThrowStatement => "Throw",
            TryCatchFinally => "Try-Catch-Finally",

            // Resource management
            UsingStatement => "Using",

            _ => node.GetType().Name
        };
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

    private static string FormatValue(object? value)
    {
        return value switch {
            null => "null",
            string s => $"\\\"{s}\\\"",
            char c => $"\\'{c}\\'",
            bool b => b.ToString().ToLowerInvariant(),
            _ => value.ToString() ?? "null"
        };
    }

    private static string FormatTypeName(ITypeDefinition type)
    {
        var name = type.Name ?? "Unknown";

        // Handle generic types (e.g., "Nullable`1" -> "Nullable<T>")
        if (name.Contains('`')) {
            var parts = name.Split('`');
            var baseName = parts[0];
            var argCount = int.Parse(parts[1]);

            // Generate placeholder type parameters
            var typeParams = argCount == 1
                ? "T"
                : string.Join(", ", Enumerable.Range(1, argCount).Select(i => $"T{i}"));

            return $"{baseName}<{typeParams}>";
        }

        return name;
    }

    private enum NodeShape {
        Rectangle,
        RoundedRectangle,
        Circle,
        Rhombus,
        Hexagon
    }
}