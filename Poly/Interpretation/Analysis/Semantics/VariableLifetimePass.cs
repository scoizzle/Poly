using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Interpretation.Analysis;

internal record VariableAnalysisMetadata(
    Dictionary<Block, HashSet<Variable>> BlockScopes,
    Dictionary<Variable, Variable?> VariableReferences,
    Dictionary<Block, ScopeVertex> ScopeVertices,
    Dictionary<Variable, Node> VariableDeclarationScope,
    Dictionary<Variable, int> AssignmentCount,
    HashSet<Variable> EscapedVariables,
    HashSet<Variable> CapturedVariables,
    HashSet<Parameter> CapturedParameters
) : IAnalysisMetadata;

/// <summary>One free binding of a <see cref="Lambda"/>, in tree-walk order.</summary>
internal readonly record struct LambdaCaptureBinding(Variable? Variable, Parameter? Parameter);

/// <summary>Free variables/parameters of a <see cref="Lambda"/>, in walk order.
/// Emit uses this so stored closures and function-table bodies agree.</summary>
internal sealed record LambdaCaptureMetadata(
    IReadOnlyList<LambdaCaptureBinding> Bindings
) : IAnalysisMetadata;

internal record ScopeVertex(Block Block, ScopeVertex? Parent, HashSet<Variable> Declared);

/// <summary>Per-analysis mutable state threaded through recursive calls.</summary>
internal sealed record ScopeState(
    VariableAnalysisMetadata Meta,
    List<Block> ScopeStack,
    // Names in scope, innermost last. Values are Variable declarations or
    // lambda Parameter declarations — a parameter name registered for a lambda
    // body shadows an outer variable of the same name.
    Dictionary<string, Stack<Node>> VariablesByName
);

internal sealed class ScopeValidator : INodeAnalyzer {
    public const string Id = "VariableScope";
    public string PassName => Id;
    public void Analyze(AnalysisContext context, Node node) {
        var state = new ScopeState(
            Meta: new(
                BlockScopes: [],
                VariableReferences: [],
                ScopeVertices: [],
                VariableDeclarationScope: [],
                AssignmentCount: [],
                EscapedVariables: [],
                CapturedVariables: new(ReferenceEqualityComparer.Instance),
                CapturedParameters: new(ReferenceEqualityComparer.Instance)
            ),
            ScopeStack: [],
            VariablesByName: []
        );
        // Stash the metadata on the root node so any consumer (e.g. lowering)
        // can find it via analysis.GetMetadata<>(rootNode) or FindAnyVariable.
        context.SetMetadata(node, state.Meta);

        AnalyzeNode(context, state, node);
        LambdaCaptureCollector.Attach(context, node, state.Meta);
    }

    /// <summary>Analyze child nodes using the current state, NOT creating fresh
    /// per-child state as <c>this.AnalyzeChildren</c> would.</summary>
    private void AnalyzeChildrenWithState(AnalysisContext context, ScopeState state, Node node) {
        foreach (var child in node.Children) {
            if (child is not null)
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

            case TryCatchFinally tcf:
                AnalyzeTryCatchFinally(context, state, tcf);
                break;

            case Lambda lambda:
                AnalyzeLambda(context, state, lambda);
                break;

            case Variable variable:
                ValidateVariableReference(context, state, variable);
                AnalyzeChildrenWithState(context, state, node);
                break;

            case Assignment assignment when assignment.Destination is Variable v:
                AnalyzeChildrenWithState(context, state, node);
                if (state.VariablesByName.TryGetValue(v.Name, out var countStack)
                    && countStack.Count > 0
                    && countStack.Peek() is Variable decl) {
                    state.Meta.AssignmentCount.TryGetValue(decl, out var count);
                    state.Meta.AssignmentCount[decl] = count + 1;
                    // The destination carries the variable's declared type, not the
                    // assigned value's type. The VM stores raw slots, so the emitter
                    // must know the variable's representation to convert on write
                    // (`double x = 1; x = 2;` would otherwise store the integer 2 where
                    // the double path reads IEEE bits).
                    if (context.GetResolvedType(decl) is { } declaredType)
                        context.SetResolvedType(v, declaredType);
                }
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

    private void AnalyzeTryCatchFinally(AnalysisContext context, ScopeState state, TryCatchFinally tcf) {
        AnalyzeNode(context, state, tcf.TryBlock);
        if (tcf.CatchClauses is not null) {
            foreach (var clause in tcf.CatchClauses) {
                if (clause.ExceptionType is not null)
                    AnalyzeNode(context, state, clause.ExceptionType);
                Variable? catchVar = null;
                if (!string.IsNullOrEmpty(clause.VariableName)) {
                    catchVar = new Variable(clause.VariableName);
                    RegisterScopedVariable(context, state, catchVar);
                    state.Meta.VariableDeclarationScope[catchVar] =
                        state.ScopeStack.Count > 0 ? state.ScopeStack[^1] : tcf;
                }
                AnalyzeNode(context, state, clause.Body);
                if (catchVar is not null)
                    UnregisterVariable(state, catchVar.Name);
            }
        }
        if (tcf.FinallyBlock is not null)
            AnalyzeNode(context, state, tcf.FinallyBlock);
    }

    /// <summary>Register the lambda's parameters before its body so a body
    /// <see cref="Variable"/> of the same name resolves to the parameter (and
    /// shadows an outer variable rather than silently reading it).</summary>
    private void AnalyzeLambda(AnalysisContext context, ScopeState state, Lambda lambda) {
        foreach (var parameter in lambda.Parameters)
            RegisterScopedParameter(state, parameter);
        AnalyzeNode(context, state, lambda.Body);
        foreach (var parameter in lambda.Parameters)
            UnregisterVariable(state, parameter.Name);
    }

    private void AnalyzeForEachLoop(AnalysisContext context, ScopeState state, ForEachLoop forEachLoop) {
        AnalyzeNode(context, state, forEachLoop.Collection);
        MarkSubtreeEscaped(state, forEachLoop.Collection);
        RegisterScopedVariable(context, state, forEachLoop.LoopVariable);
        state.Meta.VariableDeclarationScope[forEachLoop.LoopVariable] =
            state.ScopeStack.Count > 0 ? state.ScopeStack[^1] : forEachLoop;
        AnalyzeNode(context, state, forEachLoop.Body);
        UnregisterVariable(state, forEachLoop.LoopVariable.Name);
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
            if (vars[i] is Variable v) UnregisterVariable(state, v.Name);
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
            stack = new Stack<Node>();
            state.VariablesByName[variable.Name] = stack;
        }
        if (stack.Count > 0) {
            // A local cannot shadow a lambda parameter: the emitter resolves parameter
            // uses by name, so such a local could never be read or written (and C#
            // forbids it). Fail closed rather than mis-bind.
            if (stack.Peek() is Parameter)
                context.ReportError(variable,
                    $"Variable '{variable.Name}' cannot shadow a lambda parameter of the same name.");
            else
                context.ReportWarning(variable, $"Variable '{variable.Name}' shadows outer scope variable");
        }
        stack.Push(variable);
    }

    private static void UnregisterVariable(ScopeState state, string name) {
        if (state.VariablesByName.TryGetValue(name, out var stack) && stack.Count > 0)
            stack.Pop();
    }

    private static void RegisterScopedParameter(ScopeState state, Parameter parameter) {
        if (!state.VariablesByName.TryGetValue(parameter.Name, out var stack)) {
            stack = new Stack<Node>();
            state.VariablesByName[parameter.Name] = stack;
        }
        stack.Push(parameter);
    }

    private void ValidateVariableReference(AnalysisContext context, ScopeState state, Variable variable) {
        if (state.VariablesByName.TryGetValue(variable.Name, out var stack) && stack.Count > 0) {
            // A parameter declaration resolves the name but is not a Variable;
            // its emit slot is recovered by the emitter's parameter maps.
            if (stack.Peek() is Variable declaration) {
                var metadata = GetOrCreateMetadata(context, state.Meta, variable);
                metadata.VariableReferences[variable] = declaration;
                // A local's type is inferred once, on its declaration (ResolveBlockType);
                // every use of the name is a distinct Variable node. Carry the declaration's
                // resolved type onto the use, or type-directed decisions (string +, casts,
                // arithmetic categories) never see a local's type.
                if (context.GetResolvedType(variable) is null
                    && context.GetResolvedType(declaration) is { } declaredType)
                    context.SetResolvedType(variable, declaredType);
            }
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
        if (state.VariablesByName.TryGetValue(v.Name, out var stack)
            && stack.Count > 0
            && stack.Peek() is Variable decl) {
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