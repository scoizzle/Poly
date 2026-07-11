using Poly.Interpretation;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.LinqExpressions;
using Poly.Interpretation.Vm;
using Poly.Syntax;
using Poly.Syntax.Analysis;

using SN = Poly.Syntax.Nodes;

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
///
/// <para><b>Subjects</b> must be types with real CLR properties (records, POCOs).
/// For test subject factories and ad-hoc property bags, see
/// <c>Poly.Tests.TestHelpers.PolicyTestSubjects</c>.</para>
/// </summary>
public static class PolicyEvaluator {

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

        var analysis = Interpreter.Analyze(lowered);

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

    // ── Domain-validated property path ─────────────────────────────
    //
    // These overloads validate that property references in the policy
    // expression exist on the domain entity definition before lowering
    // to the CLR-bound pipeline. This is the simplest proof that the
    // domain model informs evaluation.

    /// <summary>
    /// Returns the set of property names referenced by <paramref name="expression"/>
    /// via <c>PropertyAccess</c> nodes. Used by the entity-validated overloads
    /// to check that all references correspond to real entity properties.
    /// </summary>
    public static HashSet<string> GetReferencedProperties(DomainExpression expression) {
        ArgumentNullException.ThrowIfNull(expression);
        var result = new HashSet<string>();
        CollectPropertyAccesses(expression, result);
        return result;
    }

    /// <summary>
    /// Compiles <paramref name="policy"/> into a <c>Func&lt;TEntity, bool&gt;</c>
    /// using the <b>VM</b>, but first validates that all <c>PropertyAccess</c>
    /// references in the policy expression exist on the domain <paramref name="entity"/>.
    /// Throws <see cref="ArgumentException"/> if a referenced property is not found.
    /// </summary>
    public static Func<TEntity, bool> CompileVMPredicate<TEntity>(this Policy policy, Entity entity) {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(entity);

        var referenced = GetReferencedProperties(policy.Expression);
        var entityProps = new HashSet<string>(entity.Properties.Select(p => p.Name));

        foreach (var propName in referenced) {
            if (!entityProps.Contains(propName))
                throw new ArgumentException(
                    $"Policy '{policy.Name}' references property '{propName}' " +
                    $"which is not defined on entity '{entity.Name}'.");
        }

        // Proceed with standard VM compilation
        return policy.CompileVMPredicate<TEntity>();
    }

    /// <summary>
    /// Evaluates <paramref name="policy"/> against <paramref name="subject"/>,
    /// first validating that all property references exist on the domain
    /// <paramref name="entity"/>. See <see cref="CompileVMPredicate{TEntity}(Policy, Entity)"/>.
    /// </summary>
    public static bool Evaluate<TEntity>(this Policy policy, TEntity subject, Entity entity) {
        var predicate = policy.CompileVMPredicate<TEntity>(entity);
        return predicate(subject);
    }

    private static void CollectPropertyAccesses(DomainExpression expr, HashSet<string> result) {
        switch (expr) {
            case PropertyAccess pa:
                result.Add(pa.Name);
                break;
            case OwnedAccess oa:
                CollectPropertyAccesses(oa.Inner, result);
                break;
            case Exists e:
                CollectPropertyAccesses(e.Target, result);
                break;
            case NotExists ne:
                CollectPropertyAccesses(ne.Target, result);
                break;
            case Subtract s:
                CollectPropertyAccesses(s.Left, result);
                CollectPropertyAccesses(s.Right, result);
                break;
            case Add a:
                CollectPropertyAccesses(a.Left, result);
                CollectPropertyAccesses(a.Right, result);
                break;
            case Multiply m:
                CollectPropertyAccesses(m.Left, result);
                CollectPropertyAccesses(m.Right, result);
                break;
            case Divide d:
                CollectPropertyAccesses(d.Left, result);
                CollectPropertyAccesses(d.Right, result);
                break;
            case DateOperation dateOp:
                CollectPropertyAccesses(dateOp.Date, result);
                CollectPropertyAccesses(dateOp.Offset, result);
                break;
            case RelationshipNavigation rn:
                CollectPropertyAccesses(rn.TargetProperty, result);
                break;
            case Comparison comp:
                CollectPropertyAccesses(comp.Left, result);
                CollectPropertyAccesses(comp.Right, result);
                break;
            case And and:
                CollectPropertyAccesses(and.Left, result);
                CollectPropertyAccesses(and.Right, result);
                break;
            case Or or:
                CollectPropertyAccesses(or.Left, result);
                CollectPropertyAccesses(or.Right, result);
                break;
            case Not not:
                CollectPropertyAccesses(not.Operand, result);
                break;
                // Literal, ParameterAccess — no property references
        }
    }
}