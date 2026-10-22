using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Interpretation.Analysis;

internal record VariableAnalysisMetadata(
    Dictionary<Block, HashSet<Variable>> BlockScopes,
    Dictionary<Variable, Variable?> VariableReferences,
    Dictionary<Variable, Parameter> ParameterReferences,
    Dictionary<Block, ScopeVertex> ScopeVertices,
    Dictionary<Variable, Node> VariableDeclarationScope,
    Dictionary<Variable, int> AssignmentCount,
    HashSet<Variable> EscapedVariables,
    HashSet<Variable> CapturedVariables,
    HashSet<Parameter> CapturedParameters
) : IAnalysisMetadata;

/// <summary>Links a catch clause body to the synthetic declaration the bind pass
/// created for its variable, so the type pass can stamp the exception type.</summary>
internal sealed record CatchBindingMetadata(Variable Declaration) : IAnalysisMetadata;

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
    // Parameter declarations — a parameter name registered for a lambda or method
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
                // Every Node carries a stable NodeId that participates in record
                // equality, so these maps already distinguish a shadowed declaration
                // from its outer namesake. Key by the node's identity (Id), not by
                // name, and not by instance reference.
                VariableReferences: [],
                ParameterReferences: [],
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
        // A member chain rewritten to a type reference is a type name, not a value: its
        // identifier spine is not a local, so it must not be validated as one.
        if (context.GetNodeReplacement(node) is TypeReference or NamedTypeReference or PrimitiveTypeReference)
            return;

        switch (node) {
            case Block block:
                AnalyzeBlock(context, state, block);
                break;

            case ForEachLoop forEachLoop:
                AnalyzeForEachLoop(context, state, forEachLoop);
                break;

            case ForLoop forLoop:
                AnalyzeForLoop(context, state, forLoop);
                break;

            case TryCatchFinally tcf:
                AnalyzeTryCatchFinally(context, state, tcf);
                break;

            case Lambda lambda:
                AnalyzeLambda(context, state, lambda);
                break;

            case MethodDefinitionNode method:
                AnalyzeMethodDefinition(context, state, method);
                break;

            case ConstructorDefinitionNode constructor:
                AnalyzeConstructorDefinition(context, state, constructor);
                break;

            case Variable variable:
                ValidateVariableReference(context, state, variable);
                AnalyzeChildrenWithState(context, state, node);
                break;

            case Member member:
                // A dotted CLR type name (`System.String`) is folded as a member chain
                // over an identifier. Rewrite it here, where the scope is known, so a
                // declared local named `System` wins over the type name.
                if (TryRewriteDottedTypeName(context, state, member))
                    return;
                AnalyzeChildrenWithState(context, state, node);
                break;

            case Assignment assignment when assignment.Destination is Variable v:
                AnalyzeChildrenWithState(context, state, node);
                if (state.VariablesByName.TryGetValue(v.Name, out var countStack)
                    && countStack.Count > 0
                    && countStack.Peek() is Variable decl) {
                    state.Meta.AssignmentCount.TryGetValue(decl, out var count);
                    state.Meta.AssignmentCount[decl] = count + 1;
                    // The declared type is projected by the type pass through the
                    // binding; this pass only records the write count.
                }
                break;

            case Invoke invoke:
                if (invoke.Delegate is TypeReference { TypeName: "nameof" })
                    return; // the argument is a name, not an expression
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
                    // Link the synthetic declaration to the clause body so the type
                    // pass can stamp it with the resolved exception type. The clause
                    // itself is not a Node.
                    context.SetMetadata(clause.Body, new CatchBindingMetadata(catchVar));
                }
                AnalyzeNode(context, state, clause.Body);
                if (catchVar is not null)
                    UnregisterVariable(state, catchVar.Name);
            }
        }
        if (tcf.FinallyBlock is not null)
            AnalyzeNode(context, state, tcf.FinallyBlock);
    }

    /// <summary>Register a method's value parameters before its body. Return type,
    /// attributes, parameter declarations/defaults, and generic parameters are walked
    /// first, outside the parameter scope.</summary>
    private void AnalyzeMethodDefinition(
        AnalysisContext context, ScopeState state, MethodDefinitionNode method) {
        AnalyzeNode(context, state, method.ReturnType);
        foreach (var attribute in method.Attributes)
            AnalyzeNode(context, state, attribute);
        if (method.Parameters is { } parameters) {
            foreach (var parameter in parameters)
                AnalyzeNode(context, state, parameter);
        }
        if (method.GenericParameters is { } genericParameters) {
            foreach (var generic in genericParameters)
                AnalyzeNode(context, state, generic);
        }

        if (method.Parameters is { } valueParameters) {
            foreach (var parameter in valueParameters)
                RegisterScopedParameter(state, parameter);
        }
        if (method.Body is { } body)
            AnalyzeNode(context, state, body);
        if (method.Parameters is { } declared) {
            foreach (var parameter in declared)
                UnregisterVariable(state, parameter.Name);
        }
    }

    /// <summary>Register a constructor's value parameters before its body and
    /// initializer; parameter declarations/defaults are walked outside the scope.</summary>
    private void AnalyzeConstructorDefinition(
        AnalysisContext context, ScopeState state, ConstructorDefinitionNode constructor) {
        if (constructor.Parameters is { } parameters) {
            foreach (var parameter in parameters)
                AnalyzeNode(context, state, parameter);
        }

        if (constructor.Parameters is { } valueParameters) {
            foreach (var parameter in valueParameters)
                RegisterScopedParameter(state, parameter);
        }
        if (constructor.Body is { } body)
            AnalyzeNode(context, state, body);
        if (constructor.BaseConstructorInvocation is { } baseInvocation)
            AnalyzeNode(context, state, baseInvocation);
        if (constructor.BaseCall is { } baseCall) {
            foreach (var argument in baseCall)
                AnalyzeNode(context, state, argument);
        }
        if (constructor.Parameters is { } declared) {
            foreach (var parameter in declared)
                UnregisterVariable(state, parameter.Name);
        }
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

    /// <summary>A for-loop initializer declaration (<c>for (var i = 0; …)</c>) declares its
    /// variables for the condition, increment, and body; register them before the loop is
    /// walked, or the initializer's own destination and every later use would be undeclared.
    /// The loop's variables go out of scope when the loop ends.</summary>
    private void AnalyzeForLoop(AnalysisContext context, ScopeState state, ForLoop forLoop) {
        var declared = forLoop.DeclaredVariables;
        if (declared is { Count: > 0 }) {
            foreach (var v in declared) {
                RegisterScopedVariable(context, state, v);
                state.Meta.VariableDeclarationScope[v] =
                    state.ScopeStack.Count > 0 ? state.ScopeStack[^1] : forLoop;
            }
        }
        AnalyzeChildrenWithState(context, state, forLoop);
        if (declared is { Count: > 0 }) {
            foreach (var v in declared)
                UnregisterVariable(state, v.Name);
        }
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
            // A local cannot shadow a parameter: the emitter resolves parameter
            // uses by name, so such a local could never be read or written (and C#
            // forbids it). Fail closed rather than mis-bind.
            if (stack.Peek() is Parameter)
                context.ReportError(variable,
                    $"Variable '{variable.Name}' cannot shadow a parameter of the same name.");
            else
                context.ReportWarning(variable, $"Variable '{variable.Name}' shadows outer scope variable");
        }
        stack.Push(variable);
    }

    private static void UnregisterVariable(ScopeState state, string name) {
        if (state.VariablesByName.TryGetValue(name, out var stack) && stack.Count > 0)
            stack.Pop();
    }

    /// <summary>Rewrite a pure identifier spine that spells a CLR type, unless its
    /// root is a declared name in scope (a local `System` shadows the type). Returns
    /// true when the node was replaced; the caller must not descend then.</summary>
    private static bool TryRewriteDottedTypeName(
        AnalysisContext context, ScopeState state, Member member) {
        if (context.GetNodeReplacement(member) is not null)
            return false;
        if (!DottedTypeNames.TryGetSpine(member, out var dotted, out var root))
            return false;
        // UnregisterVariable only pops; a name that has left scope keeps an empty
        // stack in the map. Only a live declaration wins over the type name.
        if (state.VariablesByName.TryGetValue(root.Name, out var rootStack) && rootStack.Count > 0)
            return false;
        if (!DottedTypeNames.TryResolveType(context, member, out var clr))
            return false;
        context.SetNodeReplacement(member, new ClrTypeReference(clr.RuntimeType));
        context.SetResolvedType(member, clr);
        return true;
    }

    private static void RegisterScopedParameter(ScopeState state, Parameter parameter) {
        if (!state.VariablesByName.TryGetValue(parameter.Name, out var stack)) {
            stack = new Stack<Node>();
            state.VariablesByName[parameter.Name] = stack;
        }
        stack.Push(parameter);
    }

    private void ValidateVariableReference(AnalysisContext context, ScopeState state, Variable variable) {
        // A node rewritten to a type reference (e.g. a member chain that spelled a dotted
        // CLR type name) is a type name, not a local reference.
        if (context.GetNodeReplacement(variable) is TypeReference or NamedTypeReference or PrimitiveTypeReference)
            return;
        if (state.VariablesByName.TryGetValue(variable.Name, out var stack) && stack.Count > 0) {
            // The declaration edge is the only type source for a use; the type pass
            // projects through it. Attach the shared bag to the use either way.
            var metadata = GetOrCreateMetadata(context, state.Meta, variable);
            if (stack.Peek() is Variable declaration)
                metadata.VariableReferences[variable] = declaration;
            else if (stack.Peek() is Parameter parameter)
                metadata.ParameterReferences[variable] = parameter;
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