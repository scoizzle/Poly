using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

using Syntactic = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Lowers domain <see cref="Effect"/> types to Syntax AST nodes for VM
/// execution via <see cref="Interpreter.Compile"/>. Composes with
/// <see cref="DomainExpressionLoweringPass"/> for expression-heavy effects
/// like <see cref="AssignEffect"/> and <see cref="ConditionalEffect"/>.
///
/// <para>Create / create-in / unique lower to Store jobs
/// (<c>Create</c> / <c>CreateIn</c> / <c>ProbeCreate</c> / <c>EnsureUnique</c>)
/// on the one operation tree. C# print of those jobs is ordinary methods;
/// generated <c>Create</c> factories may still call <c>Stay.Create</c> as the
/// host bind of the job. Persistence unique indexes stay a host-artifact concern.
/// Mixed if+create is the same guarded-probe + body tree.
/// StageTransition and invoke are handwritten IR on both paths.</para>
///
/// <para>When <see cref="Analysis"/> is set, lowering reads pre-computed
/// <see cref="IAnalysisMetadata"/> instead of re-scanning domain collections.
/// Null-safe — falls back to re-scan when absent. The emitted tree is generic
/// Syntax (Store calls, Assignment, …); it does not carry bag types.</para>
/// </summary>
public sealed class EffectLoweringPass : EffectDispatch<Node?> {
    private readonly Entity _entity;
    private readonly Domain? _domain;
    private readonly DomainExpressionLoweringPass _expressionPass;
    private readonly INodeMetadataProvider? _analysis;
    private readonly bool _useThisReference;
    private readonly string? _stageEnumTypeName;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<Node>>? _postTransitionNodes;
    private string? _sourceStageName;
    private readonly IReadOnlyDictionary<string, string>? _enumPropertyNames;
    private readonly LoweringContext _context;
    private readonly bool _emitInstanceNotify;
    private int _forEachInvokeSequence;
    private int _createInProbeSequence;

    /// <summary>Pre-computed analysis metadata provider, when available.</summary>
    public INodeMetadataProvider? Analysis => _analysis;

    public EffectLoweringPass(Entity entity, Node subject)
        : this(entity, new LoweringContext(subject)) { }

    public EffectLoweringPass(Entity entity, LoweringContext context) {
        _entity = entity;
        _context = context;
        _domain = context.Domain;
        _analysis = context.Analysis;
        _useThisReference = context.UseThisReference;
        _stageEnumTypeName = context.StageEnumTypeName;
        _postTransitionNodes = context.PostTransitionNodes;
        _sourceStageName = context.SourceStageName;
        _enumPropertyNames = context.EnumPropertyNames;
        _emitInstanceNotify = context.EmitInstanceNotify;
        IReadOnlyDictionary<string, Node>? parameters = context.Parameters;
        if (context.ActionParameterNames is { Count: > 0 }) {
            var merged = parameters is null
                ? new Dictionary<string, Node>(StringComparer.Ordinal)
                : new Dictionary<string, Node>(parameters, StringComparer.Ordinal);
            foreach (var name in context.ActionParameterNames) {
                if (!merged.ContainsKey(name))
                    merged[name] = context.UseThisReference
                        ? new Parameter(name)
                        : new Member(context.Subject, name);
            }
            parameters = merged;
        }
        _expressionPass = new DomainExpressionLoweringPass(context with {
            Parameters = parameters,
            NavigationNameResolver = context.NavigationNameResolver ?? BuildNavigationNameResolver(entity, _domain, _analysis),
            IsCollectionNavigation = context.IsCollectionNavigation
                ?? BuildIsCollectionNavigation(entity, _domain, _analysis),
            IsRelationshipNavigation = context.IsRelationshipNavigation
                ?? BuildIsRelationshipNavigation(entity, _domain, _analysis),
            PropertyTypeResolver = context.PropertyTypeResolver ?? BuildPropertyTypeResolver(entity),
            SourceEntityName = context.SourceEntityName ?? entity.Name
        });
        Subject = context.UseThisReference && context.Subject is Parameter { Name: "entity" }
            ? new ThisReference()
            : context.Subject;
    }

    /// <summary>
    /// Builds the default DSL-nav-name → generated-member-name resolver for the
    /// entity: relationship navigation names (source-side) pascal-case to the
    /// exporter's property naming (<c>compilations</c> → <c>Compilations</c>);
    /// plain properties keep their DSL name. Analysis metadata is primary;
    /// falls back to the domain relationship list. Identity when no context.
    /// </summary>
    internal static Func<string, string> BuildNavigationNameResolver(
        Entity entity, Domain? domain, INodeMetadataProvider? analysis) {
        if (analysis is not null) {
            var rlm = domain is not null
                ? analysis.GetRelationshipLookup(domain)
                : analysis.GetRelationshipLookup();
            if (rlm is not null) {
                return name => rlm.TryGetRelationship(entity.Name, name, out var rel)
                        ? DomainToCSharpExporter.ToPascalCase(name)
                        : name;
            }
        }
        if (domain is not null) {
            var sourceNavs = entity.Navigations
                .Select(r => r.Name)
                .ToHashSet(StringComparer.Ordinal);
            return name => sourceNavs.Contains(name)
                ? DomainToCSharpExporter.ToPascalCase(name)
                : name;
        }
        return name => name;
    }

    /// <summary>
    /// Builds the default "is this DSL nav name a collection relationship on the
    /// current entity" predicate, mirroring <see cref="BuildNavigationNameResolver"/>
    /// (analysis metadata primary; domain relationship list fallback; false when
    /// unknown). Used to lower <c>Rel exists</c> on <c>many</c> navs to a
    /// <c>.Count != 0</c> check in the export.
    /// </summary>
    /// <summary>
    /// True when <paramref name="name"/> is an outbound relationship on the
    /// current entity (any cardinality). Runtime <c>Rel exists</c> uses
    /// <c>ExistsRelated</c>; property <c>Name exists</c> stays a null check.
    /// </summary>
    internal static Func<string, bool> BuildIsRelationshipNavigation(
        Entity entity, Domain? domain, INodeMetadataProvider? analysis) {
        if (analysis is not null) {
            var rlm = domain is not null
                ? analysis.GetRelationshipLookup(domain)
                : analysis.GetRelationshipLookup();
            if (rlm is not null) {
                return name => rlm.TryGetRelationship(entity.Name, name, out _);
            }
        }
        if (domain is not null) {
            var names = entity.Navigations
                .Select(r => r.Name)
                .ToHashSet(StringComparer.Ordinal);
            return name => names.Contains(name);
        }
        return _ => false;
    }

    internal static Func<string, bool> BuildIsCollectionNavigation(
        Entity entity, Domain? domain, INodeMetadataProvider? analysis) {
        if (analysis is not null) {
            var rlm = domain is not null
                ? analysis.GetRelationshipLookup(domain)
                : analysis.GetRelationshipLookup();
            if (rlm is not null) {
                return name => rlm.TryGetRelationship(entity.Name, name, out var rel)
                    && rel.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany;
            }
        }
        if (domain is not null) {
            var navCardinalities = entity.Navigations
                .GroupBy(r => r.Name)
                .ToDictionary(g => g.Key, g => g.First().Cardinality, StringComparer.Ordinal);
            return name => navCardinalities.TryGetValue(name, out var card)
                && card is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany;
        }
        return _ => false;
    }

    /// <summary>
    /// Builds the default property-name → domain-type-name resolver for date-arithmetic
    /// lowering (entity.Properties primary; null when unknown).
    /// </summary>
    internal static Func<string, string?> BuildPropertyTypeResolver(Entity entity) {
        var byName = entity.Properties.ToDictionary(
            p => p.Name, p => p.Type.TypeName, StringComparer.Ordinal);
        foreach (var action in entity.Actions)
            foreach (var p in action.Parameters)
                byName.TryAdd(p.Name, p.Type.TypeName);
        foreach (var stage in entity.Stages)
            foreach (var action in stage.Actions)
                foreach (var p in action.Parameters)
                    byName.TryAdd(p.Name, p.Type.TypeName);
        return name => byName.TryGetValue(name, out var typeName) ? typeName : null;
    }

    /// <summary>The Syntax AST node representing the current entity instance.</summary>
    public Node Subject { get; }

    /// <summary>
    /// Lowers <paramref name="effect"/> to a Syntax AST node suitable for VM
    /// compilation, or returns <c>null</c> when the effect must be executed
    /// directly on a <see cref="DomainEntityInstance"/>.
    /// </summary>
    public Node? TryLowerVmNode(Effect effect) => Route(effect);

    protected override Node? Default() => null;

    protected override Node? Assign(AssignEffect a) {
        var target = _expressionPass.Lower(a.Target, Subject);
        var value = _expressionPass.Lower(a.Value, Subject);

        // Convert enum-valued RHS to qualified enum member access when the
        // target property is enum-typed:  assign Status to "Suspended"
        // on PatronStatus-typed property →  this.Status = PatronStatus.Suspended;
        // and a bare identifier member (assign Status to Suspended) → same.
        // Only in C# export mode (EnumPropertyNames present) — the generated C#
        // must reference the enum type. On the runtime path enum values are stored
        // as strings, so a bare enum member identifier lowers to its name string
        // and a string literal is already a string constant (no qualification).
        if (a.Target is PropertyAccess propAccess) {
            var entityProp = _entity.Properties.FirstOrDefault(p =>
                string.Equals(p.Name, propAccess.Name, StringComparison.Ordinal));

            // Runtime keywords in an assign RHS (assign DueDate to now / today /
            // guid) must adapt to the TARGET property's CLR type — the raw keyword
            // shape (DateTime.UtcNow / DateOnly / Guid) would otherwise be
            // cross-typed (CS0029/CS0019 in the export, wrong-typed stores at runtime).
            // Discovery round5 F2/F3.
            if (entityProp is not null
                && a.Value is PropertyAccess keywordAccess
                && keywordAccess.Name is "Now" or "UtcNow" or "Today" or "Guid") {
                var adapted = LowerDefaultExpression(
                    keywordAccess, new NamedTypeReference(entityProp.Type.TypeName));
                if (adapted is not null) value = adapted;
            }

            if (entityProp is not null
                && DomainToCSharpExporter.TryResolveEnumType(_domain, _analysis, entityProp.Type.TypeName, out var enumType)
                && enumType is not null) {
                if (_enumPropertyNames is not null) {
                    if (a.Value is Literal { Value: string strVal }
                        && !string.IsNullOrEmpty(strVal)) {
                        value = new Member(new NamedTypeReference(enumType.Name), strVal);
                    }
                    else if (a.Value is PropertyAccess pa
                        && enumType.MemberNames.Contains(pa.Name, StringComparer.Ordinal)) {
                        value = new Member(new NamedTypeReference(enumType.Name), pa.Name);
                    }
                }
                else if (a.Value is PropertyAccess pa
                    && enumType.MemberNames.Contains(pa.Name, StringComparer.Ordinal)) {
                    value = new Constant(pa.Name);
                }
            }

            // Date arithmetic (DueDate + 14 → AddDays) is lowered by the expression
            // pass (Add/Subtract) so it applies in every context — nothing more here.
        }

        if (_analysis?.GetMetadata<AssignedMemberConversionMetadata>(a) is { } conversion) {
            value = new Invoke(
                new Member(value, conversion.MethodName),
                [.. conversion.Arguments.Select(arg =>
                    new Member(new NamedTypeReference(arg.TypeName), arg.MemberName))]);
        }

        var assignment = new Assignment(target, value);
        if (a.Target is PropertyAccess uniqueTarget
            && IsUniqueProperty(uniqueTarget.Name)) {
            return WrapUniqueAssign(assignment.Destination, uniqueTarget.Name, value);
        }
        return assignment;
    }

    private bool IsUniqueProperty(string propertyName) {
        if (_analysis is not null && _domain is not null) {
            var storage = _analysis.GetMetadata<StorageMappingMetadata>(_domain);
            if (storage is not null) {
                var mapped = storage.Storage.Entities.FirstOrDefault(e =>
                    string.Equals(e.Name, _entity.Name, StringComparison.Ordinal));
                return mapped?.Columns.Any(c =>
                    string.Equals(c.Name, propertyName, StringComparison.Ordinal) && c.IsUnique) == true;
            }
        }
        return _entity.Properties.Any(p =>
            string.Equals(p.Name, propertyName, StringComparison.Ordinal)
            && p.Constraints.OfType<UniqueConstraint>().Any());
    }

    private Node WrapUniqueAssign(Node destination, string propertyName, Node value) {
        var seq = _forEachInvokeSequence++;
        var assignedVar = new Variable($"uniqueValue{seq}");
        var checkVar = new Variable($"uniqueCheck{seq}");
        return new Block([
            new Assignment(assignedVar, value),
            new Assignment(checkVar, new Invoke(
                new Member(Subject, "EnsureUnique"),
                new Constant(propertyName),
                assignedVar)),
            new IfStatement(
                new Syntactic.Not(new Member(checkVar, "IsSuccess")),
                new Block([ReturnCallerFailureFrom(checkVar)])),
            new Assignment(destination, assignedVar)
        ], [assignedVar, checkVar]);
    }

    private Node? RouteWithRuntimeCreate(Effect effect) =>
        Route(effect);

    /// <summary>
    /// Mixed if+create in OnEntry/OnExit uses the same guarded-probe walk as
    /// <see cref="LowerActionBodyCore"/>. Probes go to <paramref name="probeSink"/>
    /// (before CurrentStage for entry); the body goes to <paramref name="bodySink"/>.
    /// </summary>
    private void AppendInlinedStageEffects(
        IReadOnlyList<Effect> effects, List<Node> probeSink, List<Node> bodySink) {
        if (effects.Count == 0)
            return;
        // CurrentStage is a prior mutation for inlined entry: probe if-only and
        // unconditional create before the stage flip (runtime and C# export).
        if (HasCreate(effects)) {
            var probes = LowerCreateInConstraintProbes(effects, priorMutation: true);
            if (probes.Count > 0)
                probeSink.Add(FlattenProbeBlocks(probes));
            var body = TryLowerVmNode(new CompositeEffect(effects));
            if (body is not null)
                bodySink.Add(body);
            return;
        }
        foreach (var effect in effects) {
            var lowered = RouteWithRuntimeCreate(effect);
            if (lowered is not null)
                bodySink.Add(lowered);
        }
    }

    private static bool HasCreate(IReadOnlyList<Effect> effects) =>
        effects.Any(ContainsCreateEffect);

    private static bool ContainsCreateEffect(Effect effect) => effect switch {
        CreateEntityInstance cei => true,
        CreateEntityInRelationshipEffect => true,
        CompositeEffect c => c.Effects.Any(ContainsCreateEffect),
        ConditionalEffect c => c.ThenEffects.Any(ContainsCreateEffect)
            || (c.ElseEffects?.Any(ContainsCreateEffect) ?? false),
        _ => false
    };

    /// <summary>
    /// Lowers a stage transition to generic Syntax AST on both runtime and emit:
    /// source-stage exit effects (when known), CurrentStage assignment, target-stage
    /// entry effects (in try), post-transition notification nodes, then
    /// <c>Invoke(Member(Subject, "Notify"), stageName)</c> in finally.
    /// Not a host-ABI node.
    /// </summary>
    protected override Node? StageTransition(StageTransitionEffect t) {
        if (!_entity.Stages.Any(s =>
            string.Equals(s.Name, t.TargetStage.StageName, StringComparison.Ordinal)))
            return new Block([]);

        var nodes = new List<Node>();

        // Exit/entry effects are best-effort at lowering time: with analysis present a
        // TryGetStage miss implies analysis/domain disagreement (both derive from the
        // same entity.Stages), so skipping is safe; InvokeActionInternal's fail-closed
        // throw covers the dispatch contract.

        // Include exit effects from the source stage (if known).
        // Analysis-present: TryGetStage only (no entity.Stages rescan).
        // Null analysis: structural stage list for non-product/test callers only.
        if (_sourceStageName is not null) {
            Stage? sourceStage = null;
            if (_analysis is not null)
                _analysis.TryGetStage(_entity, _sourceStageName, out sourceStage);
            else
                sourceStage = _entity.Stages.FirstOrDefault(s =>
                    string.Equals(s.Name, _sourceStageName, StringComparison.Ordinal));
            if (sourceStage is not null)
                AppendInlinedStageEffects(sourceStage.OnExitEffects, nodes, nodes);
        }

        // Entry probes run BEFORE CurrentStage so a taken illegal create fails
        // closed without flipping the stage. Entry body still runs after the
        // assign (nested entry transition must end at Y).
        var tryNodes = new List<Node>();
        Stage? targetStage = null;
        if (_analysis is not null)
            _analysis.TryGetStage(_entity, t.TargetStage.StageName, out targetStage);
        else
            targetStage = _entity.Stages.FirstOrDefault(s =>
                string.Equals(s.Name, t.TargetStage.StageName, StringComparison.Ordinal));
        var entryProbes = new List<Node>();
        if (targetStage is not null)
            AppendInlinedStageEffects(targetStage.OnEntryEffects, entryProbes, tryNodes);
        nodes.AddRange(entryProbes);

        Node stageValue = _useThisReference || _stageEnumTypeName is not null
            ? new Member(
                new NamedTypeReference(_stageEnumTypeName ?? $"{_entity.Name}Stage"),
                t.TargetStage.StageName)
            : new Constant(t.TargetStage.StageName);
        nodes.Add(new Assignment(
            new Member(Subject, "CurrentStage"),
            stageValue));

        if (_postTransitionNodes is not null
            && _postTransitionNodes.TryGetValue(t.TargetStage.StageName, out var postNodes)) {
            foreach (var postNode in postNodes)
                tryNodes.Add(postNode);
        }

        Node tryBody = tryNodes.Count switch {
            0 => new Block([]),
            1 => tryNodes[0],
            _ => new Block(tryNodes)
        };
        if (_emitInstanceNotify) {
            nodes.Add(new TryCatchFinally(
                tryBody,
                CatchClauses: null,
                FinallyBlock: new Invoke(
                    new Member(Subject, "Notify"),
                    new Constant(t.TargetStage.StageName))));
        }
        else if (tryNodes.Count > 0) {
            nodes.Add(tryBody);
        }

        _sourceStageName = t.TargetStage.StageName;

        return nodes.Count == 1 ? nodes[0] : new Block(nodes);
    }

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
        var args = new List<Node>();
        foreach (var binding in i.ParameterBindings) {
            args.Add(_expressionPass.Lower(binding.Expression, Subject));
        }

        // Singular cross-entity invoke (OneToOne): the runtime requires exactly one
        // outbound link (ResolveRelationshipTarget) and fails loud otherwise. Enforce the
        // invariant at the action boundary with a domain Failure BEFORE derefing the nav —
        // never a bare null-forgiving deref (which would crash with an NRE at runtime).
        if (i.TargetRelationship is not null) {
            var navMember = new Member(Subject, DomainToCSharpExporter.ToPascalCase(i.TargetRelationship));
            var guard = new IfStatement(
                new Equal(navMember, new Constant(null!)),
                new Block([ReturnCallerFailure(new Constant(
                    $"'{i.ActionName}' requires a linked '{i.TargetRelationship}' on entity '{_entity.Name}'."))]));
            var seq = _forEachInvokeSequence++;
            var resultVar = new Variable($"invoke{seq}");
            var invokeCall = new Invoke(new Member(navMember, i.ActionName), [.. args]);
            return new Block([
                guard,
                new Assignment(resultVar, invokeCall),
                new IfStatement(
                    new Poly.Ast.Nodes.Not(new Member(resultVar, "IsSuccess")),
                    new Block([ReturnCallerFailureFrom(resultVar)]))
            ], [resultVar]);
        }

        return WrapInvokeResult(new Invoke(new Member(Subject, i.ActionName), [.. args]));
    }

    private Node WrapInvokeResult(Invoke invokeCall) {
        var seq = _forEachInvokeSequence++;
        var resultVar = new Variable($"invoke{seq}");
        return new Block([
            new Assignment(resultVar, invokeCall),
            new IfStatement(
                new Poly.Ast.Nodes.Not(new Member(resultVar, "IsSuccess")),
                new Block([ReturnCallerFailureFrom(resultVar)]))
        ], [resultVar]);
    }

    /// <summary>
    /// Rewrap nested Failure as the caller action's <c>DomainResult</c> /
    /// <c>DomainResult&lt;T&gt;</c> — same shape as create-in. Returning the callee
    /// result object is CS0029 when the caller is a different DomainResult arity.
    /// Runtime omits <see cref="LoweringContext.ActionResultType"/> and falls back
    /// to untyped <c>DomainResult.Failure</c>, which <c>ExecuteEffect</c> already
    /// fail-fasts on.
    /// </summary>
    private Node CallerResultType =>
        _context.ActionResultType ?? new TypeReference("DomainResult");

    private Return ReturnCallerFailure(Node errorMessage) =>
        new(new Invoke(new Member(CallerResultType, "Failure"), errorMessage));

    private Return ReturnCallerFailureFrom(Node resultVar) =>
        ReturnCallerFailure(
            new Syntactic.Coalesce(
                new Member(resultVar, "ErrorMessage"),
                new Constant("")));

    /// <summary>
    /// Lowers a <see cref="ForEachInvokeEffect"/> (the <c>for Rel as x [where x.Policy |
    /// where x in Stage] invoke x.Action(args)</c> fan-out) to a fail-fast loop over the
    /// source's collection navigation — "fetch all from storage, invoke on every record".
    /// The first failing record fails the whole <c>for</c>; zero matches fail (no vacuous
    /// success). The predicate is a named policy (bool method) or stage membership
    /// (<c>x.CurrentStage == TargetStage.X</c>) on the target entity.
    /// </summary>
    protected override Node? ForEachInvoke(ForEachInvokeEffect e) {
        var relName = e.RelationshipName;
        var navMember = new Member(Subject, DomainToCSharpExporter.ToPascalCase(relName));

        var seq = _forEachInvokeSequence++;
        var loopVar = new Variable($"target{seq}");

        // Predicate → a `continue` guard inside the loop (no LINQ dependency in the
        // standalone export): named policy → target's bool method; stage membership →
        // CurrentStage (string on runtime, enum on emit). Same loop Variable instance
        // so the VM can bind Current onto it.
        Node? predicateGuard = null;
        if (e.Predicate is not null) {
            Node predicateExpr = e.Predicate switch {
                ForEachNamedPolicy p => (Node)new Invoke(
                    new Member(loopVar, p.PolicyName)),
                ForEachStageMembership s => new Equal(
                    new Member(loopVar, "CurrentStage"),
                    _useThisReference || _stageEnumTypeName is not null
                        ? new Member(new NamedTypeReference(TargetStageEnumTypeName(relName)), s.StageName)
                        : new Constant(s.StageName)),
                _ => throw new NotSupportedException($"Unsupported ForEachInvoke predicate '{e.Predicate.GetType().Name}'."),
            };
            predicateGuard = new IfStatement(
                new Poly.Ast.Nodes.Not(predicateExpr),
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
        var args = new List<Node>();
        foreach (var binding in e.ParameterBindings)
            args.Add(argPass.Lower(binding.Expression, Subject));

        var invokeCall = new Invoke(new Member(loopVar, e.ActionName), [.. args]);

        // Fail-fast + zero-matches-fail. Same Variable instances for VM identity.
        var matchedVar = new Variable($"matched{seq}");
        var resultVar = new Variable($"result{seq}");
        var loopBody = new List<Node>();
        if (predicateGuard is not null) loopBody.Add(predicateGuard);
        loopBody.Add(new Assignment(matchedVar, new Constant(true)));
        loopBody.Add(new Assignment(resultVar, invokeCall));
        loopBody.Add(new IfStatement(new Poly.Ast.Nodes.Not(new Member(resultVar, "IsSuccess")),
            new Block([ReturnCallerFailureFrom(resultVar)])));
        var loop = new ForEachLoop(loopVar, navMember, new Block(loopBody, [resultVar]));
        var zeroCheck = new IfStatement(
            new Poly.Ast.Nodes.Not(matchedVar),
            new Block([ReturnCallerFailure(new Constant(
                $"for {relName}.{e.ActionName} matched zero targets."))]));
        return new Block(
            [new Assignment(matchedVar, new Constant(false)), loop, zeroCheck],
            [matchedVar]);
    }

    /// <summary>Resolves the stage enum CLR name for a target entity of a relationship
    /// (e.g. <c>LineStage</c>), from analysis when present, else the default convention.</summary>
    private string TargetStageEnumTypeName(string relationshipName) {
        var targetName = _entity.Navigations.FirstOrDefault(n =>
            string.Equals(n.Name, relationshipName, StringComparison.Ordinal))?.Target.TypeName;
        if (targetName is not null && _domain is not null) {
            var targetEntity = _domain.Types.OfType<Entity>()
                .FirstOrDefault(t => string.Equals(t.Name, targetName, StringComparison.Ordinal));
            if (targetEntity is not null && _analysis is not null) {
                var esm = _analysis.GetStructure(targetEntity);
                if (esm?.StageEnumTypeName is { } custom) return custom;
            }
            if (targetEntity is not null && targetEntity.Stages.Count > 0)
                return $"{targetName}Stage";
        }
        return $"{targetName ?? relationshipName}Stage";
    }

    /// <summary>
    /// Lowers a CompositeEffect. Every sub-effect must lower — a sub-effect that
    /// cannot be lowered (unknown create target, unresolved relationship) is a
    /// fail-closed error, never a silent drop. Mixed if+create lowers on the
    /// runtime path via instance factories (not ExecuteStructured).
    /// </summary>
    protected override Node? Composite(CompositeEffect c) {
        var nodes = new List<Node>();
        var variables = new List<Node>();
        foreach (var sub in c.Effects) {
            var lowered = Route(sub);
            if (lowered is null)
                throw new InvalidOperationException(
                    $"Cannot lower effect '{DescribeEffect(sub)}' to a Syntax AST node.");
            CollectNode(nodes, variables, lowered);
        }
        return new Block(nodes, variables);
    }

    protected override Node? Conditional(ConditionalEffect c) {
        var condition = _expressionPass.Lower(c.Condition, Subject);
        var thenNodes = new List<Node>();
        var thenVars = new List<Node>();
        foreach (var sub in c.ThenEffects) {
            var lowered = Route(sub);
            if (lowered is null)
                throw new InvalidOperationException(
                    $"Cannot lower effect '{DescribeEffect(sub)}' to a Syntax AST node.");
            CollectNode(thenNodes, thenVars, lowered);
        }

        if (c.ElseEffects is not { Count: > 0 })
            return new IfStatement(condition, new Block(thenNodes, thenVars));

        var elseNodes = new List<Node>();
        var elseVars = new List<Node>();
        foreach (var sub in c.ElseEffects) {
            var lowered = Route(sub);
            if (lowered is null)
                throw new InvalidOperationException(
                    $"Cannot lower effect '{DescribeEffect(sub)}' to a Syntax AST node.");
            CollectNode(elseNodes, elseVars, lowered);
        }

        return new IfStatement(condition, new Block(thenNodes, thenVars), new Block(elseNodes, elseVars));
    }

    /// <summary>Adds a lowered node to a list. Flattens Block children, keeping declarations.</summary>
    private static void CollectNode(List<Node> nodes, List<Node> variables, Node? lowered) {
        if (lowered is null) return;
        if (lowered is Block b) {
            nodes.AddRange(b.Nodes);
            variables.AddRange(b.Variables);
        }
        else
            nodes.Add(lowered);
    }

    /// <summary>
    /// Lowers CreateEntityInstance to <c>this.Create(type, prop, value, …)</c>
    /// with Failure unwrap. Same tree for simulate and emit.
    /// </summary>
    protected override Node? CreateEntityInstance(CreateEntityInstance cei) =>
        LowerRuntimeFactoryCall(
            "Create", cei.Type.TypeName, cei.Initializers,
            ResolveEntity(cei.Type.TypeName),
            cei.RelationshipName);

    /// <summary>
    /// Lowers create-in to <c>this.CreateIn(relationship, prop, value, …)</c>
    /// with Failure unwrap. Same tree for simulate and emit.
    /// </summary>
    protected override Node? CreateEntityInRelationship(CreateEntityInRelationshipEffect cr) {
        if (_domain is null)
            throw new InvalidOperationException(
                "Cannot execute 'create in' without a domain to resolve relationship targets.");
        var runtimeTarget = _analysis?.GetMetadata<ResolvedRelationshipTargetMetadata>(cr);
        var runtimeRel = runtimeTarget?.Relationship ?? ResolveRelationship(cr.RelationshipName);
        var runtimeEntity = runtimeTarget?.TargetEntity
            ?? (runtimeRel is not null ? ResolveEntity(runtimeRel.Target.TypeName) : null);
        return LowerRuntimeFactoryCall(
            "CreateIn", cr.RelationshipName, cr.Initializers, runtimeEntity);
    }

    internal List<Node> LowerCreateInConstraintProbes(
        IReadOnlyList<Effect> effects, bool priorMutation = false) {
        var nodes = new List<Node>();
        var assigned = new HashSet<string>(StringComparer.Ordinal);
        CollectCreateInProbes(effects, nodes, ref priorMutation, assigned);
        return nodes;
    }

    /// <summary>
    /// Guarded create probes (before prior assigns) plus the action body.
    /// Same tree for simulate and emit.
    /// </summary>
    internal Node? LowerActionBody(IReadOnlyList<Effect> effects) =>
        LowerActionBodyCore(effects);

    private Node? LowerActionBodyCore(IReadOnlyList<Effect> effects) {
        var probes = LowerCreateInConstraintProbes(effects);
        var lowered = TryLowerVmNode(new CompositeEffect(effects));
        if (probes.Count == 0)
            return lowered;
        var nodes = new List<Node>();
        var locals = new List<Node>();
        foreach (var probe in probes) {
            if (probe is Block pb) {
                nodes.AddRange(pb.Nodes);
                locals.AddRange(pb.Variables);
            }
            else {
                nodes.Add(probe);
            }
        }
        if (lowered is Block block) {
            nodes.AddRange(block.Nodes);
            locals.AddRange(block.Variables);
        }
        else if (lowered is not null) {
            nodes.Add(lowered);
        }
        return new Block(nodes, locals);
    }

    private void CollectCreateInProbes(
        IReadOnlyList<Effect> effects,
        List<Node> nodes,
        ref bool priorMutation,
        HashSet<string> assigned) {
        // Unconditional create / create-in (and composites of them) probe at method
        // start — same set as runtime PrevalidateUnconditionalCreates.
        // ConditionalEffect is not probed unguarded (illegal then-branch on an
        // untaken if must not fail the action). When a prior sibling already
        // mutates (assign / create / invoke / transition), emit a condition-guarded
        // probe so a taken illegal create returns Failure before those assigns.
        // Runtime skips that guarded probe when the condition reads a property a
        // prior sibling assigned (ConditionDrift: assign Create to false then if).
        // C# export keeps the documented pre-assign-bag probe.
        foreach (var effect in effects) {
            switch (effect) {
                case CompositeEffect composite:
                    CollectCreateInProbes(composite.Effects, nodes, ref priorMutation, assigned);
                    break;
                case CreateEntityInRelationshipEffect cr:
                    if (LowerCreateInProbe(cr) is { } probe)
                        nodes.Add(probe);
                    priorMutation = true;
                    break;
                case CreateEntityInstance cei:
                    if (LowerCreateEntityInstanceProbe(cei) is { } createProbe)
                        nodes.Add(createProbe);
                    priorMutation = true;
                    break;
                case ConditionalEffect cond:
                    if (priorMutation) {
                        var skipRuntimeGuarded =
                            ConditionReadsAssignedProperty(cond.Condition, assigned);
                        if (!skipRuntimeGuarded)
                            CollectGuardedBranchProbes(cond, nodes);
                    }
                    priorMutation = true;
                    break;
                case AssignEffect a:
                    priorMutation = true;
                    if (a.Target is PropertyAccess pa)
                        assigned.Add(pa.Name);
                    break;
                case StageTransitionEffect or InvokeActionEffect or ForEachInvokeEffect:
                    priorMutation = true;
                    break;
            }
        }
    }

    private static bool ConditionReadsAssignedProperty(
        DomainExpression expr, HashSet<string> assigned) {
        if (expr is PropertyAccess pa)
            return assigned.Contains(pa.Name);
        foreach (var child in expr.Children) {
            if (child is DomainExpression inner
                && ConditionReadsAssignedProperty(inner, assigned))
                return true;
        }
        return false;
    }

    private void CollectGuardedBranchProbes(ConditionalEffect cond, List<Node> nodes) {
        var thenProbes = new List<Node>();
        // Nested else-if is a ConditionalEffect in ElseEffects. Runtime
        // PrevalidateUnconditionalCreates recurses regardless of nested
        // priorMutation; start then/else as already-mutating so those probes
        // are collected (this walk is only entered after a prior mutation).
        var thenPrior = true;
        CollectCreateInProbes(cond.ThenEffects, thenProbes, ref thenPrior,
            new HashSet<string>(StringComparer.Ordinal));
        if (thenProbes.Count > 0) {
            var condition = _expressionPass.Lower(cond.Condition, Subject);
            nodes.Add(new IfStatement(condition, FlattenProbeBlocks(thenProbes)));
        }

        if (cond.ElseEffects is not { Count: > 0 })
            return;
        var elseProbes = new List<Node>();
        var elsePrior = true;
        CollectCreateInProbes(cond.ElseEffects, elseProbes, ref elsePrior,
            new HashSet<string>(StringComparer.Ordinal));
        if (elseProbes.Count == 0)
            return;
        var elseCondition = _expressionPass.Lower(cond.Condition, Subject);
        nodes.Add(new IfStatement(
            new Syntactic.Not(elseCondition), FlattenProbeBlocks(elseProbes)));
    }

    private static Block FlattenProbeBlocks(List<Node> probes) {
        var nodes = new List<Node>();
        var locals = new List<Node>();
        foreach (var probe in probes) {
            if (probe is Block b) {
                nodes.AddRange(b.Nodes);
                locals.AddRange(b.Variables);
            }
            else {
                nodes.Add(probe);
            }
        }
        return new Block(nodes, locals);
    }

    private Block? LowerCreateInProbe(CreateEntityInRelationshipEffect cr) {
        if (_domain is null || _analysis is null)
            return null;

        var resolvedTarget = _analysis.GetMetadata<ResolvedRelationshipTargetMetadata>(cr);
        var relationship = resolvedTarget?.Relationship
            ?? ResolveRelationship(cr.RelationshipName);
        if (relationship is null) return null;
        var targetEntity = resolvedTarget?.TargetEntity
            ?? ResolveEntity(relationship.Target.TypeName);
        if (targetEntity is null) return null;

        var probe = LowerRuntimeFactoryCall(
            "ProbeCreate", targetEntity.Name, cr.Initializers, targetEntity);
        return probe as Block ?? new Block([probe]);
    }

    private Block? LowerCreateEntityInstanceProbe(CreateEntityInstance cei) {
        var targetEntity = ResolveEntity(cei.Type.TypeName);
        if (targetEntity is null) return null;
        var probe = LowerRuntimeFactoryCall(
            "ProbeCreate", targetEntity.Name, cei.Initializers, targetEntity);
        return probe as Block ?? new Block([probe]);
    }

    /// <summary>
    /// Runtime create / probe: <c>this.Create/CreateIn/ProbeCreate(name, prop, value, ...)</c>
    /// with Failure unwrap. Entity-typed values are cast to object so the 1-pair
    /// object slot matches; primitives stay unboxed so the VM still boxes them at
    /// the object-parameter invoke (a TypeCast to object is a no-op on scalars).
    /// </summary>
    private Node LowerRuntimeFactoryCall(
        string methodName,
        string nameArg,
        IReadOnlyList<PropertyBinding> initializers,
        Entity? targetEntity,
        string? linkRelationshipName = null) {
        var args = new List<Node> { new Constant(nameArg) };
        foreach (var init in initializers) {
            args.Add(new Constant(init.PropertyName));
            var prop = targetEntity?.Properties.FirstOrDefault(p =>
                string.Equals(p.Name, init.PropertyName, StringComparison.Ordinal));
            Node value = prop is not null
                ? LowerEnumAwareValue(init.Expression, prop.Type, Subject)
                : _expressionPass.Lower(init.Expression, Subject);
            args.Add(NeedsObjectSlotCast(targetEntity, prop, init.PropertyName, value)
                ? new TypeCast(value, TypeReference.To<object>())
                : value);
        }

        var seq = _createInProbeSequence++;
        var resultVar = new Variable($"create{seq}");
        var resultType = _context.ActionResultType ?? new NamedTypeReference("DomainResult");
        var locals = new List<Node> { resultVar };
        var nodes = new List<Node> {
            new Assignment(resultVar, new Invoke(new Member(Subject, methodName), [.. args])),
            new IfStatement(
                new Syntactic.Not(new Member(resultVar, "IsSuccess")),
                new Block([
                    new Return(
                        new Invoke(
                            new Member(resultType, "Failure"),
                            new Syntactic.Coalesce(
                                new Member(resultVar, "ErrorMessage"),
                                new Constant(""))))
                ]))
        };
        if (!methodName.StartsWith("Probe", StringComparison.Ordinal)) {
            var valueVar = new Variable($"created{seq}");
            locals.Add(valueVar);
            nodes.Add(new Assignment(valueVar, new Member(resultVar, "Value")));
            if (linkRelationshipName is not null) {
                nodes.Add(new Invoke(
                    new Member(Subject, "LinkRelated"),
                    new Constant(linkRelationshipName),
                    valueVar));
            }
        }
        return new Block(nodes, locals);
    }

    /// <summary>
    /// Entity-typed create initializers (properties or singular navs) do not match
    /// the long/string/bool 1-pair slots. Cast those values to object. Do not cast
    /// primitives: TypeCast-to-object is a VM no-op on scalars and skips boxing.
    /// </summary>
    private bool NeedsObjectSlotCast(
        Entity? targetEntity, Property? prop, string initName, Node value) {
        if (value is Constant)
            return false;
        if (IsDomainEntityType(prop?.Type))
            return true;
        return targetEntity?.Navigations.Any(n =>
            string.Equals(n.Name, initName, StringComparison.Ordinal)) == true;
    }

    private bool IsDomainEntityType(DomainTypeReference? type) =>
        type is not null && _domain?.Types.OfType<Entity>().Any(e =>
            string.Equals(e.Name, type.TypeName, StringComparison.Ordinal)) == true;

    // ── Runtime default expression helpers ─────────────────

    /// <summary>
    /// Builds a Syntax AST node for a runtime default expression, adapted to the
    /// target property's CLR type when known (discovery round5 F1–F3).
    /// <c>now</c>/<c>utcnow</c> → <c>DateTime.UtcNow</c> on a DateTime target,
    /// <c>DateOnly.FromDateTime(DateTime.UtcNow)</c> on a Date target;
    /// <c>today</c> → <c>DateTime.Today</c> / <c>DateOnly.FromDateTime(DateTime.Today)</c>;
    /// <c>guid</c> → <c>Guid.NewGuid()</c> on a Guid target,
    /// <c>Guid.NewGuid().ToString()</c> on a Text target.
    /// Returns null for literal defaults (handled directly by the exporter).
    /// </summary>
    internal static Node? LowerDefaultExpression(
        DomainExpression expr,
        Node? typeHint = null) {
        var targetName = typeHint is NamedTypeReference ntr ? ntr.TypeName : null;
        var isDateTimeTarget = targetName is "DateTime" or "Timestamp";
        if (expr is Now) {
            return isDateTimeTarget
                ? new Member(new NamedTypeReference("DateTime"), "UtcNow")
                : new Invoke(new Member(new NamedTypeReference("DateOnly"), "FromDateTime"),
                    new Member(new NamedTypeReference("DateTime"), "UtcNow"));
        }
        if (expr is Today) {
            return isDateTimeTarget
                ? new Member(new NamedTypeReference("DateTime"), "Today")
                : new Invoke(new Member(new NamedTypeReference("DateOnly"), "FromDateTime"),
                    new Member(new NamedTypeReference("DateTime"), "Today"));
        }
        if (expr is not PropertyAccess pa) return null;
        return pa.Name switch {
            "Now" or "UtcNow" => isDateTimeTarget
                ? new Member(new NamedTypeReference("DateTime"), "UtcNow")
                : new Invoke(new Member(new NamedTypeReference("DateOnly"), "FromDateTime"),
                    new Member(new NamedTypeReference("DateTime"), "UtcNow")),
            "Today" => isDateTimeTarget
                ? new Member(new NamedTypeReference("DateTime"), "Today")
                : new Invoke(new Member(new NamedTypeReference("DateOnly"), "FromDateTime"),
                    new Member(new NamedTypeReference("DateTime"), "Today")),
            "Guid" => targetName is "Text" or "String"
                ? new Invoke(new Member(
                    new Invoke(new Member(new NamedTypeReference("Guid"), "NewGuid")), "ToString"))
                : new Invoke(new Member(new NamedTypeReference("Guid"), "NewGuid")),
            _ => null, // treat as enum member name
        };
    }

    /// <summary>
    /// Lowers an initializer/assignment VALUE whose target is
    /// <paramref name="targetType"/>. A bare identifier that names a member of the
    /// target enum type resolves to qualified member access
    /// (<c>create in tokens { Kind: Numeric }</c> → <c>TokenKind.Numeric</c>),
    /// mirroring how string literals already lower to qualified enum members on
    /// assign (<c>"Suspended"</c> → <c>PatronStatus.Suspended</c>). Any other
    /// expression (parameter, subject property, literal) lowers normally.
    /// </summary>
    private Node LowerEnumAwareValue(DomainExpression expr, DomainTypeReference targetType, Node subject) {
        if (DomainToCSharpExporter.TryResolveEnumType(_domain, _analysis, targetType.TypeName, out var enumType)
            && enumType is not null) {
            // Bare identifier member: Tier: Pro
            if (expr is PropertyAccess pa && enumType.MemberNames.Contains(pa.Name, StringComparer.Ordinal)) {
                return new Member(new NamedTypeReference(enumType.Name), pa.Name);
            }
            // String-literal member: Kind: "Keyword" — same qualification as assign.
            if (expr is Literal { Value: string s }
                && enumType.MemberNames.Contains(s, StringComparer.Ordinal)) {
                return new Member(new NamedTypeReference(enumType.Name), s);
            }
        }
        return _expressionPass.Lower(expr, subject);
    }

    private Entity? ResolveEntity(string typeName) {
        // Catalog primary when domain + analysis present.
        if (_analysis is not null) {
            var lookup = _analysis.GetTypeLookup(_domain);
            if (lookup is not null
                && lookup.Types.TryGetValue(typeName, out var domainType)
                && domainType is Entity entity)
                return entity;

            // Analysis present: fail closed (no domain tree rescan).
            return null;
        }

        if (_domain is not null) {
            return _domain.Types.OfType<Entity>().FirstOrDefault(e =>
                string.Equals(e.Name, typeName, StringComparison.Ordinal));
        }

        return null;
    }

    private Relationship? ResolveRelationship(string relationshipName) {
        if (_analysis is not null) {
            var lookup = _analysis.GetRelationshipLookup(_domain);
            if (lookup is not null
                && lookup.TryGetRelationship(_entity.Name, relationshipName, out var relationship))
                return relationship;

            return null;
        }

        if (_domain is not null) {
            return _entity.Navigations.FirstOrDefault(r =>
                string.Equals(r.Name, relationshipName, StringComparison.Ordinal));
        }

        return null;
    }

    /// <summary>
    /// Returns a human-readable description of why <paramref name="effect"/>
    /// cannot be lowered, including effect-specific detail like action names.
    /// </summary>
    private static string DescribeEffect(Effect effect) => effect switch {
        InvokeActionEffect i when i.TargetRelationship is null => $"invoke {i.ActionName}",
        InvokeActionEffect i => $"invoke {i.TargetRelationship}.{i.ActionName}",
        ForEachInvokeEffect efe => $"for {efe.RelationshipName}.{efe.ActionName}",
        StageTransitionEffect s => $"transition to {s.TargetStage.StageName} (StageTransitionEffect)",
        CreateEntityInstance cei => $"create {cei.Type.TypeName}",
        CreateEntityInRelationshipEffect cr => $"create in {cr.RelationshipName}",
        _ => $"Cannot lower: {effect.GetType().Name}"
    };
}