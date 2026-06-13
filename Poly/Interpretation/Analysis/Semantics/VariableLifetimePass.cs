using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Interpretation.Analysis;

/// <summary>Unified metadata for variable scope, assignment counts, and escape
/// analysis.  Produced by <see cref="ScopeValidator"/> during its single AST
/// walk and stored as a shared singleton on every analyzed node.</summary>
internal record VariableAnalysisMetadata(
    // ── Scope hierarchy ──
    Dictionary<Block, HashSet<Variable>> BlockScopes,
    Dictionary<Variable, Variable?> VariableReferences,
    Dictionary<Block, ScopeVertex> ScopeVertices,
    Dictionary<Variable, Node> VariableDeclarationScope,
    // ── Alias analysis ──
    Dictionary<Variable, int> AssignmentCount,
    HashSet<Variable> EscapedVariables
) : IAnalysisMetadata;

/// <summary>A vertex in the scope tree.  Each <see cref="Block"/> gets one,
/// linked to its parent via <see cref="Parent"/>.</summary>
internal record ScopeVertex(Block Block, ScopeVertex? Parent, HashSet<Variable> Declared);

internal record VariableScopeError(Node Node, string Message);

internal sealed class ScopeValidator : INodeAnalyzer {
    private readonly Stack<Block> _scopeStack = new();
    private readonly Dictionary<string, Stack<Variable>> _variablesByName = [];
    private readonly VariableAnalysisMetadata _sharedMeta = new(
        BlockScopes: [],
        VariableReferences: [],
        ScopeVertices: [],
        VariableDeclarationScope: [],
        AssignmentCount: [],
        EscapedVariables: []
    );

    public void Analyze(AnalysisContext context, Node node) {
        if (!context.TryBeginAnalyzerVisit<ScopeValidator>(node)) {
            return;
        }

        switch (node) {
            case Block block:
                AnalyzeBlock(context, block);
                break;

            case ForEachLoop forEachLoop:
                AnalyzeForEachLoop(context, forEachLoop);
                break;

            case Variable variable when variable.Value == null:
                ValidateVariableReference(context, variable);
                this.AnalyzeChildren(context, node);
                break;

            case Assignment assignment when assignment.Destination is Variable v:
                if (!_variablesByName.TryGetValue(v.Name, out var stack) || stack.Count == 0) {
                    RegisterScopedVariable(context, v);
                }
                else {
                    ValidateVariableReference(context, v);
                }
                // Count this write against the current declaration
                if (_variablesByName.TryGetValue(v.Name, out var countStack) && countStack.Count > 0) {
                    var decl = countStack.Peek();
                    _sharedMeta.AssignmentCount.TryGetValue(decl, out var count);
                    _sharedMeta.AssignmentCount[decl] = count + 1;
                }
                this.AnalyzeChildren(context, node);
                break;

            case Invoke invoke:
                // Arguments to non-lambda calls escape
                if (invoke.Delegate is not Lambda)
                    MarkSubtreeEscaped(invoke.Arguments);
                this.AnalyzeChildren(context, node);
                break;

            case Return r:
                MarkSubtreeEscaped(r.Value);
                this.AnalyzeChildren(context, node);
                break;

            default:
                this.AnalyzeChildren(context, node);
                break;
        }
    }

    private void AnalyzeForEachLoop(AnalysisContext context, ForEachLoop forEachLoop) {
        Analyze(context, forEachLoop.Collection);

        // The collection variable escapes (it's iterated)
        MarkSubtreeEscaped(forEachLoop.Collection);

        RegisterScopedVariable(context, forEachLoop.LoopVariable);
        _sharedMeta.VariableDeclarationScope[forEachLoop.LoopVariable] =
            _scopeStack.Count > 0 ? _scopeStack.Peek() : forEachLoop;
        Analyze(context, forEachLoop.Body);
        UnregisterVariable(forEachLoop.LoopVariable);
    }

    private void AnalyzeBlock(AnalysisContext context, Block block) {
        _scopeStack.Push(block);

        // Build the ScopeVertex for this block
        var parent = _scopeStack.Count > 1
            ? _sharedMeta.ScopeVertices.GetValueOrDefault(_scopeStack.ElementAt(1))
            : null;
        _sharedMeta.ScopeVertices[block] = new ScopeVertex(block, parent, []);

        // Register block-scoped variables
        var vars = block.Variables;
        for (int i = 0; i < vars.Count; i++) {
            if (vars[i] is Variable v) RegisterVariable(context, v, block);
        }

        // Update the vertex with declared variables now that RegisterVariable has run
        _sharedMeta.ScopeVertices[block] = _sharedMeta.ScopeVertices[block] with {
            Declared = _sharedMeta.BlockScopes.GetValueOrDefault(block) ?? []
        };

        // Analyze block contents
        this.AnalyzeChildren(context, block);

        // Pop scope and unregister variables
        for (int i = 0; i < vars.Count; i++) {
            if (vars[i] is Variable v) UnregisterVariable(v);
        }

        _scopeStack.Pop();
    }

    private void RegisterVariable(AnalysisContext context, Variable variable, Block scope) {
        RegisterScopedVariable(context, variable);

        var meta = GetOrCreateMetadata(context, variable);
        if (!meta.BlockScopes.TryGetValue(scope, out var scopeVars)) {
            scopeVars = [];
            meta.BlockScopes[scope] = scopeVars;
        }

        scopeVars.Add(variable);
        _sharedMeta.VariableDeclarationScope[variable] = scope;
    }

    private void RegisterScopedVariable(AnalysisContext context, Variable variable) {
        if (!_variablesByName.TryGetValue(variable.Name, out var stack)) {
            stack = new Stack<Variable>();
            _variablesByName[variable.Name] = stack;
        }

        if (stack.Count > 0) {
            context.ReportWarning(variable, $"Variable '{variable.Name}' shadows outer scope variable");
        }

        stack.Push(variable);
    }

    private void UnregisterVariable(Variable variable) {
        if (_variablesByName.TryGetValue(variable.Name, out var stack)) {
            stack.Pop();
        }
    }

    private void ValidateVariableReference(AnalysisContext context, Variable variable) {
        if (_variablesByName.TryGetValue(variable.Name, out var stack) && stack.Count > 0) {
            var declaration = stack.Peek();
            var metadata = GetOrCreateMetadata(context, variable);
            metadata.VariableReferences[variable] = declaration;
        }
        else {
            context.ReportError(variable, $"Variable '{variable.Name}' is not declared in this scope");
        }
    }

    private VariableAnalysisMetadata GetOrCreateMetadata(AnalysisContext context, Node node) {
        return context.Metadata.GetOrAdd(node, () => _sharedMeta);
    }

    // ── Escape tracking ──

    private void MarkSubtreeEscaped(Node? node) {
        if (node is Variable v) {
            MarkVariableEscaped(v);
            return;
        }
        if (node is not null) {
            foreach (var arg in node.Children) {
                if (arg is not null)
                    MarkSubtreeEscaped(arg);
            }
        }
    }

    private void MarkSubtreeEscaped(System.Collections.Generic.IReadOnlyList<Node?> nodes) {
        for (int i = 0; i < nodes.Count; i++) {
            if (nodes[i] is not null)
                MarkSubtreeEscaped(nodes[i]);
        }
    }

    private void MarkVariableEscaped(Variable v) {
        if (_variablesByName.TryGetValue(v.Name, out var stack) && stack.Count > 0) {
            var decl = stack.Peek();
            _sharedMeta.EscapedVariables.Add(decl);
        }
    }
}

public static class VariableAnalysisExtensions {
    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseVariableScopeValidator() {
            builder.AddAnalyzer(new ScopeValidator());
            return builder;
        }
    }
}