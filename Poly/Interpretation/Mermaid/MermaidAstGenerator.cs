using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Interpretation.Mermaid;

/// <summary>Generates Mermaid flowchart diagrams from analyzed AST nodes for
/// visualization. Output can be rendered in GitHub Markdown, VS Code extensions,
/// or any Mermaid-compatible renderer.</summary>
/// <remarks>
/// <para>Produces <c>graph TB</c> (top-to-bottom) flowcharts with labeled edges
/// showing child relationships (left, right, body, condition, etc.). Leaf nodes
/// use rounded rectangles; internal nodes use sharp rectangles.</para>
/// <para>When an <see cref="AnalysisResult"/> is provided, type information is
/// included in node labels for richer visualization.</para>
/// </remarks>
public sealed class MermaidAstGenerator {
    private readonly AnalysisResult? _analysisResult;
    private readonly StringBuilder _output;
    private readonly HashSet<string> _visitedEdges;
    private readonly StringBuilder _scratch;

    /// <summary>Creates a generator without analysis metadata. Node labels will
    /// show only AST node types and values without type information.</summary>
    public MermaidAstGenerator() {
        _output = new StringBuilder();
        _visitedEdges = new HashSet<string>();
        _scratch = new StringBuilder();
    }

    /// <summary>Creates a generator with semantic analysis results for
    /// type-enriched output.</summary>
    /// <param name="analysisResult">The semantic analysis result containing
    /// type resolution information for enhanced node labels.</param>
    public MermaidAstGenerator(AnalysisResult analysisResult) {
        ArgumentNullException.ThrowIfNull(analysisResult);
        _analysisResult = analysisResult;
        _output = new StringBuilder();
        _visitedEdges = new HashSet<string>();
        _scratch = new StringBuilder();
    }

    /// <summary>Generates a Mermaid flowchart diagram from an AST node.
    /// The output is a directed graph with labeled edges showing AST child
    /// relationships (left, right, body, condition, etc.). Leaf nodes use
    /// rounded rectangles; internal nodes use sharp rectangles.</summary>
    /// <param name="node">The root AST node to visualize.</param>
    /// <param name="direction">The flow direction: TB (top-bottom, default),
    /// LR (left-right), BT (bottom-top), RL (right-left).</param>
    /// <returns>A Mermaid flowchart string (without <c>```mermaid</c> fences)
    /// suitable for embedding in Markdown documents.</returns>
    public string Generate(Node node, string direction = "TB") {
        ArgumentNullException.ThrowIfNull(node);

        _output.Clear();
        _visitedEdges.Clear();

        _output.AppendLine($"graph {direction}");

        // First, collect and generate all Parameter nodes to put them at the beginning
        var parameterNodes = CollectParameterNodes(node);
        foreach (var paramNode in parameterNodes) {
            var nodeId = GetNodeId(paramNode);
            var shape = GetNodeShape(paramNode);
            AppendNodeDefinition(paramNode, nodeId, shape, LabelFor(FactFor(paramNode)));

            // Add styling annotations if analysis result is available
            if (_analysisResult != null) {
                AddStyleAnnotations(nodeId, paramNode);
            }
        }

        GenerateNode(node);

        return _output.ToString();
    }

    private List<Parameter> CollectParameterNodes(Node node) {
        var parameters = new List<Parameter>();
        var visited = new HashSet<Node>();

        void Visit(Node n) {
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

    private string GenerateNode(Node node) {
        var fact = FactFor(node);
        var nodeId = GetNodeId(node);

        // Skip definition for Parameter nodes as they were already defined at the beginning
        if (!(node is Parameter)) {
            AppendNodeDefinition(node, nodeId, GetNodeShape(node), LabelFor(fact));

            // Add styling annotations if analysis result is available
            if (_analysisResult != null) {
                AddStyleAnnotations(nodeId, node);
            }
        }

        foreach (var (child, edgeLabel) in GetChildren(node)) {
            AppendEdge(nodeId, GenerateNode(child), edgeLabel);
        }

        return nodeId;
    }

    private static string LabelFor(NodeFact fact) =>
        fact.Producers.HasFlag(Producers.Mermaid) ? fact.Label : $"unclaimed: {fact.Label}";

    private void AppendEdge(string nodeId, string childId, string edgeLabel) {
        if (!_visitedEdges.Add($"{nodeId}->{childId}")) {
            return;
        }

        if (!string.IsNullOrEmpty(edgeLabel)) {
            _output.AppendLine($"    {nodeId} -->|{edgeLabel}| {childId}");
        }
        else {
            _output.AppendLine($"    {nodeId} --> {childId}");
        }
    }

    private void AppendNodeDefinition(Node node, string nodeId, NodeShape shape, string label) {
        _scratch.Clear();
        // Add type information to label if available
        if (_analysisResult != null) {
            var resolvedType = _analysisResult.GetResolvedType(node);
            if (resolvedType != null) {
                AppendTypeName(_scratch, resolvedType);
                _scratch.Append(' ');
            }
        }

        _scratch.Append(label);

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

    private void AddStyleAnnotations(string nodeId, Node node) {
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

    private string GetNodeId(Node node) {
        // Use the node's stable NodeId instead of auto-generated counter
        return node.Id.Value;
    }

    private static NodeFact FactFor(Node node) =>
        NodeFacts.All.TryGetValue(node.GetType(), out var fact)
            ? fact
            : throw new InvalidOperationException($"{node.GetType().Name} has no NodeFacts row.");

    private NodeShape GetNodeShape(Node node) {
        return node switch {
            // Leaf nodes - rounded rectangles
            Constant or Parameter or Variable or ThisReference => NodeShape.RoundedRectangle,

            // Conditionals - rhombus
            Conditional or IfStatement or SwitchStatement => NodeShape.Rhombus,

            // Loops - hexagon
            WhileLoop or DoWhileLoop or ForLoop => NodeShape.Hexagon,

            // Operations - default rectangle
            _ => NodeShape.Rectangle
        };
    }

    private IEnumerable<(Node Child, string EdgeLabel)> GetChildren(Node node) {
        var labelled = BespokeEdgeLabels(node).ToArray();
        foreach (var child in node.Children) {
            if (child is null)
                continue;
            yield return (child, EdgeLabelFor(labelled, child));
        }
    }

    private static string EdgeLabelFor((Node Child, string EdgeLabel)[] labelled, Node child) {
        foreach (var (candidate, label) in labelled) {
            if (ReferenceEquals(candidate, child))
                return label;
        }
        return string.Empty;
    }

    /// <summary>Edge labels for the kinds whose relationships a reader can name; a kind without
    /// an entry still renders every child, on an unlabeled edge.</summary>
    private static IEnumerable<(Node Child, string EdgeLabel)> BespokeEdgeLabels(Node node) {
        return node switch {
            // Binary operations
            Add add => [(add.LeftHandValue, "left"), (add.RightHandValue, "right")],
            Subtract sub => [(sub.LeftHandValue, "left"), (sub.RightHandValue, "right")],
            Multiply mul => [(mul.LeftHandValue, "left"), (mul.RightHandValue, "right")],
            Divide div => [(div.LeftHandValue, "left"), (div.RightHandValue, "right")],
            Modulo mod => [(mod.LeftHandValue, "left"), (mod.RightHandValue, "right")],

            Equal eq => [(eq.LeftHandValue, "left"), (eq.RightHandValue, "right")],
            NotEqual neq => [(neq.LeftHandValue, "left"), (neq.RightHandValue, "right")],
            LessThan lt => [(lt.LeftHandValue, "left"), (lt.RightHandValue, "right")],
            LessThanOrEqual lte => [(lte.LeftHandValue, "left"), (lte.RightHandValue, "right")],
            GreaterThan gt => [(gt.LeftHandValue, "left"), (gt.RightHandValue, "right")],
            GreaterThanOrEqual gte => [(gte.LeftHandValue, "left"), (gte.RightHandValue, "right")],

            And and => [(and.LeftHandValue, "left"), (and.RightHandValue, "right")],
            Or or => [(or.LeftHandValue, "left"), (or.RightHandValue, "right")],

            Coalesce coalesce => [(coalesce.LeftHandValue, "value"), (coalesce.RightHandValue, "default")],

            // Unary operations
            UnaryMinus minus => [(minus.Operand, "")],
            Not not => [(not.Value, "")],

            // Conditional
            Conditional cond => [
                (cond.Condition, "condition"),
                (cond.IfTrue, "true"),
                (cond.IfFalse, "false")
            ],

            // Type operations
            TypeCast cast => [(cast.Operand, "")],
            TypeIs typeIs => [(typeIs.Operand, "")],
            TypeAs typeAs => [(typeAs.Operand, "")],

            // Member access
            Member member => [(member.Value, "")],
            IndexAccess index => new[] { (index.Value, "target") }
                .Concat(index.Arguments.Select((arg, i) => (arg, $"index{i}"))),

            // Method invocation
            Invoke method => new[] { (method.Delegate, "method") }
                .Concat(method.Arguments.Select((arg, i) => ((Node)arg, $"arg{i}"))),

            // Constructor invocation
            New @new => new[] { (@new.Type, "type") }
                .Concat(@new.Arguments.Select((arg, i) => ((Node)arg, $"arg{i}"))),

            PropertyGetterDefinitionNode getter when getter.Body != null => [(getter.Body, "body")],
            PropertySetterDefinitionNode setter => new[] { ((Node)setter.ValueParameter!, "value") }
                .Where(static pair => pair.Item1 is not null)
                .Concat(setter.Body is null ? Array.Empty<(Node, string)>() : [(setter.Body, "body")]),
            PropertyInitializerDefinitionNode initializer =>
                initializer.Value is null ? [] : [(initializer.Value, "value")],

            // Block
            Block block => block.Nodes.Select((n, i) => (n, $"{i}")),

            // Assignment
            Assignment assign => [(assign.Destination, "target"), (assign.Value, "value")],

            // Control flow
            IfStatement ifStmt => ifStmt.ElseBranch != null
                ? new[] { (ifStmt.Condition, "condition"), (ifStmt.ThenBranch, "then"), (ifStmt.ElseBranch, "else") }
                : [(ifStmt.Condition, "condition"), (ifStmt.ThenBranch, "then")],

            WhileLoop whileLoop => [(whileLoop.Condition, "condition"), (whileLoop.Body, "body")],
            DoWhileLoop doWhile => [(doWhile.Body, "body"), (doWhile.Condition, "condition")],

            ForLoop forLoop => new[] {
                (forLoop.Initializer!, "init"),
                (forLoop.Condition!, "condition"),
                (forLoop.Increment!, "iterate"),
                (forLoop.Body, "body")
            }.Where(x => x.Item1 != null!),

            Return ret => ret.Value != null ? [(ret.Value, "")] : Array.Empty<(Node, string)>(),
            ThrowStatement throw_ => [(throw_.Exception, "")],

            // Exception handling
            TryCatchFinally tcf => new[] {
                (tcf.TryBlock, "try")
            }.Concat(tcf.CatchClauses?.Select((c, i) => ((Node)c.Body, $"catch{i}")) ?? [])
             .Concat(tcf.FinallyBlock is not null ? [(tcf.FinallyBlock, "finally")] : []),

            // Switch
            SwitchStatement switchStmt => new[] {
                (switchStmt.Value, "value")
            }.Concat(switchStmt.Cases.SelectMany((c, i) => new[] { (c.Pattern, $"case{i}_pattern"), (c.Body, $"case{i}_body") }))
             .Concat(switchStmt.DefaultCase is not null ? [(switchStmt.DefaultCase, "default")] : []),

            // Using
            UsingStatement usingStmt => new[] {
                (usingStmt.Resource, "resource"),
                (usingStmt.Body, "body")
            },

            _ => []
        };
    }

    private static void AppendTypeName(StringBuilder builder, ITypeDefinition type) {
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