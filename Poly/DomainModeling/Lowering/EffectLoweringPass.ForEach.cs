using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

using Syntactic = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

// ForEachEffect.
public sealed partial class EffectLoweringPass {
    /// <summary>
    /// Lowers the general <c>for x in Rel [where cond] { effects }</c> (#409) to a fail-fast
    /// <c>foreach</c> over the source's collection navigation, the same loop as the fan-out.
    /// A <c>where</c> that is false skips the record (<c>continue</c>). The body runs once
    /// per remaining record, in storage order; an <c>invoke binder.Action</c> in it targets
    /// the loop variable and returns the caller's failure on the first failing record.
    /// Zero records — an empty collection or a <c>where</c> that matched none — is a no-op.
    /// </summary>
    protected override Node? ForEach(ForEachEffect e) {
        var navMember = new Member(Subject, DomainToCSharpExporter.ToPascalCase(e.RelationshipName));
        var loopVar = _names.Next("target");

        var mergedParams = new Dictionary<string, Node>(StringComparer.Ordinal);
        if (_context.Parameters is not null)
            foreach (var kv in _context.Parameters)
                mergedParams[kv.Key] = kv.Value;
        mergedParams[e.BinderName] = loopVar;
        // The body's source is whatever stage the runtime instance is in at that
        // iteration, not the stage that declared the loop: seed the dynamic (null)
        // cursor, the per-stage exit switch.
        var bodyContext = _context with { Parameters = mergedParams, SourceStageName = null };

        var binders = new Dictionary<string, Node>(_loopBinders ?? new Dictionary<string, Node>(), StringComparer.Ordinal) {
            [e.BinderName] = loopVar
        };
        var bodyPass = new EffectLoweringPass(_entity, bodyContext) { _loopBinders = binders };

        var loopBody = new List<Node>();
        var loopLocals = new List<Node>();
        if (e.Where is not null) {
            var where = new DomainExpressionLoweringPass(bodyContext).LowerExpression(e.Where, Subject);
            loopBody.AddRange(where.Statements);
            loopLocals.AddRange(where.Variables);
            loopBody.Add(new IfStatement(
                new Poly.Ast.Nodes.Not(where.Value),
                new Block([new ContinueStatement()])));
        }
        foreach (var sub in e.Body) {
            var lowered = bodyPass.Route(sub)
                ?? throw new InvalidOperationException(
                    $"Cannot lower effect '{DescribeEffect(sub)}' to a Syntax AST node.");
            CollectNode(loopBody, loopLocals, lowered);
        }

        // A body that can change stage (a hop, or a self-invoke whose callee hops) leaves the
        // runtime stage unknown once the loop ends: the body's own cursor is a separate pass,
        // so the enclosing cursor is dynamic for the rest of this body too.
        ClearCursorIf(CanChangeStage(e.Body));

        return new Block([new ForEachLoop(loopVar, navMember, new Block(loopBody, loopLocals))]);
    }
}