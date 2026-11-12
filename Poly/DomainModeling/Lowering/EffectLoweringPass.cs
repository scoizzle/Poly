using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Meaning;
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
/// <para>By-type create lowers to <c>Type.Create(args…)</c>. Create-in lowers
/// to <c>this.Create{Nav}(args…)</c>, which links the child. A failed create
/// returns the caller's <c>DomainResult</c>. StageTransition and invoke are
/// the same Syntax on both paths.</para>
///
/// <para>Lowering reads the pre-computed <see cref="IAnalysisMetadata"/> that
/// <see cref="LoweringContext"/> requires instead of re-scanning domain collections;
/// only a bag a pass did not publish for a node falls back to a computed value.
/// The emitted tree is generic
/// Syntax (Store calls, Assignment, …); it does not carry bag types.</para>
/// </summary>
public sealed partial class EffectLoweringPass : EffectDispatch<Node?> {
    private readonly Entity _entity;
    private readonly Domain _domain;
    private readonly DomainExpressionLoweringPass _expressionPass;
    private readonly INodeMetadataProvider _analysis;
    private readonly string? _stageEnumTypeName;
    private readonly IReadOnlySet<string>? _postTransitionNotifyStages;
    private readonly IReadOnlySet<string>? _previousStageNotifyStages;
    private string? _sourceStageName;
    private readonly string? _lifecyclePropertyName;
    private readonly string? _lifecycleEnumTypeName;
    private readonly LoweringContext _context;
    /// <summary>Entity has entry/exit transitions: emit the shared loop-guard Note.</summary>
    private readonly bool _emitAutomaticStageGuard;
    private readonly LocalNames _names;
    /// <summary>Stages whose exit is already being inlined — nested transitions must not re-enter.</summary>
    private readonly HashSet<string> _exitsInlining = new(StringComparer.Ordinal);
    /// <summary>True while inlining a hop's source exit block: a nested hop to the same target is redundant.</summary>
    private bool _inSourceExitInline;
    /// <summary>The enclosing hop's target while <see cref="_inSourceExitInline"/> is set.</summary>
    private string? _enclosingHopTarget;
    /// <summary>Stages whose entry is already being inlined — nested transitions must not re-enter.</summary>
    private readonly HashSet<string> _entriesInlining = new(StringComparer.Ordinal);

    /// <summary>Pre-computed analysis metadata provider, when available.</summary>
    public INodeMetadataProvider Analysis => _analysis;

    /// <summary>
    /// Creates a pass for <paramref name="entity"/>. The effect's entity always sets
    /// <see cref="LoweringContext.SourceEntityName"/>, overriding any value in
    /// <paramref name="context"/>.
    /// </summary>
    public EffectLoweringPass(Entity entity, LoweringContext context) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Analysis);
        ArgumentNullException.ThrowIfNull(context.Domain);
        _entity = entity;
        _context = (context.Names is null ? context with { Names = new LocalNames() } : context)
            with { SourceEntityName = entity.Name };
        _names = _context.Names!;
        _domain = context.Domain;
        _analysis = context.Analysis;
        _stageEnumTypeName = context.StageEnumTypeName;
        _postTransitionNotifyStages = context.PostTransitionNotifyStages;
        _previousStageNotifyStages = context.PreviousStageNotifyStages;
        _sourceStageName = context.SourceStageName;
        _lifecyclePropertyName = context.LifecyclePropertyName;
        _lifecycleEnumTypeName = context.LifecycleEnumTypeName;
        _emitAutomaticStageGuard = EffectHelpers.HasAutomaticTransitions(entity);
        IReadOnlyDictionary<string, Node>? parameters = context.Parameters;
        if (context.ActionParameterNames is { Count: > 0 }) {
            var merged = parameters is null
                ? new Dictionary<string, Node>(StringComparer.Ordinal)
                : new Dictionary<string, Node>(parameters, StringComparer.Ordinal);
            foreach (var name in context.ActionParameterNames) {
                if (!merged.ContainsKey(name))
                    merged[name] = new Parameter(name);
            }
            parameters = merged;
        }
        _expressionPass = new DomainExpressionLoweringPass(_context with {
            Parameters = parameters,
            NavigationNameResolver = context.NavigationNameResolver ?? BuildNavigationNameResolver(entity, _domain, _analysis),
            IsCollectionNavigation = context.IsCollectionNavigation
                ?? BuildIsCollectionNavigation(entity, _domain, _analysis),
            PropertyTypeResolver = context.PropertyTypeResolver ?? BuildPropertyTypeResolver(entity)
        });
        Subject = context.Subject;
    }

    /// <summary>
    /// Builds the default DSL-nav-name → generated-member-name resolver for the
    /// entity: relationship navigation names (source-side) pascal-case to the
    /// exporter's property naming (<c>compilations</c> → <c>Compilations</c>);
    /// plain properties keep their DSL name. Analysis metadata is primary;
    /// falls back to the domain relationship list. Identity when no context.
    /// </summary>
    internal static Func<string, string> BuildNavigationNameResolver(
        Entity entity, Domain domain, INodeMetadataProvider analysis) {
        var rlm = analysis.GetRelationshipLookup(domain);
        if (rlm is not null) {
            return name => rlm.TryGetRelationship(entity.Name, name, out var rel)
                    ? DomainToCSharpExporter.ToPascalCase(name)
                    : name;
        }
        var sourceNavs = entity.Navigations
            .Select(r => r.Name)
            .ToHashSet(StringComparer.Ordinal);
        return name => sourceNavs.Contains(name)
            ? DomainToCSharpExporter.ToPascalCase(name)
            : name;
    }

    /// <summary>
    /// Builds the default "is this DSL nav name a collection relationship on the
    /// current entity" predicate, mirroring <see cref="BuildNavigationNameResolver"/>
    /// (analysis metadata primary; domain relationship list fallback; false when
    /// unknown). Used to lower <c>Rel exists</c> on <c>many</c> navs to a
    /// <c>.Count != 0</c> check in the export.
    /// </summary>
    internal static Func<string, bool> BuildIsCollectionNavigation(
        Entity entity, Domain domain, INodeMetadataProvider analysis) {
        var rlm = analysis.GetRelationshipLookup(domain);
        if (rlm is not null) {
            return name => rlm.TryGetRelationship(entity.Name, name, out var rel)
                && rel.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany;
        }
        var navCardinalities = entity.Navigations
            .GroupBy(r => r.Name)
            .ToDictionary(g => g.Key, g => g.First().Cardinality, StringComparer.Ordinal);
        return name => navCardinalities.TryGetValue(name, out var card)
            && card is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany;
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
    /// compilation, or returns <c>null</c> when the effect has no lowered form.
    /// </summary>
    public Node? TryLowerVmNode(Effect effect) => Route(effect);

    protected override Node? Default() => null;

    /// <summary>
    /// The action body. Create-in is <c>this.Create{Nav}</c>. Constraint probes
    /// are not prefixed.
    /// </summary>
    internal Node? LowerActionBody(IReadOnlyList<Effect> effects) =>
        TryLowerVmNode(new CompositeEffect(effects));

    private Node? RouteWithRuntimeCreate(Effect effect) =>
        Route(effect);

    // Loop binder name → loop variable, for `invoke binder.Action` inside a general `for` body.
    private IReadOnlyDictionary<string, Node>? _loopBinders;

    /// <summary>
    /// OnEntry/OnExit effects lower into <paramref name="bodySink"/>. Create
    /// effects lower as one composite so nested structure stays intact.
    /// </summary>
    internal void AppendInlinedStageEffects(IReadOnlyList<Effect> effects, List<Node> bodySink) {
        if (effects.Count == 0)
            return;
        if (HasCreate(effects)) {
            var body = TryLowerVmNode(new CompositeEffect(effects));
            if (body is not null)
                bodySink.Add(body);
            return;
        }
        foreach (var effect in effects) {
            var lowered = RouteWithRuntimeCreate(effect);
            if (lowered is not null && lowered is not Block { Nodes.Count: 0 })
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
    /// Failure for assign guards: <c>return DomainResult.Failure</c> when the
    /// caller has a result type; <c>throw ConstraintFailureException</c> in void
    /// export contexts (ctor / stage entry) — same split as create-in fail-closed.
    /// </summary>
    private Node AssignConstraintFailure(Node message) {
        if (_context.ActionResultType is null) {
            return new ThrowStatement(new New(
                new NamedTypeReference("ConstraintFailureException"),
                message));
        }
        return ReturnCallerFailure(message);
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

    // ── Runtime default expression helpers ─────────────────

    /// <summary>
    /// Builds a Syntax AST node for a runtime default expression, adapted to the
    /// target property's CLR type when known (discovery round5 F1–F3).
    /// Library clocks resolve via session ident rewrite then Meaning.Defaults.
    /// <c>guid</c> → <c>Guid.NewGuid()</c> on a Guid target,
    /// <c>Guid.NewGuid().ToString()</c> on a Text target.
    /// Returns null for literal defaults (handled directly by the exporter).
    /// </summary>
    private Node? LowerDefault(DomainExpression expr, Node? typeHint = null) =>
        LowerDefaultExpression(
            expr,
            typeHint,
            _context.Meaning ?? RuntimeAnalysisCache.MeaningFor(_domain),
            _context.Forms ?? RuntimeAnalysisCache.FormsFor(_domain));

    internal static Node? LowerDefaultExpression(
        DomainExpression expr,
        Node? typeHint = null,
        ExpressionMeaning? meaning = null,
        ExpressionFormRegistry? forms = null) {
        var targetName = typeHint is NamedTypeReference ntr ? ntr.TypeName : null;
        if (expr is PropertyAccess pa && forms is not null && forms.TryRewriteIdent(pa.Name, out var claimed))
            expr = claimed;
        if ((meaning ?? ExpressionMeaning.Empty).Defaults.TryResolve(expr, targetName, out _, out var export))
            return export;
        if (expr is not PropertyAccess guid) return null;
        return guid.Name switch {
            "Guid" => targetName is "Text" or "String"
                ? new Invoke(new Member(
                    new Invoke(new Member(new NamedTypeReference("Guid"), "NewGuid")), "ToString"))
                : new Invoke(new Member(new NamedTypeReference("Guid"), "NewGuid")),
            _ => null,
        };
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