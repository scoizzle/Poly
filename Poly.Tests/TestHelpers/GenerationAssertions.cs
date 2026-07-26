using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DslCompiler;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// Structural assertion helpers for codegen IR trees.
/// Lets tests assert on the Syntax IR (CompilationUnitNode) directly
/// instead of string-comparing rendered C#, avoiding formatting brittleness.
/// </summary>
internal static class GenerationAssertions {
    // ── IR Builders ─────────────────────────────────────────────

    /// <summary>
    /// Produces the IR CompilationUnitNode for a DbContext from the given domain.
    /// </summary>
    public static CompilationUnitNode DbContextIr(Domain domain) {
        var analysis = DomainModelAnalyzer.Analyze(domain);
        var storage = analysis.GetMetadata<StorageMappingMetadata>(domain)?.Storage
            ?? throw new InvalidOperationException("Domain analysis did not produce StorageMappingMetadata.");
        var gen = new DbContextGenerator(domain, storage);
        return gen.GenerateCompilationUnit();
    }

    /// <summary>
    /// Produces the IR CompilationUnitNode for a MinimalApi program from the given domain.
    /// </summary>
    public static CompilationUnitNode MinimalApiIr(Domain domain) {
        var analysis = DomainModelAnalyzer.Analyze(domain);
        var storage = analysis.GetMetadata<StorageMappingMetadata>(domain)?.Storage
            ?? throw new InvalidOperationException("Domain analysis did not produce StorageMappingMetadata.");
        var behavior = analysis.GetMetadata<BehaviorMetadata>(domain)?.Behavior
            ?? throw new InvalidOperationException("Domain analysis did not produce BehaviorMetadata.");
        var aggregate = analysis.GetMetadata<OwnershipAggregateMetadata>(domain)?.Aggregate
            ?? throw new InvalidOperationException("Domain analysis did not produce OwnershipAggregateMetadata.");
        var gen = new MinimalApiGenerator(domain, storageModel: storage, behaviorModel: behavior, aggregateModel: aggregate);
        return gen.GenerateCompilationUnit("TDbCtx");
    }

    // ── Type/method/property lookup ────────────────────────

    public static TypeDefinitionNode? FindType(this CompilationUnitNode unit, string name) =>
        unit.Types.FirstOrDefault(t => t.Name == name);

    public static MethodDefinitionNode? FindMethod(this TypeDefinitionNode type, string name) =>
        type.Methods?.FirstOrDefault(m => m.Name == name);

    public static PropertyDefinitionNode? FindProperty(this TypeDefinitionNode type, string name) =>
        type.Properties?.FirstOrDefault(p => p.Name == name);

    // ── Invocation finding in method bodies ─────────────────

    /// <summary>
    /// Finds all Invoke nodes whose resolved method name matches <paramref name="methodName"/>,
    /// recursively walking the given node tree. Handles fluent chains where the name
    /// is the first call in the chain (e.g. "Property" in Property.HasColumnName).
    /// </summary>
    public static List<Invoke> FindInvocations(this Node node, string methodName) {
        var results = new List<Invoke>();
        WalkForInvocations(node, methodName, results);
        return results;
    }

    /// <summary>
    /// Resolves the full method-chain name of an Invoke.
    /// For b.ToTable("Items") returns "ToTable".
    /// For b.Property(...).HasColumnName(...) returns "Property.HasColumnName".
    /// </summary>
    public static string ResolveMethodName(this Invoke invoke) {
        var parts = new List<string>();
        var current = invoke.Delegate;
        while (current is Member member) {
            parts.Insert(0, member.MemberName);
            current = member.Value;
        }
        return string.Join(".", parts);
    }

    /// <summary>
    /// Walks a fluent chain from the outermost Invoke inward, returning method names.
    /// For b.Property(x => x.X).HasColumnName("Y").HasColumnType("varchar"),
    /// returns ["Property.HasColumnName.HasColumnType", "Property.HasColumnName", "Property"].
    /// The first element is the full chain; the last is the root.
    /// </summary>
    public static List<string> GetFluentChain(this Invoke outermost) {
        var chain = new List<string>();
        var current = outermost;
        while (true) {
            chain.Add(current.ResolveMethodName());
            if (current.Delegate is Member { Value: Invoke inner }) {
                current = inner;
            }
            else break;
        }
        return chain;
    }

    /// <summary>
    /// Returns the set of all individual method names in top-level invocations,
    /// splitting fluent chains. E.g. "Services.AddDbContext" yields {"Services", "AddDbContext"}.
    /// </summary>
    public static HashSet<string> TopLevelInvocationNames(this CompilationUnitNode unit) {
        var names = new HashSet<string>();
        if (unit.TopLevelStatements is not null) {
            foreach (var stmt in unit.TopLevelStatements) {
                if (stmt is Invoke inv)
                    AddMethodNames(inv.ResolveMethodName(), names);
                else if (stmt is Variable var && var.Value is Invoke varInv)
                    AddMethodNames(varInv.ResolveMethodName(), names);
            }
        }
        return names;
    }

    private static void AddMethodNames(string chain, HashSet<string> names) {
        foreach (var part in chain.Split('.'))
            names.Add(part);
    }

    /// <summary>
    /// Renders the IR to string via CSharpGenerator (for fallback or debugging).
    /// </summary>
    public static string Render(this CompilationUnitNode unit) =>
        new CSharpGenerator().Generate(unit);

    // ── Internals ───────────────────────────────────────────────

    private static void WalkForInvocations(Node node, string targetMethod, List<Invoke> results) {
        if (node is Invoke invoke) {
            var name = invoke.ResolveMethodName();
            var nameParts = name.Split('.');
            // Match if any segment equals the target (handles fluent chains like Services.AddDbContext)
            if (name == targetMethod || nameParts.Contains(targetMethod))
                results.Add(invoke);

            // Walk arguments and delegate for recursive content
            WalkForInvocations(invoke.Delegate, targetMethod, results);
            foreach (var arg in invoke.Arguments)
                WalkForInvocations(arg, targetMethod, results);
            return;
        }

        if (node is Block block) {
            foreach (var expr in block.Nodes)
                WalkForInvocations(expr, targetMethod, results);
            return;
        }

        if (node is Conditional conditional) {
            WalkForInvocations(conditional.Condition, targetMethod, results);
            WalkForInvocations(conditional.IfTrue, targetMethod, results);
            WalkForInvocations(conditional.IfFalse, targetMethod, results);
            return;
        }

        if (node is Lambda lambda) {
            WalkForInvocations(lambda.Body, targetMethod, results);
            return;
        }

        // For any other node (MethodDefinitionNode, TypeDefinitionNode, etc.),
        // walk children to find nested invocations
        foreach (var child in node.Children) {
            if (child is not null)
                WalkForInvocations(child, targetMethod, results);
        }
    }

    private static object? ArgToValue(Node arg) => arg switch {
        Constant c => c.Value,
        Variable v => v.Name,
        Parameter p => p.Name,
        Member m => $"{ArgToValue(m.Value)}.{m.MemberName}",
        Lambda l => $"({string.Join(", ", l.Parameters.Select(p => p.Name))}) => ...",
        Invoke i => i.ResolveMethodName(),
        _ => arg.ToString()
    };
}