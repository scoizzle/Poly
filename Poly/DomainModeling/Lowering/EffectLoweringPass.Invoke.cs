using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

using Syntactic = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

// InvokeAction and ForEachInvoke with the invoke-result helpers.
public sealed partial class EffectLoweringPass {
    /// <summary>
    /// Self-invoke (no TargetRelationship) is handwritten IR like StageTransition:
    /// <c>Invoke(Member(Subject, actionName), args)</c> on both runtime and emit.
    /// Singular cross-entity invoke is <c>this.Rel.Action(args)</c> with a
    /// linked-target guard that returns caller <c>DomainResult.Failure</c> before deref
    /// (never a bare NRE). Kitchen dogfood: nested Failure must fail-fast
    /// (<c>if (!result.IsSuccess) return caller.Failure(error)</c>) so later effects
    /// do not run and C# export does not CS0029 across <c>DomainResult</c> /
    /// <c>DomainResult&lt;T&gt;</c>. OneToMany fan-out uses the for-each lowering.
    /// </summary>
    protected override Node? InvokeAction(InvokeActionEffect i) {
        var loweredArgs = i.ParameterBindings
            .Select(binding => _expressionPass.LowerExpression(binding.Expression, Subject))
            .ToList();
        var args = loweredArgs.Select(a => a.Value).ToList();

        // Singular cross-entity invoke (OneToOne): the runtime requires exactly one
        // outbound link (ResolveRelationshipTarget) and fails loud otherwise. Enforce the
        // invariant at the action boundary with a domain Failure BEFORE derefing the nav —
        // never a bare null-forgiving deref (which would crash with an NRE at runtime).
        if (i.TargetRelationship is not null && _loopBinders?.TryGetValue(i.TargetRelationship, out var binderVar) == true) {
            var binderResult = _names.Next("invoke");
            return LoweredExpression.Before(loweredArgs, new Block([
                new Assignment(binderResult, new Invoke(new Member(binderVar!, i.ActionName), [.. args])),
                new IfStatement(
                    new Poly.Ast.Nodes.Not(new Member(binderResult, "IsSuccess")),
                    new Block([ReturnCallerFailureFrom(binderResult)]))
            ], [binderResult]));
        }

        if (i.TargetRelationship is not null) {
            var navMember = new Member(Subject, DomainToCSharpExporter.ToPascalCase(i.TargetRelationship));
            var guard = new IfStatement(
                new Equal(navMember, new Constant(null!)),
                new Block([ReturnCallerFailure(new Constant(
                    $"'{_context.ActionName ?? i.ActionName}' requires a linked '{i.TargetRelationship}' on entity '{_entity.Name}'."))]));
            var resultVar = _names.Next("invoke");
            var invokeCall = new Invoke(new Member(navMember, i.ActionName), [.. args]);
            return LoweredExpression.Before(loweredArgs, new Block([
                guard,
                new Assignment(resultVar, invokeCall),
                new IfStatement(
                    new Poly.Ast.Nodes.Not(new Member(resultVar, "IsSuccess")),
                    new Block([ReturnCallerFailureFrom(resultVar)]))
            ], [resultVar]));
        }

        // A callee may transition — directly, or through its own invokes — and the
        // lowering has no transitive "callee can transition" fact, so the caller's
        // cursor goes dynamic rather than keep a source the callee may have left.
        _sourceStageName = null;

        return LoweredExpression.Before(loweredArgs,
            WrapInvokeResult(new Invoke(new Member(Subject, i.ActionName), [.. args])));
    }
    private Node WrapInvokeResult(Invoke invokeCall) {
        var resultVar = _names.Next("invoke");
        return new Block([
            new Assignment(resultVar, invokeCall),
            new IfStatement(
                new Poly.Ast.Nodes.Not(new Member(resultVar, "IsSuccess")),
                new Block([ReturnCallerFailureFrom(resultVar)]))
        ], [resultVar]);
    }

    /// <summary>
    /// Lowers a <see cref="ForEachInvokeEffect"/> (the <c>for Rel as x [where x.Policy |
    /// where x in Stage] invoke x.Action(args)</c> fan-out) to a fail-fast loop over the
    /// source's collection navigation — "fetch all from storage, invoke on every record".
    /// The first failing record fails the whole <c>for</c>. Zero records — an empty
    /// collection, or a predicate that matched none — is a no-op: the action continues
    /// with the effect after the loop. The predicate is a named policy (bool method) or
    /// stage membership (<c>x.CurrentStage == TargetStage.X</c>) on the target entity.
    /// </summary>
    protected override Node? ForEachInvoke(ForEachInvokeEffect e) {
        var relName = e.RelationshipName;
        var navMember = new Member(Subject, DomainToCSharpExporter.ToPascalCase(relName));

        var loopVar = _names.Next("target");

        // Predicate → a `continue` guard inside the loop (no LINQ dependency in the
        // standalone export): named policy → target's bool method; stage membership →
        // CurrentStage (string on runtime, enum on emit). Same loop Variable instance
        // so the VM can bind Current onto it.
        Node? predicateGuard = null;
        if (e.Predicate is not null) {
            Node predicateExpr = e.Predicate switch {
                ForEachNamedPolicy p => new Poly.Ast.Nodes.Not(
                    new Invoke(new Member(loopVar, p.PolicyName))),
                ForEachStageMembership s => new NotEqual(
                    new Member(loopVar, "CurrentStage"),
                    new Member(new NamedTypeReference(TargetStageEnumTypeName(relName)), s.StageName)),
                _ => throw new NotSupportedException($"Unsupported ForEachInvoke predicate '{e.Predicate.GetType().Name}'."),
            };
            predicateGuard = new IfStatement(
                predicateExpr,
                new Block([new ContinueStatement()]));
        }

        // Lower the invoke arguments with the binder mapped to the loop variable, so
        // `invoke x.Mark(amount: x Qty)` references the current record.
        var mergedParams = new Dictionary<string, Node>(StringComparer.Ordinal);
        if (_context.Parameters is not null)
            foreach (var kv in _context.Parameters)
                mergedParams[kv.Key] = kv.Value;
        mergedParams[e.BinderName] = loopVar;
        var argPass = new DomainExpressionLoweringPass(_context with { Parameters = mergedParams });
        var loweredArgs = e.ParameterBindings
            .Select(binding => argPass.LowerExpression(binding.Expression, Subject))
            .ToList();
        var args = loweredArgs.Select(a => a.Value).ToList();

        var invokeCall = new Invoke(new Member(loopVar, e.ActionName), [.. args]);

        // Fail-fast: the first failing record fails the whole `for` and returns the
        // caller's failure. Zero iterations (an empty collection, or a predicate that
        // matched none) is a no-op — the action continues to the effect after the loop.
        // Same Variable instances for VM identity. Arg statements sit inside the loop,
        // immediately before the invoke.
        var resultVar = _names.Next("result");
        var loopBody = new List<Node>();
        if (predicateGuard is not null) loopBody.Add(predicateGuard);
        loopBody.AddRange(loweredArgs.SelectMany(a => a.Statements));
        loopBody.Add(new Assignment(resultVar, invokeCall));
        loopBody.Add(new IfStatement(new Poly.Ast.Nodes.Not(new Member(resultVar, "IsSuccess")),
            new Block([ReturnCallerFailureFrom(resultVar)])));
        var loopLocals = new List<Node> { resultVar };
        loopLocals.AddRange(loweredArgs.SelectMany(a => a.Variables));
        var loop = new ForEachLoop(loopVar, navMember, new Block(loopBody, loopLocals));
        return new Block([loop]);
    }

    /// <summary>Resolves the stage enum CLR name for a target entity of a relationship
    /// (e.g. <c>LineStage</c>), from analysis, else the default convention.</summary>
    private string TargetStageEnumTypeName(string relationshipName) {
        var targetName = _entity.Navigations.FirstOrDefault(n =>
            string.Equals(n.Name, relationshipName, StringComparison.Ordinal))?.Target.TypeName;
        if (targetName is not null) {
            var targetEntity = _domain.Types.OfType<Entity>()
                .FirstOrDefault(t => string.Equals(t.Name, targetName, StringComparison.Ordinal));
            if (targetEntity is not null) {
                var esm = _analysis.GetStructure(targetEntity);
                if (esm is { HasStages: false })
                    throw new InvalidOperationException(
                        $"Entity '{targetEntity.Name}' has only an implicit stage; " +
                        "a stage membership filter needs an explicit stage.");
                if (esm?.StageEnumTypeName is { } custom) return custom;
                if (EntityStructureAnalyzer.IsImplicitStage(targetEntity, _domain))
                    throw new InvalidOperationException(
                        $"Entity '{targetEntity.Name}' has only an implicit stage; " +
                        "a stage membership filter needs an explicit stage.");
                if (targetEntity.Stages.Count > 0)
                    return $"{targetName}Stage";
            }
        }
        return $"{targetName ?? relationshipName}Stage";
    }
}