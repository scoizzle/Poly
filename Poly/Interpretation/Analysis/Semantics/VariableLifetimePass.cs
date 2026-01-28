namespace Poly.Interpretation.Analysis;

internal record VariableScopeMetadata(
    Dictionary<Block, HashSet<Variable>> BlockScopes,
    Dictionary<Variable, Variable?> VariableReferences, // Maps Variable uses → declarations
    List<VariableScopeError> Errors
);

internal record VariableScopeError(Node Node, string Message);

internal sealed class ScopeValidator : INodeAnalyzer {
    private readonly Stack<Block> _scopeStack = new();
    private readonly Dictionary<string, Stack<Variable>> _variablesByName = new();

    public void Analyze(AnalysisContext context, Node node)
    {
        switch (node) {
            case Block block:
                AnalyzeBlock(context, block);
                break;

            case Variable variable when variable.Value == null:
                // Variable reference (usage)
                ValidateVariableReference(context, variable);
                this.AnalyzeChildren(context, node);
                break;

            case Assignment assignment when assignment.Destination is Variable v:
                // Variable assignment
                ValidateVariableReference(context, v);
                this.AnalyzeChildren(context, node);
                break;

            default:
                this.AnalyzeChildren(context, node);
                break;
        }
    }

    private void AnalyzeBlock(AnalysisContext context, Block block)
    {
        _scopeStack.Push(block);

        // Register block-scoped variables
        foreach (var variable in block.Variables.OfType<Variable>()) {
            RegisterVariable(context, variable, block);
        }

        // Analyze block contents
        this.AnalyzeChildren(context, block);

        // Pop scope and unregister variables
        foreach (var variable in block.Variables.OfType<Variable>()) {
            UnregisterVariable(variable);
        }

        _scopeStack.Pop();
    }

    private void RegisterVariable(AnalysisContext context, Variable variable, Block scope)
    {
        if (!_variablesByName.TryGetValue(variable.Name, out var stack)) {
            stack = new Stack<Variable>();
            _variablesByName[variable.Name] = stack;
        }

        // Check for shadowing (warning, not error)
        if (stack.Count > 0) {
            AddWarning(context, variable, $"Variable '{variable.Name}' shadows outer scope variable");
        }

        stack.Push(variable);

        // Track which block owns this variable
        var metadata = GetOrCreateMetadata(context);
        if (!metadata.BlockScopes.TryGetValue(scope, out var scopeVars)) {
            scopeVars = new HashSet<Variable>();
            metadata.BlockScopes[scope] = scopeVars;
        }
        scopeVars.Add(variable);
    }

    private void UnregisterVariable(Variable variable)
    {
        if (_variablesByName.TryGetValue(variable.Name, out var stack)) {
            stack.Pop();
        }
    }

    private void ValidateVariableReference(AnalysisContext context, Variable variable)
    {
        if (_variablesByName.TryGetValue(variable.Name, out var stack) && stack.Count > 0) {
            // Valid reference - link to declaration
            var declaration = stack.Peek();
            var metadata = GetOrCreateMetadata(context);
            metadata.VariableReferences[variable] = declaration;
        }
        else {
            // Undeclared variable
            AddError(context, variable, $"Variable '{variable.Name}' is not declared in this scope");
        }
    }

    private VariableScopeMetadata GetOrCreateMetadata(AnalysisContext context)
    {
        return context.Metadata.GetOrAdd(() => new VariableScopeMetadata(
            new Dictionary<Block, HashSet<Variable>>(),
            new Dictionary<Variable, Variable?>(),
            new List<VariableScopeError>()
        ));
    }

    private void AddError(AnalysisContext context, Node node, string message)
    {
        var metadata = GetOrCreateMetadata(context);
        metadata.Errors.Add(new VariableScopeError(node, message));
    }

    private void AddWarning(AnalysisContext context, Node node, string message)
    {
        // Could add a warnings list to metadata
    }
}

public static class VariableScopeMetadataExtensions {
    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseVariableScopeValidator()
        {
            builder.AddAnalyzer(new ScopeValidator());
            return builder;
        }
    }
}