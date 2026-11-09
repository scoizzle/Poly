namespace Poly.Interpretation.Analysis.Semantics;

/// <summary>
/// Enforces the C# iterator rules on <see cref="YieldStatement"/> (#338 slice 1).
/// <list type="bullet">
/// <item><c>IT0001</c> — a yield outside the body of a method returning <c>IEnumerable</c> /
/// <c>IEnumerable&lt;T&gt;</c> (a void or other return type, or no enclosing method at all).</item>
/// <item><c>IT0002</c> — a yield in a lambda.</item>
/// <item><c>IT0003</c> — a yield (return or break) in a <c>finally</c> block.</item>
/// <item><c>IT0004</c> — <c>yield return</c> in a <c>try</c> block that has a <c>catch</c> clause.</item>
/// <item><c>IT0005</c> — <c>yield return</c> in a <c>catch</c> clause.</item>
/// <item><c>IT0006</c> — a yielded value with no implicit conversion to the element type.</item>
/// <item><c>IT0007</c> — a member or index access on a local that holds an iterator call result. The
/// type provider spells <c>IEnumerable&lt;T&gt;</c> as <c>List&lt;T&gt;</c>, but the runtime value is a lazy
/// iterator, so only enumeration (<c>foreach</c>) is sound; <c>e.Count</c> would otherwise type-check and
/// fail at run time. Applies to <c>var e = M()</c> and <c>IEnumerable&lt;T&gt; e = M()</c> alike.</item>
/// </list>
/// <c>yield break</c> is legal in a try-with-catch and in a catch, as in C#. Runs after type
/// resolution, because the value check reads the resolved type of the yielded expression. An element
/// type that does not resolve skips the IT0006 check; the VM still fails closed on the node.
/// </summary>
internal sealed class IteratorLegalityAnalyzer : INodeAnalyzer {
    public const string Id = "IteratorLegality";
    public string PassName => Id;

    private sealed record Scope(
        bool InLambda = false,
        bool IsIterator = false,
        Type? ElementType = null,
        bool InTryWithCatch = false,
        bool InCatch = false,
        bool InFinally = false);

    public void Analyze(AnalysisContext context, Node node) {
        Walk(context, node, new Scope());
        CheckIteratorResultUse(context, node);
    }

    private static void CheckIteratorResultUse(AnalysisContext context, Node root) {
        var iteratorNames = new HashSet<string>();
        CollectIteratorNames(root, iteratorNames);
        if (iteratorNames.Count == 0)
            return;
        var holders = new HashSet<string>();
        CollectHolders(root, iteratorNames, holders);
        if (holders.Count == 0)
            return;
        ReportHolderUse(context, root, holders);
    }

    private static void CollectIteratorNames(Node node, HashSet<string> names) {
        if (node is MethodDefinitionNode { Body: not null } method && !method.IsAsync && ContainsYield(method.Body))
            names.Add(method.Name);
        foreach (var child in node.Children)
            if (child is not null)
                CollectIteratorNames(child, names);
    }

    private static bool ContainsYield(Node node) {
        if (node is YieldStatement)
            return true;
        foreach (var child in node.Children)
            if (child is not null && ContainsYield(child))
                return true;
        return false;
    }

    private static void CollectHolders(Node node, HashSet<string> iteratorNames, HashSet<string> holders) {
        // A typed declaration (IEnumerable<T> e = M()) folds to a cast of the initializer.
        if (node is Assignment { Destination: Variable local } assignment
            && (assignment.Value is TypeCast cast ? cast.Operand : assignment.Value) is Invoke { Delegate: Member call }
            && iteratorNames.Contains(call.MemberName))
            holders.Add(local.Name);
        foreach (var child in node.Children)
            if (child is not null)
                CollectHolders(child, iteratorNames, holders);
    }

    private static void ReportHolderUse(AnalysisContext context, Node node, HashSet<string> holders) {
        if (node is Member { Value: Variable holder } && holders.Contains(holder.Name)) {
            Error(context, node, "IT0007",
                $"'{holder.Name}' holds an iterator result, which can only be enumerated; '{node}' is not available on it.");
        }
        else if (node is IndexAccess { Value: Variable indexed } && holders.Contains(indexed.Name)) {
            Error(context, node, "IT0007",
                $"'{indexed.Name}' holds an iterator result, which can only be enumerated; it cannot be indexed.");
        }
        foreach (var child in node.Children)
            if (child is not null)
                ReportHolderUse(context, child, holders);
    }

    private static void Walk(AnalysisContext context, Node node, Scope scope) {
        switch (node) {
            case MethodDefinitionNode method:
                if (method.Body is not null)
                    Walk(context, method.Body, MethodScope(context, method));
                return;

            case Lambda lambda:
                Walk(context, lambda.Body, new Scope(InLambda: true));
                return;

            case TryCatchFinally tcf: {
                    var hasCatch = tcf.CatchClauses is { Count: > 0 };
                    Walk(context, tcf.TryBlock, scope with { InTryWithCatch = scope.InTryWithCatch || hasCatch });
                    if (tcf.CatchClauses is not null) {
                        foreach (var clause in tcf.CatchClauses)
                            Walk(context, clause.Body, scope with { InCatch = true });
                    }
                    if (tcf.FinallyBlock is not null)
                        Walk(context, tcf.FinallyBlock, scope with { InFinally = true });
                    return;
                }

            case YieldStatement yield:
                Check(context, yield, scope);
                if (yield.Value is not null)
                    Walk(context, yield.Value, scope);
                return;

            default:
                foreach (var child in node.Children) {
                    if (child is not null)
                        Walk(context, child, scope);
                }
                return;
        }
    }

    private static Scope MethodScope(AnalysisContext context, MethodDefinitionNode method) {
        if (method.IsAsync)
            return new Scope();
        switch (method.ReturnType) {
            case NamedTypeReference { TypeArguments: { Count: 1 } args } named when IsEnumerableName(named):
                return new Scope(IsIterator: true, ElementType: ElementClrType(context, args[0]));
            case NamedTypeReference { TypeArguments: null or { Count: 0 } } plain when IsEnumerableName(plain):
                return new Scope(IsIterator: true, ElementType: typeof(object));
            case TypeReference { TypeName: "IEnumerable" or "System.Collections.IEnumerable" }:
                return new Scope(IsIterator: true, ElementType: typeof(object));
            default:
                return new Scope();
        }
    }

    private static bool IsEnumerableName(NamedTypeReference named) {
        var generic = named.TypeArguments is { Count: > 0 };
        return named.FullName switch {
            "IEnumerable" => true,
            "System.Collections.Generic.IEnumerable" => generic,
            "System.Collections.IEnumerable" => !generic,
            _ => false,
        };
    }

    private static Type? ElementClrType(AnalysisContext context, Node element) =>
        element switch {
            PrimitiveTypeReference { IsNullable: false } prim => prim.PrimitiveId.GetClrType(),
            _ => context.GetResolvedType(element)?.GetRuntimeType(),
        };

    private static void Check(AnalysisContext context, YieldStatement yield, Scope scope) {
        if (scope.InLambda) {
            Error(context, yield, "IT0002", "yield cannot appear in a lambda; only an iterator method body may yield.");
            return;
        }
        if (!scope.IsIterator) {
            Error(context, yield, "IT0001",
                "yield is only legal in the body of a method returning IEnumerable or IEnumerable<T>.");
            return;
        }
        if (scope.InFinally) {
            Error(context, yield, "IT0003", "yield cannot appear in a finally block.");
            return;
        }
        if (yield.Value is null)
            return;
        if (scope.InCatch) {
            Error(context, yield, "IT0005", "yield return cannot appear in a catch clause.");
            return;
        }
        if (scope.InTryWithCatch) {
            Error(context, yield, "IT0004", "yield return cannot appear in a try block that has a catch clause.");
            return;
        }
        var valueType = context.GetResolvedType(yield.Value)?.GetRuntimeType();
        if (valueType is not null && scope.ElementType is { } element
            && !SyntaxTypeCompatibilityAnalyzer.IsImplicitlyConvertible(valueType, element)) {
            Error(context, yield, "IT0006",
                $"cannot yield a value of type '{valueType.Name}' from an iterator of '{element.Name}'.");
        }
    }

    private static void Error(AnalysisContext context, Node node, string code, string message) =>
        context.ReportDiagnostic(node, DiagnosticSeverity.Error, message, code);
}

public static class IteratorLegalityExtensions {
    extension(AnalyzerBuilder builder) {
        /// <summary>Adds the iterator legality pass (<c>IT0001</c>–<c>IT0007</c>) to the pipeline.</summary>
        public AnalyzerBuilder UseIteratorLegality() {
            builder.AddAnalyzer(new IteratorLegalityAnalyzer());
            return builder;
        }
    }
}