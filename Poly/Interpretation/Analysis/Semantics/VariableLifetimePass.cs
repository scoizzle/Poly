namespace Poly.Interpretation.Analysis;

internal record VariableScopeMetadata(
    Dictionary<Block, HashSet<Variable>> BlockScopes,
    Dictionary<Variable, Variable?> VariableReferences // Maps Variable uses → declarations
) : IAnalysisMetadata;

internal record VariableScopeError(Node Node, string Message);

internal sealed class ScopeValidator : INodeAnalyzer {
    private readonly Stack<Block> _scopeStack = new();
    private readonly Dictionary<string, Stack<Variable>> _variablesByName = [];
    private readonly VariableScopeMetadata _sharedScopeMeta = new(
        BlockScopes: new Dictionary<Block, HashSet<Variable>>(),
        VariableReferences: new Dictionary<Variable, Variable?>()
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
                // Variable reference (usage)
                ValidateVariableReference(context, variable);
                this.AnalyzeChildren(context, node);
                break;

            case Assignment assignment when assignment.Destination is Variable v:
                // Variable assignment: register if first use (declaration by assignment for simple blocks)
                if (!_variablesByName.TryGetValue(v.Name, out var stack) || stack.Count == 0) {
                    RegisterScopedVariable(context, v);
                }
                else {
                    ValidateVariableReference(context, v);
                }
                this.AnalyzeChildren(context, node);
                break;

            default:
                this.AnalyzeChildren(context, node);
                break;
        }
    }

    private void AnalyzeForEachLoop(AnalysisContext context, ForEachLoop forEachLoop) {
        Analyze(context, forEachLoop.Collection);

        RegisterScopedVariable(context, forEachLoop.LoopVariable);
        Analyze(context, forEachLoop.Body);
        UnregisterVariable(forEachLoop.LoopVariable);
    }

    private void AnalyzeBlock(AnalysisContext context, Block block) {
        _scopeStack.Push(block);

        // Register block-scoped variables (direct indexed per SideEffect direct-Block + Aggregate lesson for wide blocks; avoids OfType enumerator).
        var vars = block.Variables;
        for (int i = 0; i < vars.Count; i++) {
            if (vars[i] is Variable v) RegisterVariable(context, v, block);
        }

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

        // Track which block owns this variable
        var metadata = GetOrCreateMetadata(context, variable);
        if (!metadata.BlockScopes.TryGetValue(scope, out var scopeVars)) {
            scopeVars = [];
            metadata.BlockScopes[scope] = scopeVars;
        }

        scopeVars.Add(variable);
    }

    private void RegisterScopedVariable(AnalysisContext context, Variable variable) {
        if (!_variablesByName.TryGetValue(variable.Name, out var stack)) {
            stack = new Stack<Variable>();
            _variablesByName[variable.Name] = stack;
        }

        // Check for shadowing (warning, not error)
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
            // Valid reference - link to declaration
            var declaration = stack.Peek();
            var metadata = GetOrCreateMetadata(context, variable);
            metadata.VariableReferences[variable] = declaration;
        }
        else {
            // Undeclared variable
            context.ReportError(variable, $"Variable '{variable.Name}' is not declared in this scope");
        }
    }

    private VariableScopeMetadata GetOrCreateMetadata(AnalysisContext context, Node node) {
        // Shared single metadata instance (with one set of dicts) for all nodes -- applies sparse/single root metadata lesson; avoids per-var heavy allocs of separate dicts. All mutations go to the shared maps.
        return context.Metadata.GetOrAdd(node, () => _sharedScopeMeta);
    }
}

public static class VariableScopeMetadataExtensions {
    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseVariableScopeValidator() {
            builder.AddAnalyzer(new ScopeValidator());
            return builder;
        }
    }
}