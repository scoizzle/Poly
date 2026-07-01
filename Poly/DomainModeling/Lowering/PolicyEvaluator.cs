using Poly.Data.Modeling;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.LinqExpressions;
using Poly.Interpretation.Vm;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Syntax.Analysis;

using PrimReturn = Poly.Syntax.Primitives.Return;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Lowers a <see cref="Policy"/>'s <see cref="DomainExpression"/> guard
/// to an executable predicate and evaluates it against an entity instance.
/// </summary>
public static class PolicyEvaluator {
    private static readonly Analyzer LinqAnalyzer = new AnalyzerBuilder()
        .UseTypeAndMemberResolver()
        .UseSideEffectAnalysis()
        .UseVariableScopeValidator()
        .UseDefiniteAssignmentAnalysis()
        .Build();

    /// <summary>
    /// Compiles <paramref name="policy"/> into a <c>Func&lt;TEntity, bool&gt;</c>
    /// that evaluates the policy's guard expression against an entity instance.
    /// Uses <c>LinqExpressionGenerator</c> to produce a LINQ expression tree.
    /// </summary>
    public static Func<TEntity, bool> CompileLinqPredicate<TEntity>(this Policy policy) {
        ArgumentNullException.ThrowIfNull(policy);

        var entityParam = new Parameter("entity", TypeReference.To<TEntity>());
        var pass = new DomainExpressionLoweringPass();
        var lowered = pass.Lower(policy.Expression, entityParam);

        var analysis = LinqAnalyzer.Analyze(lowered);

        var generator = new LinqExpressionGenerator(analysis);
        var compiled = generator.Compile(lowered);
        var entityExpr = compiled.Parameters.FirstOrDefault(p => p.Name == "entity")
            ?? Expression.Parameter(typeof(TEntity), "entity");

        var lambda = Expression
            .Lambda<Func<TEntity, bool>>(compiled.Expression, entityExpr);

        return lambda.Compile();
    }

    /// <summary>
    /// Compiles <paramref name="policy"/> into a <c>Func&lt;TEntity, bool&gt;</c>
    /// that evaluates the policy's guard expression against an entity instance.
    /// Uses the new <c>ExpansionPass</c> + <c>CompilePrimitives</c> pipeline.
    /// </summary>
    public static Func<TEntity, bool> CompileVMPredicate<TEntity>(this Policy policy) {
        ArgumentNullException.ThrowIfNull(policy);

        var entityParam = new Parameter("entity", TypeReference.To<TEntity>());
        var pass = new DomainExpressionLoweringPass();
        var lowered = pass.Lower(policy.Expression, entityParam);

        // New pipeline: ExpansionPass → CompilePrimitives
        var analysis = new AnalyzerBuilder()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .AddAnalyzer(new ExpansionPass())
            .Build()
            .Analyze(lowered);

        var meta = analysis.GetMetadata<Poly.Interpretation.Analysis.PrimitiveExpansionMetadata>(lowered);
        System.Collections.Generic.IReadOnlyList<Poly.Syntax.Primitives.PrimitiveNode> primitives;
        if (meta is not null) {
            primitives = meta.Primitives;
        }
        else {
            // Fallback: call ToPrimitives directly if ExpansionPass didn't run
            var ctx = new AnalysisContext(ClrTypeDefinitionRegistry.Shared);
            primitives = lowered.ToPrimitives(ctx).ToArray();
        }

        var primsList = primitives.ToList();
        primsList.Add(new PrimReturn());
        var linked = Poly.Interpretation.Vm.PrimitiveLinker.Link(primsList);
        var compiled = ProgramCompiler.CompilePrimitives(linked);

        return e => {
            var result = Vm.Execute(compiled, e);
            return result.GetValue<bool>();
        };
    }

    /// <summary>
    /// Evaluates <paramref name="policy"/> against <paramref name="entity"/>
    /// and returns <c>true</c> if the policy's guard expression is satisfied.
    /// </summary>
    public static bool Evaluate<TEntity>(this Policy policy, TEntity entity) {
        var predicate = policy.CompileLinqPredicate<TEntity>();
        var predicate2 = policy.CompileVMPredicate<TEntity>();

        var result = predicate(entity);
        var result2 = predicate2(entity);

        Debug.Assert(result == result2);
        return result && result2;
    }
}