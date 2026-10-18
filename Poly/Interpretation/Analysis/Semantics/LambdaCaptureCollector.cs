namespace Poly.Interpretation.Analysis;

/// <summary>
/// Walks each <see cref="Lambda"/> and records free <see cref="Variable"/> /
/// <see cref="Parameter"/> bindings in tree-walk order. A <see cref="Variable"/>
/// is a local only when declared by the lambda (<see cref="Block.Variables"/> or
/// a foreach loop variable), not when used as a capture.
/// </summary>
internal static class LambdaCaptureCollector {
    public static void Attach(AnalysisContext context, Node root, VariableAnalysisMetadata meta) {
        Walk(context, root, meta);
    }

    private static void Walk(AnalysisContext context, Node node, VariableAnalysisMetadata meta) {
        if (node is Lambda lambda) {
            var bindings = Collect(lambda, context.GetNodeReplacement);
            context.SetMetadata(lambda, new LambdaCaptureMetadata(bindings));
            foreach (var b in bindings) {
                if (b.Variable is { } v)
                    meta.CapturedVariables.Add(v);
                if (b.Parameter is { } p)
                    meta.CapturedParameters.Add(p);
            }
            Walk(context, lambda.Body, meta);
            return;
        }
        foreach (var child in node.Children) {
            if (child is not null)
                Walk(context, child, meta);
        }
    }

    internal static IReadOnlyList<LambdaCaptureBinding> Collect(Lambda lambda, Func<Node, Node?>? replacement = null) {
        var declared = new HashSet<Variable>(ReferenceEqualityComparer.Instance);
        CollectDeclaredLocals(lambda.Body, declared, replacement);
        var result = new List<LambdaCaptureBinding>();
        var seenVars = new HashSet<Variable>(ReferenceEqualityComparer.Instance);
        var seenParams = new HashSet<Parameter>(ReferenceEqualityComparer.Instance);
        var ownParams = new HashSet<Parameter>(lambda.Parameters, ReferenceEqualityComparer.Instance);
        CollectRecursive(lambda.Body, declared, ownParams, seenVars, seenParams, result, replacement);
        return result;
    }

    internal static void CollectDeclaredLocals(Node node, HashSet<Variable> declared, Func<Node, Node?>? replacement = null) {
        if (node is Lambda || IsTypeNameReplacement(node, replacement))
            return;
        if (node is Block block) {
            foreach (var v in block.Variables) {
                if (v is Variable variable)
                    declared.Add(variable);
            }
        }
        if (node is ForEachLoop fe)
            declared.Add(fe.LoopVariable);
        foreach (var child in node.Children) {
            if (child is not null)
                CollectDeclaredLocals(child, declared, replacement);
        }
    }

    /// <summary>A member chain rewritten to a type reference is a type name, not a value:
    /// its identifier spine is not a capture.</summary>
    private static bool IsTypeNameReplacement(Node node, Func<Node, Node?>? replacement) =>
        replacement?.Invoke(node) is TypeReference or NamedTypeReference or PrimitiveTypeReference;

    private static void CollectRecursive(
        Node node,
        HashSet<Variable> declared,
        HashSet<Parameter> ownParams,
        HashSet<Variable> seenVars,
        HashSet<Parameter> seenParams,
        List<LambdaCaptureBinding> result,
        Func<Node, Node?>? replacement) {
        if (IsTypeNameReplacement(node, replacement))
            return;
        // A `nameof(arg)` argument is a name, not an expression to capture.
        if (node is Invoke { Delegate: TypeReference { TypeName: "nameof" } })
            return;
        if (node is Lambda nested) {
            var nestedOwn = new HashSet<Parameter>(ownParams, ReferenceEqualityComparer.Instance);
            foreach (var np in nested.Parameters)
                nestedOwn.Add(np);
            var nestedDeclared = new HashSet<Variable>(declared, ReferenceEqualityComparer.Instance);
            CollectDeclaredLocals(nested.Body, nestedDeclared, replacement);
            CollectRecursive(nested.Body, nestedDeclared, nestedOwn, seenVars, seenParams, result, replacement);
            return;
        }
        // The reader emits a fresh Variable per identifier, so a body use of a
        // lambda parameter is a Variable whose name matches an own parameter:
        // it is the parameter, not a capture of some outer variable.
        if (node is Variable v && seenVars.Add(v) && !declared.Contains(v)
            && ownParams.All(p => p.Name != v.Name)) {
            result.Add(new LambdaCaptureBinding(v, null));
            return;
        }
        if (node is Parameter p && seenParams.Add(p)) {
            if (!ownParams.Contains(p) && ownParams.All(op => op.Name != p.Name))
                result.Add(new LambdaCaptureBinding(null, p));
            return;
        }
        foreach (var child in node.Children) {
            if (child is not null)
                CollectRecursive(child, declared, ownParams, seenVars, seenParams, result, replacement);
        }
    }
}