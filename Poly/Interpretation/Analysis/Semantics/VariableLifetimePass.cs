using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Interpretation.Analysis;

internal record VariableAnalysisMetadata(
    Dictionary<Block, HashSet<Variable>> BlockScopes,
    Dictionary<Variable, Variable?> VariableReferences,
    Dictionary<Block, ScopeVertex> ScopeVertices,
    Dictionary<Variable, Node> VariableDeclarationScope,
    Dictionary<Variable, int> AssignmentCount,
    HashSet<Variable> EscapedVariables
) : IAnalysisMetadata;

internal record ScopeVertex(Block Block, ScopeVertex? Parent, HashSet<Variable> Declared);

internal sealed class ScopeValidator : INodeAnalyzer {
    // Shared metadata instance reused across all analyses on the same AST.
    // The Analyzer doc says passes are stateless; all mutable state is in
    // this single record whose dictionaries are populated during the walk.
    private readonly VariableAnalysisMetadata _meta = new(
        BlockScopes: [],
        VariableReferences: [],
        ScopeVertices: [],
        VariableDeclarationScope: [],
        AssignmentCount: [],
        EscapedVariables: []
    );
    private readonly List<Block> _scopeStack = [];  // used as a stack (Add/RemoveAt)
    private readonly Dictionary<string, Stack<Variable>> _variablesByName = [];

    public void Analyze(AnalysisContext context, Node node) {
        if (!context.TryBeginAnalyzerVisit<ScopeValidator>(node))
            return;

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
                if (!_variablesByName.TryGetValue(v.Name, out var stack) || stack.Count == 0)
                    RegisterScopedVariable(context, v);
                else
                    ValidateVariableReference(context, v);
                if (_variablesByName.TryGetValue(v.Name, out var countStack) && countStack.Count > 0) {
                    var decl = countStack.Peek();
                    _meta.AssignmentCount.TryGetValue(decl, out var count);
                    _meta.AssignmentCount[decl] = count + 1;
                }
                this.AnalyzeChildren(context, node);
                break;

            case Invoke invoke:
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
        MarkSubtreeEscaped(forEachLoop.Collection);
        RegisterScopedVariable(context, forEachLoop.LoopVariable);
        _meta.VariableDeclarationScope[forEachLoop.LoopVariable] =
            _scopeStack.Count > 0 ? _scopeStack[^1] : forEachLoop;
        Analyze(context, forEachLoop.Body);
        UnregisterVariable(forEachLoop.LoopVariable);
    }

    private void AnalyzeBlock(AnalysisContext context, Block block) {
        _scopeStack.Add(block);

        // Peek at the block below the top (parent scope) without enumerating
        // the stack, since ElementAt would create an enumerator that's
        // invalidated by nested Push/Pop during child block analysis.
        var parent = _scopeStack.Count > 1
            ? _meta.ScopeVertices.GetValueOrDefault(_scopeStack[^2])
            : null;
        var vars = block.Variables;
        for (int i = 0; i < vars.Count; i++) {
            if (vars[i] is Variable v) RegisterVariable(context, v, block);
        }

        _meta.ScopeVertices[block] = new ScopeVertex(block, parent,
            _meta.BlockScopes.GetValueOrDefault(block) ?? []);

        this.AnalyzeChildren(context, block);

        for (int i = 0; i < vars.Count; i++) {
            if (vars[i] is Variable v) UnregisterVariable(v);
        }

        _scopeStack.RemoveAt(_scopeStack.Count - 1);
    }

    private void RegisterVariable(AnalysisContext context, Variable variable, Block scope) {
        RegisterScopedVariable(context, variable);

        var meta = GetOrCreateMetadata(context, variable);
        if (!meta.BlockScopes.TryGetValue(scope, out var scopeVars)) {
            scopeVars = [];
            meta.BlockScopes[scope] = scopeVars;
        }

        scopeVars.Add(variable);
        _meta.VariableDeclarationScope[variable] = scope;
    }

    private void RegisterScopedVariable(AnalysisContext context, Variable variable) {
        if (!_variablesByName.TryGetValue(variable.Name, out var stack)) {
            stack = new Stack<Variable>();
            _variablesByName[variable.Name] = stack;
        }
        if (stack.Count > 0)
            context.ReportWarning(variable, $"Variable '{variable.Name}' shadows outer scope variable");
        stack.Push(variable);
    }

    private void UnregisterVariable(Variable variable) {
        if (_variablesByName.TryGetValue(variable.Name, out var stack) && stack.Count > 0)
            stack.Pop();
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

    private VariableAnalysisMetadata GetOrCreateMetadata(AnalysisContext context, Node node) =>
        context.Metadata.GetOrAdd(node, () => _meta);

    private void MarkSubtreeEscaped(Node? node) {
        if (node is Variable v) {
            MarkVariableEscaped(v);
            return;
        }
        if (node is not null) {
            foreach (var child in node.Children) {
                if (child is not null)
                    MarkSubtreeEscaped(child);
            }
        }
    }

    private void MarkSubtreeEscaped(IReadOnlyList<Node?> nodes) {
        for (int i = 0; i < nodes.Count; i++) {
            if (nodes[i] is not null)
                MarkSubtreeEscaped(nodes[i]);
        }
    }

    private void MarkVariableEscaped(Variable v) {
        if (_variablesByName.TryGetValue(v.Name, out var stack) && stack.Count > 0) {
            var decl = stack.Peek();
            _meta.EscapedVariables.Add(decl);
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