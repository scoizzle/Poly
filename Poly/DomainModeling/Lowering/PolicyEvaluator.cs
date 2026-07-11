using Poly.Interpretation;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.LinqExpressions;
using Poly.Interpretation.Vm;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Lowers a <see cref="Policy"/>'s <see cref="DomainExpression"/> guard
/// to an executable predicate and evaluates it against an entity instance.
///
/// <para><b>VM-primary design:</b> The <see cref="Evaluate{TEntity}"/> and
/// <see cref="CompileVMPredicate{TEntity}"/> methods use the VM (direct AST
/// lowering) as the canonical evaluation path. The LINQ compile path
/// (<see cref="CompileLinqPredicate{TEntity}"/>) is retained as a secondary
/// reference for divergence detection — use <see cref="EvaluateWithDualOracle{TEntity}"/>
/// in tests that explicitly cross-check the two engines.</para>
/// </summary>
public static class PolicyEvaluator {
    private static readonly Analyzer LinqAnalyzer = new AnalyzerBuilder()
        .UseThisReferenceContext()
        .UseTypeAndMemberResolver()
        .UseVariableScopeValidator()
        .UseSideEffectAnalysis()
        .UseJumpTargetResolution()
        .UseControlFlowAnalysis()
        .UseDefiniteAssignmentAnalysis()
        .Build();

    /// <summary>
    /// Compiles <paramref name="policy"/> into a <c>Func&lt;TEntity, bool&gt;</c>
    /// using the <b>VM</b> (direct AST lowering). This is the primary product path.
    /// </summary>
    public static Func<TEntity, bool> CompileVMPredicate<TEntity>(this Policy policy) {
        ArgumentNullException.ThrowIfNull(policy);

        var entityParam = new Parameter("entity", TypeReference.To<TEntity>());
        var pass = new DomainExpressionLoweringPass();
        var lowered = pass.Lower(policy.Expression, entityParam);

        // Standard VM interpretation pipeline
        var compiled = Interpreter.Compile(lowered);

        return e => {
            using var exec = Interpreter.Execute(compiled, s => s.SetArgs(new object?[] { e }));
            return exec.Result.GetValue<bool>();
        };
    }

    /// <summary>
    /// Evaluates <paramref name="policy"/> against <paramref name="entity"/>
    /// on the <b>VM</b> (direct AST lowering — canonical path).
    /// Returns <c>true</c> if the policy's guard expression is satisfied.
    /// </summary>
    public static bool Evaluate<TEntity>(this Policy policy, TEntity entity) {
        var predicate = policy.CompileVMPredicate<TEntity>();
        return predicate(entity);
    }

    /// <summary>
    /// Compiles <paramref name="policy"/> into a <c>Func&lt;TEntity, bool&gt;</c>
    /// using <c>LinqExpressionGenerator</c>. Retained as secondary reference.
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
    /// Evaluates <paramref name="policy"/> using <b>both</b> LINQ and VM paths
    /// and throws if they diverge. Use only in tests that explicitly cross-check
    /// engine agreement. For product code, prefer <see cref="Evaluate{TEntity}"/>
    /// (VM-primary).
    /// </summary>
    public static bool EvaluateWithDualOracle<TEntity>(this Policy policy, TEntity entity) {
        var linqResult = policy.CompileLinqPredicate<TEntity>()(entity);
        var vmResult = policy.CompileVMPredicate<TEntity>()(entity);

        if (linqResult != vmResult)
            throw new InvalidOperationException(
                $"Policy evaluation mismatch: LINQ returned {linqResult}, VM returned {vmResult}. " +
                $"This indicates a divergence between the two execution engines for policy '{policy.Name}'.");
        return vmResult;
    }
}