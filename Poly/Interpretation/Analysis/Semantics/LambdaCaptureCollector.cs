namespace Poly.Interpretation.Analysis;

/// <summary>
/// Walks each <see cref="Lambda"/> and records free <see cref="Variable"/> /
/// <see cref="Parameter"/> bindings in tree-walk order. A <see cref="Variable"/>
/// with a sticky <c>Initializer</c> is a local only when declared by the lambda
/// (block variables or a declare-init statement), not when used as a capture.
/// </summary>
internal static class LambdaCaptureCollector {
    public static void Attach(AnalysisContext context, Node root, VariableAnalysisMetadata meta) {
        Walk(context, root, meta);
    }

    private static void Walk(AnalysisContext context, Node node, VariableAnalysisMetadata meta) {
        if (node is Lambda lambda) {
            var bindings = Collect(lambda);
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

    internal static IReadOnlyList<LambdaCaptureBinding> Collect(Lambda lambda) {
        var declared = new HashSet<Variable>(ReferenceEqualityComparer.Instance);
        CollectDeclaredLocals(lambda.Body, declared);
        var result = new List<LambdaCaptureBinding>();
        var seenVars = new HashSet<Variable>(ReferenceEqualityComparer.Instance);
        var seenParams = new HashSet<Parameter>(ReferenceEqualityComparer.Instance);
        var ownParams = new HashSet<Parameter>(lambda.Parameters, ReferenceEqualityComparer.Instance);
        CollectRecursive(lambda.Body, declared, ownParams, seenVars, seenParams, result);
        return result;
    }

    internal static void CollectDeclaredLocals(Node node, HashSet<Variable> declared) {
        if (node is Lambda)
            return;
        if (node is Block block) {
            foreach (var v in block.Variables) {
                if (v is Variable variable)
                    declared.Add(variable);
            }
            foreach (var stmt in block.Nodes) {
                if (stmt is Variable declaredVar && declaredVar.Initializer is not null)
                    declared.Add(declaredVar);
            }
        }
        if (node is ForEachLoop fe)
            declared.Add(fe.LoopVariable);
        foreach (var child in node.Children) {
            if (child is not null)
                CollectDeclaredLocals(child, declared);
        }
    }

    private static void CollectRecursive(
        Node node,
        HashSet<Variable> declared,
        HashSet<Parameter> ownParams,
        HashSet<Variable> seenVars,
        HashSet<Parameter> seenParams,
        List<LambdaCaptureBinding> result) {
        if (node is Lambda nested) {
            var nestedOwn = new HashSet<Parameter>(ownParams, ReferenceEqualityComparer.Instance);
            foreach (var np in nested.Parameters)
                nestedOwn.Add(np);
            var nestedDeclared = new HashSet<Variable>(declared, ReferenceEqualityComparer.Instance);
            CollectDeclaredLocals(nested.Body, nestedDeclared);
            CollectRecursive(nested.Body, nestedDeclared, nestedOwn, seenVars, seenParams, result);
            return;
        }
        if (node is Variable v && seenVars.Add(v) && !declared.Contains(v)) {
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
                CollectRecursive(child, declared, ownParams, seenVars, seenParams, result);
        }
    }
}