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
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.TryBeginAnalyzerVisit<ScopeValidator>(node)) {
            return;
        }

        var scopeStack = new Stack<Block>();
        var variablesByName = new Dictionary<string, Stack<Variable>>();
        var sharedMeta = new VariableAnalysisMetadata(
            BlockScopes: [],
            VariableReferences: [],
            ScopeVertices: [],
            VariableDeclarationScope: [],
            AssignmentCount: [],
            EscapedVariables: []
        );

        AnalyzeImpl(context, node, scopeStack, variablesByName, sharedMeta);
    }

    private static void AnalyzeImpl(AnalysisContext context, Node node,
        Stack<Block> scopeStack, Dictionary<string, Stack<Variable>> variablesByName,
        VariableAnalysisMetadata sharedMeta) {

        switch (node) {
            case Block block:
                AnalyzeBlock(context, block, scopeStack, variablesByName, sharedMeta);
                break;

            case ForEachLoop forEachLoop:
                AnalyzeForEachLoop(context, forEachLoop, scopeStack, variablesByName, sharedMeta);
                break;

            case Variable variable when variable.Value == null:
                ValidateVariableReference(context, variable, variablesByName, sharedMeta);
                AnalyzeChildrenImpl(context, node, scopeStack, variablesByName, sharedMeta);
                break;

            case Assignment assignment when assignment.Destination is Variable v:
                if (!variablesByName.TryGetValue(v.Name, out var stack) || stack.Count == 0) {
                    RegisterScopedVariable(context, v, variablesByName, sharedMeta);
                }
                else {
                    ValidateVariableReference(context, v, variablesByName, sharedMeta);
                }
                if (variablesByName.TryGetValue(v.Name, out var countStack) && countStack.Count > 0) {
                    var decl = countStack.Peek();
                    sharedMeta.AssignmentCount.TryGetValue(decl, out var count);
                    sharedMeta.AssignmentCount[decl] = count + 1;
                }
                AnalyzeChildrenImpl(context, node, scopeStack, variablesByName, sharedMeta);
                break;

            case Invoke invoke:
                if (invoke.Delegate is not Lambda)
                    MarkSubtreeEscaped(invoke.Arguments, variablesByName, sharedMeta);
                AnalyzeChildrenImpl(context, node, scopeStack, variablesByName, sharedMeta);
                break;

            case Return r:
                MarkSubtreeEscaped(r.Value, variablesByName, sharedMeta);
                AnalyzeChildrenImpl(context, node, scopeStack, variablesByName, sharedMeta);
                break;

            default:
                AnalyzeChildrenImpl(context, node, scopeStack, variablesByName, sharedMeta);
                break;
        }
    }

    private static void AnalyzeForEachLoop(AnalysisContext context, ForEachLoop forEachLoop,
        Stack<Block> scopeStack, Dictionary<string, Stack<Variable>> variablesByName,
        VariableAnalysisMetadata sharedMeta) {

        AnalyzeImpl(context, forEachLoop.Collection, scopeStack, variablesByName, sharedMeta);
        MarkSubtreeEscaped(forEachLoop.Collection, variablesByName, sharedMeta);

        RegisterScopedVariable(context, forEachLoop.LoopVariable, variablesByName, sharedMeta);
        sharedMeta.VariableDeclarationScope[forEachLoop.LoopVariable] =
            scopeStack.Count > 0 ? scopeStack.Peek() : forEachLoop;
        AnalyzeImpl(context, forEachLoop.Body, scopeStack, variablesByName, sharedMeta);
        UnregisterVariable(forEachLoop.LoopVariable, variablesByName);
    }

    private static void AnalyzeBlock(AnalysisContext context, Block block,
        Stack<Block> scopeStack, Dictionary<string, Stack<Variable>> variablesByName,
        VariableAnalysisMetadata sharedMeta) {

        scopeStack.Push(block);

        var parent = scopeStack.Count > 1
            ? sharedMeta.ScopeVertices.GetValueOrDefault(scopeStack.ElementAt(1))
            : null;
        sharedMeta.ScopeVertices[block] = new ScopeVertex(block, parent, []);

        var vars = block.Variables;
        for (int i = 0; i < vars.Count; i++) {
            if (vars[i] is Variable v) RegisterVariable(context, v, block, variablesByName, sharedMeta);
        }

        sharedMeta.ScopeVertices[block] = sharedMeta.ScopeVertices[block] with {
            Declared = sharedMeta.BlockScopes.GetValueOrDefault(block) ?? []
        };

        AnalyzeChildrenImpl(context, block, scopeStack, variablesByName, sharedMeta);

        for (int i = 0; i < vars.Count; i++) {
            if (vars[i] is Variable v) UnregisterVariable(v, variablesByName);
        }

        scopeStack.Pop();
    }

    private static void RegisterVariable(AnalysisContext context, Variable variable, Block scope,
        Dictionary<string, Stack<Variable>> variablesByName,
        VariableAnalysisMetadata sharedMeta) {

        RegisterScopedVariable(context, variable, variablesByName, sharedMeta);

        var meta = GetOrCreateMetadata(context, variable, sharedMeta);
        if (!meta.BlockScopes.TryGetValue(scope, out var scopeVars)) {
            scopeVars = [];
            meta.BlockScopes[scope] = scopeVars;
        }

        scopeVars.Add(variable);
        sharedMeta.VariableDeclarationScope[variable] = scope;
    }

    private static void RegisterScopedVariable(AnalysisContext context, Variable variable,
        Dictionary<string, Stack<Variable>> variablesByName,
        VariableAnalysisMetadata sharedMeta) {

        if (!variablesByName.TryGetValue(variable.Name, out var stack)) {
            stack = new Stack<Variable>();
            variablesByName[variable.Name] = stack;
        }

        if (stack.Count > 0) {
            context.ReportWarning(variable, $"Variable '{variable.Name}' shadows outer scope variable");
        }

        stack.Push(variable);
    }

    private static void UnregisterVariable(Variable variable,
        Dictionary<string, Stack<Variable>> variablesByName) {

        if (variablesByName.TryGetValue(variable.Name, out var stack)) {
            stack.Pop();
        }
    }

    private static void ValidateVariableReference(AnalysisContext context, Variable variable,
        Dictionary<string, Stack<Variable>> variablesByName,
        VariableAnalysisMetadata sharedMeta) {

        if (variablesByName.TryGetValue(variable.Name, out var stack) && stack.Count > 0) {
            var declaration = stack.Peek();
            var metadata = GetOrCreateMetadata(context, variable, sharedMeta);
            metadata.VariableReferences[variable] = declaration;
        }
        else {
            context.ReportError(variable, $"Variable '{variable.Name}' is not declared in this scope");
        }
    }

    private static VariableAnalysisMetadata GetOrCreateMetadata(AnalysisContext context, Node node,
        VariableAnalysisMetadata sharedMeta) {

        return context.Metadata.GetOrAdd(node, () => sharedMeta);
    }

    private static void AnalyzeChildrenImpl(AnalysisContext context, Node node,
        Stack<Block> scopeStack, Dictionary<string, Stack<Variable>> variablesByName,
        VariableAnalysisMetadata sharedMeta) {

        foreach (var child in node.Children) {
            if (child is null || !context.ShouldAnalyze(child))
                continue;
            AnalyzeImpl(context, child!, scopeStack, variablesByName, sharedMeta);
        }
    }

    // ── Escape tracking ──

    private static void MarkSubtreeEscaped(Node? node,
        Dictionary<string, Stack<Variable>> variablesByName,
        VariableAnalysisMetadata sharedMeta) {

        if (node is Variable v) {
            MarkVariableEscaped(v, variablesByName, sharedMeta);
            return;
        }
        if (node is not null) {
            foreach (var arg in node.Children) {
                if (arg is not null)
                    MarkSubtreeEscaped(arg, variablesByName, sharedMeta);
            }
        }
    }

    private static void MarkSubtreeEscaped(System.Collections.Generic.IReadOnlyList<Node?> nodes,
        Dictionary<string, Stack<Variable>> variablesByName,
        VariableAnalysisMetadata sharedMeta) {

        for (int i = 0; i < nodes.Count; i++) {
            if (nodes[i] is not null)
                MarkSubtreeEscaped(nodes[i], variablesByName, sharedMeta);
        }
    }

    private static void MarkVariableEscaped(Variable v,
        Dictionary<string, Stack<Variable>> variablesByName,
        VariableAnalysisMetadata sharedMeta) {

        if (variablesByName.TryGetValue(v.Name, out var stack) && stack.Count > 0) {
            var decl = stack.Peek();
            sharedMeta.EscapedVariables.Add(decl);
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