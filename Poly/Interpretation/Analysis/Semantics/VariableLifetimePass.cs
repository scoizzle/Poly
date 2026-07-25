using Poly.Interpretation.Analysis.Semantics;
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

/// <summary>Per-analysis mutable state threaded through recursive calls.</summary>
internal sealed record ScopeState(
    VariableAnalysisMetadata Meta,
    List<Block> ScopeStack,
    Dictionary<string, Stack<Variable>> VariablesByName
);

internal sealed class ScopeValidator : INodeAnalyzer {
    public const string Id = "VariableScope";
    public string PassName => Id;
    public string[] Dependencies => [TypeAndMemberResolver.Id];
    public void Analyze(AnalysisContext context, Node node) {
        var state = new ScopeState(
            Meta: new(
                BlockScopes: [],
                VariableReferences: [],
                ScopeVertices: [],
                VariableDeclarationScope: [],
                AssignmentCount: [],
                EscapedVariables: []
            ),
            ScopeStack: [],
            VariablesByName: []
        );
        // Stash the metadata on the root node so any consumer (e.g. lowering)
        // can find it via analysis.GetMetadata<>(rootNode) or FindAnyVariable.
        context.SetMetadata(node, state.Meta);

        AnalyzeNode(context, state, node);
    }

    /// <summary>Analyze child nodes using the current state, NOT creating fresh
    /// per-child state as <c>this.AnalyzeChildren</c> would.</summary>
    private void AnalyzeChildrenWithState(AnalysisContext context, ScopeState state, Node node) {
        foreach (var child in node.Children) {
            if (child is not null && context.ShouldAnalyze(child))
                AnalyzeNode(context, state, child);
        }
    }

    private void AnalyzeNode(AnalysisContext context, ScopeState state, Node node) {
        switch (node) {
            case Block block:
                AnalyzeBlock(context, state, block);
                break;

            case ForEachLoop forEachLoop:
                AnalyzeForEachLoop(context, state, forEachLoop);
                break;

            case Variable variable when variable.Value == null:
                ValidateVariableReference(context, state, variable);
                AnalyzeChildrenWithState(context, state, node);
                break;

            case Assignment assignment when assignment.Destination is Variable v:
                if (!state.VariablesByName.TryGetValue(v.Name, out var stack) || stack.Count == 0)
                    RegisterScopedVariable(context, state, v);
                else
                    ValidateVariableReference(context, state, v);
                if (state.VariablesByName.TryGetValue(v.Name, out var countStack) && countStack.Count > 0) {
                    var decl = countStack.Peek();
                    state.Meta.AssignmentCount.TryGetValue(decl, out var count);
                    state.Meta.AssignmentCount[decl] = count + 1;
                }
                AnalyzeChildrenWithState(context, state, node);
                break;

            case Invoke invoke:
                if (invoke.Delegate is not Lambda)
                    MarkSubtreeEscaped(state, invoke.Arguments);
                AnalyzeChildrenWithState(context, state, node);
                break;

            case Return r:
                MarkSubtreeEscaped(state, r.Value);
                AnalyzeChildrenWithState(context, state, node);
                break;

            default:
                AnalyzeChildrenWithState(context, state, node);
                break;
        }
    }

    private void AnalyzeForEachLoop(AnalysisContext context, ScopeState state, ForEachLoop forEachLoop) {
        AnalyzeNode(context, state, forEachLoop.Collection);
        MarkSubtreeEscaped(state, forEachLoop.Collection);
        RegisterScopedVariable(context, state, forEachLoop.LoopVariable);
        state.Meta.VariableDeclarationScope[forEachLoop.LoopVariable] =
            state.ScopeStack.Count > 0 ? state.ScopeStack[^1] : forEachLoop;
        AnalyzeNode(context, state, forEachLoop.Body);
        UnregisterVariable(state, forEachLoop.LoopVariable);
    }

    private void AnalyzeBlock(AnalysisContext context, ScopeState state, Block block) {
        state.ScopeStack.Add(block);

        var parent = state.ScopeStack.Count > 1
            ? state.Meta.ScopeVertices.GetValueOrDefault(state.ScopeStack[^2])
            : null;
        var vars = block.Variables;
        for (int i = 0; i < vars.Count; i++) {
            if (vars[i] is Variable v) RegisterVariable(context, state, v, block);
        }

        state.Meta.ScopeVertices[block] = new ScopeVertex(block, parent,
            state.Meta.BlockScopes.GetValueOrDefault(block) ?? []);

        AnalyzeChildrenWithState(context, state, block);

        for (int i = 0; i < vars.Count; i++) {
            if (vars[i] is Variable v) UnregisterVariable(state, v);
        }

        state.ScopeStack.RemoveAt(state.ScopeStack.Count - 1);
    }

    private void RegisterVariable(AnalysisContext context, ScopeState state, Variable variable, Block scope) {
        RegisterScopedVariable(context, state, variable);

        var meta = GetOrCreateMetadata(context, state.Meta, variable);
        if (!meta.BlockScopes.TryGetValue(scope, out var scopeVars)) {
            scopeVars = [];
            meta.BlockScopes[scope] = scopeVars;
        }

        scopeVars.Add(variable);
        state.Meta.VariableDeclarationScope[variable] = scope;
    }

    private static void RegisterScopedVariable(AnalysisContext context, ScopeState state, Variable variable) {
        if (!state.VariablesByName.TryGetValue(variable.Name, out var stack)) {
            stack = new Stack<Variable>();
            state.VariablesByName[variable.Name] = stack;
        }
        if (stack.Count > 0)
            context.ReportWarning(variable, $"Variable '{variable.Name}' shadows outer scope variable");
        stack.Push(variable);
    }

    private static void UnregisterVariable(ScopeState state, Variable variable) {
        if (state.VariablesByName.TryGetValue(variable.Name, out var stack) && stack.Count > 0)
            stack.Pop();
    }

    private void ValidateVariableReference(AnalysisContext context, ScopeState state, Variable variable) {
        if (state.VariablesByName.TryGetValue(variable.Name, out var stack) && stack.Count > 0) {
            var declaration = stack.Peek();
            var metadata = GetOrCreateMetadata(context, state.Meta, variable);
            metadata.VariableReferences[variable] = declaration;
        }
        else {
            context.ReportError(variable, $"Variable '{variable.Name}' is not declared in this scope");
        }
    }

    private static VariableAnalysisMetadata GetOrCreateMetadata(AnalysisContext context, VariableAnalysisMetadata meta, Node node) =>
        context.Metadata.GetOrAdd(node, () => meta);

    private static void MarkSubtreeEscaped(ScopeState state, Node? node) {
        if (node is Variable v) {
            MarkVariableEscaped(state, v);
            return;
        }
        if (node is not null) {
            foreach (var child in node.Children) {
                if (child is not null)
                    MarkSubtreeEscaped(state, child);
            }
        }
    }

    private static void MarkSubtreeEscaped(ScopeState state, IReadOnlyList<Node?> nodes) {
        for (int i = 0; i < nodes.Count; i++) {
            if (nodes[i] is not null)
                MarkSubtreeEscaped(state, nodes[i]);
        }
    }

    private static void MarkVariableEscaped(ScopeState state, Variable v) {
        if (state.VariablesByName.TryGetValue(v.Name, out var stack) && stack.Count > 0) {
            var decl = stack.Peek();
            state.Meta.EscapedVariables.Add(decl);
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