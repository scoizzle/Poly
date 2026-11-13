using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

using Syntactic = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

// StageTransition with the inline entry/exit helpers.
public sealed partial class EffectLoweringPass {
    /// <summary>
    /// Lowers a stage transition to generic Syntax AST on both runtime and emit:
    /// source-stage exit effects (when known; once — no self-re-entry), CurrentStage
    /// assignment, target-stage entry effects (once), then
    /// <c>Notify{Target}Subscribers(previousStageN)</c> when the target is a watched
    /// stage (see <see cref="LoweringContext.PreviousStageNotifyStages"/>).
    /// The exit runs before the cursor moves, so each hop exits its own source.
    /// <see cref="_sourceStageName"/> is then advanced to the target <em>before</em> the
    /// entry effects inline and is never overwritten afterwards, so a nested entry chain
    /// ends on its innermost target. A guarded hop advances the cursor only within its own
    /// branch (the else branch restarts from the entry stage). The cursor is null — the
    /// per-stage exit switch — in and after a <c>for</c> whose body holds a hop or a
    /// self-<c>invoke</c>, after a branch holding a hop or a self-<c>invoke</c>, and after
    /// every self-<c>invoke</c>.
    /// Does not emit instance <c>Notify</c> — that method exists only on the
    /// simulator heap object; printed classes do not have one. Emits
    /// <c>NoteAutomaticStage</c> before the assign so simulate and printed C#
    /// share a loop-guard backstop.
    /// Captures <c>CurrentStage</c> into a unique local before the assign so nested
    /// OnEntry transitions do not collide (CS0136) and <c>when all</c> sees the
    /// outer pre-stage. Not a host-ABI node.
    /// </summary>
    protected override Node? StageTransition(StageTransitionEffect t) {
        if (!_entity.Stages.Any(s =>
            string.Equals(s.Name, t.TargetStage.StageName, StringComparison.Ordinal)))
            throw new InvalidOperationException(
                $"Stage '{t.TargetStage.StageName}' does not exist on entity '{_entity.Name}'.");

        // A hop inside this hop's own exit block to the same target is redundant: the
        // enclosing hop owns the stage assign, the target's entry effects and the notify,
        // once each. DMEFF017 makes this the only reachable shape; a differing target can
        // occur only in a dead arm (a stage-when handler lowers with a null cursor), so it
        // falls through and lowers as before. Return before touching the cursor.
        if (_inSourceExitInline
            && string.Equals(_enclosingHopTarget, t.TargetStage.StageName, StringComparison.Ordinal))
            return new Block(Array.Empty<Node>());

        var nodes = new List<Node>();
        Variable? previousStage = null;
        var notifyTarget = _postTransitionNotifyStages is not null
            && _postTransitionNotifyStages.Contains(t.TargetStage.StageName);
        var needsPrevious = notifyTarget
            && _previousStageNotifyStages is not null
            && _previousStageNotifyStages.Contains(t.TargetStage.StageName);
        if (needsPrevious) {
            previousStage = _names.Next("previousStage");
            nodes.Add(new Assignment(
                previousStage,
                new Member(Subject, "CurrentStage")));
        }

        // Exit/entry effects are best-effort at lowering time: with analysis present a
        // TryGetStage miss implies analysis/domain disagreement (both derive from the
        // same entity.Stages), so skipping is safe; InvokeActionInternal's fail-closed
        // throw covers the dispatch contract.

        // The exit inline runs with the enclosing-hop flags set (restored in finally, so a
        // throw cannot leak them and a fall-through hop re-points them at its own target).
        // Dead-arm corner: a fall-through hop's own entry inline runs after the restore,
        // with the outer flags, so an entry hop to the outer target is suppressed there; the
        // arm is dead code behind the handler's stage gate.
        var savedInline = _inSourceExitInline;
        var savedTarget = _enclosingHopTarget;
        _inSourceExitInline = true;
        _enclosingHopTarget = t.TargetStage.StageName;
        try {
            // Include exit effects from the source stage (if known).
            // TryGetStage only (no entity.Stages rescan).
            if (_sourceStageName is not null) {
                _analysis.TryGetStage(_entity, _sourceStageName, out var sourceStage);
                if (sourceStage is not null)
                    InlineExitOnce(sourceStage, nodes);
            }
            else {
                // Entity-level transition: source stage is CurrentStage at run time.
                // Emit per-stage OnExit so simulate and C# both run exit before assign.
                // An else-if chain: at most one stage's exit runs per hop, even when an
                // arm's exit holds a hop that moves CurrentStage.
                var arms = new List<(Stage Stage, List<Node> Body)>();
                foreach (var stage in _entity.Stages) {
                    if (stage.OnExitEffects.Count == 0)
                        continue;
                    var exitNodes = new List<Node>();
                    InlineExitOnce(stage, exitNodes);
                    if (exitNodes.Count == 0)
                        continue;
                    arms.Add((stage, exitNodes));
                }
                Node? chain = null;
                for (var i = arms.Count - 1; i >= 0; i--) {
                    var (stage, body) = arms[i];
                    Node stageMatch = new Member(
                        new NamedTypeReference(_stageEnumTypeName ?? $"{_entity.Name}Stage"),
                        stage.Name);
                    var test = new Equal(new Member(Subject, "CurrentStage"), stageMatch);
                    var branch = body.Count == 1 ? body[0] : new Block(body);
                    chain = chain is null ? new IfStatement(test, branch) : new IfStatement(test, branch, chain);
                }
                if (chain is not null)
                    nodes.Add(chain);
            }
        }
        finally {
            _inSourceExitInline = savedInline;
            _enclosingHopTarget = savedTarget;
        }

        // The hop's own source exit has run: the body is in the target from here on.
        // Set before the entry inline (not after) so a nested entry hop exits the
        // target, and do not re-set it afterwards so a chain ends on the innermost
        // target. Unconditional: a target with no entry effects still leaves the next
        // effect in this body exiting the target.
        _sourceStageName = t.TargetStage.StageName;

        // Entry effects run after the stage assign (a nested entry transition
        // must end at Y). Constraint probes are not emitted.
        var tryNodes = new List<Node>();
        _analysis.TryGetStage(_entity, t.TargetStage.StageName, out var targetStage);
        if (targetStage is not null)
            InlineEntryOnce(targetStage, tryNodes);

        // Loop-guard backstop shared with printed C#: each stage once per trigger.
        if (Subject is ThisReference && _emitAutomaticStageGuard) {
            nodes.Add(new Invoke(
                new Member(Subject, "NoteAutomaticStage"),
                new Constant(t.TargetStage.StageName)));
        }

        Node stageValue = new Member(
            new NamedTypeReference(_stageEnumTypeName ?? $"{_entity.Name}Stage"),
            t.TargetStage.StageName);
        nodes.Add(new Assignment(
            new Member(Subject, "CurrentStage"),
            stageValue));

        // G7: keep the lifecycle property in lockstep with CurrentStage. Emit
        // this.{LifecycleProperty} = {LifecycleEnum}.X after the stage assign and
        // before entry effects, unless the target stage's entry already assigns it
        // at the top level (Visit.CheckedIn entry { assign State to CheckedIn }).
        if (_lifecyclePropertyName is not null && _lifecycleEnumTypeName is not null) {
            var entryAssignsLifecycle = targetStage is not null
                && targetStage.OnEntryEffects.OfType<AssignEffect>().Any(ae =>
                    ae.Target is PropertyAccess pa
                    && string.Equals(pa.Name, _lifecyclePropertyName, StringComparison.Ordinal));
            if (!entryAssignsLifecycle) {
                nodes.Add(new Assignment(
                    new Member(Subject, _lifecyclePropertyName),
                    new Member(new NamedTypeReference(_lifecycleEnumTypeName), t.TargetStage.StageName)));
            }
        }

        if (notifyTarget) {
            // The dispatcher returns the first handler failure; this hop returns it to its own caller.
            var notifyResult = _names.Next("notify");
            tryNodes.Add(new Block([
                new Assignment(notifyResult, new Invoke(
                    new Member(Subject, $"Notify{t.TargetStage.StageName}Subscribers"),
                    needsPrevious ? [previousStage!] : [])),
                new IfStatement(
                    new Syntactic.Not(new Member(notifyResult, "IsSuccess")),
                    new Block([ReturnCallerFailureFrom(notifyResult)]))
            ], [notifyResult]));
        }

        // Splice a lowered entry Block into this transition body (D13) while
        // keeping its locals on the returned block.
        var tryLocals = new List<Node>();
        var flatTry = new List<Node>();
        foreach (var tryNode in tryNodes)
            CollectNode(flatTry, tryLocals, tryNode);
        if (flatTry.Count > 0)
            nodes.AddRange(flatTry);

        var locals = new List<Node>();
        if (previousStage is not null)
            locals.Add(previousStage);
        locals.AddRange(tryLocals);
        if (locals.Count == 0 && nodes.Count == 1)
            return nodes[0];
        return new Block(nodes, locals);
    }
    /// <summary>Inline a stage's exit at most once per lowering (no self-re-entry).</summary>
    private void InlineExitOnce(Stage stage, List<Node> bodySink) {
        if (!_exitsInlining.Add(stage.Name))
            return;
        try {
            AppendInlinedStageEffects(stage.OnExitEffects, bodySink);
        }
        finally {
            _exitsInlining.Remove(stage.Name);
        }
    }

    /// <summary>Inline a stage's entry at most once per lowering (no self-re-entry).</summary>
    private void InlineEntryOnce(Stage stage, List<Node> bodySink) {
        if (!_entriesInlining.Add(stage.Name))
            return;
        try {
            AppendInlinedStageEffects(stage.OnEntryEffects, bodySink);
        }
        finally {
            _entriesInlining.Remove(stage.Name);
        }
    }
}