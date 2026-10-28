using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Pre-computes <see cref="EntityStructureMetadata"/> for every entity in the domain.
///
/// This pass runs during the analysis pipeline so lowering passes (StorageAnalyzer,
/// AggregateAnalyzer) can consume the results without re-scanning properties and
/// constraints. The metadata captures entity-local structural properties:
/// root detection, key structure, soft-delete, and stage tracking.
///
/// Cross-entity parent resolution (aggregate ownership) remains in the lowering
/// layer because it depends on <see cref="EffectTopology"/>.
/// </summary>
internal sealed class EntityStructureAnalyzer : INodeAnalyzer {
    public const string Id = "DomainEntityStructureAnalyzer";
    public string PassName => Id;

    public void Analyze(AnalysisContext context, Node node) {

        if (node is Domain domain) {
            AnalyzeDomain(context, domain);
            return;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnalyzeDomain(AnalysisContext context, Domain domain) {
        var lookup = context.GetTypeLookup(domain);
        if (lookup is null) return;

        foreach (var entity in lookup.Entities) {
            var metadata = ComputeStructure(entity, domain, lookup);
            context.SetMetadata(entity, metadata);
        }
    }

    private static EntityStructureMetadata ComputeStructure(
        Entity entity, Domain domain, DomainTypeLookupMetadata lookup) {

        // ── Natural key detection ──────────────────────────────
        var uniqueProp = entity.Properties.FirstOrDefault(p =>
            p.Constraints.Any(c => c is UniqueConstraint));
        var hasNaturalKey = uniqueProp is not null;
        var keyPropName = uniqueProp?.Name;
        // Map domain type → host CLR name for natural keys; shadow keys use int.
        var keyClrType = hasNaturalKey
            ? RuntimeAnalysisCache.ClrTypeName(domain, uniqueProp!.Type.TypeName)
            : "int";

        // ── Root detection ────────────────────────────────────
        // An entity is a root unless something owns it: a required reference to
        // another entity (a singular nav to its owner, or an entity-typed property),
        // or another entity's `owned` nav (which makes that target a child).
        var isRoot = !HasRequiredEntityRef(entity, lookup) && !IsOwnedTarget(entity, domain);

        // ── Stage tracking ────────────────────────────────────
        // A single stage with no entry/exit, no stage policy, and no transition
        // into it is implicit: the lifecycle is constant and the printed type
        // needs no stage enum or CurrentStage field (G21).
        var hasStages = entity.Stages.Count > 0 && !IsImplicitStage(entity, domain);
        string? stageEnumTypeName = null;
        IReadOnlyDictionary<string, Stage>? stageByName = null;
        if (hasStages) {
            stageEnumTypeName = domain.Types
                .OfType<EnumType>()
                .FirstOrDefault(e => e.Name == $"{entity.Name}Stage")
                ?.Name ?? $"{entity.Name}Stage";
            stageByName = entity.Stages.ToDictionary(s => s.Name, StringComparer.Ordinal);
        }

        var constructorParameters = ComputeConstructorParameterOrder(entity, lookup);
        var entryAssignedPropertyNames = ComputeEntryAssignedPropertyNames(entity);

        // ── Lifecycle detection ────────────────────────────────
        // A property whose type is a domain enum whose member-name set is exactly
        // equal (ordinal, same count) to the entity's stage-name set is the lifecycle
        // property. Exactly one candidate → that property; zero → none; more than
        // one → throw (fail closed, never guess).
        string? lifecyclePropertyName = null;
        string? lifecycleEnumTypeName = null;
        if (hasStages) {
            var stageNames = entity.Stages.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
            var candidates = new List<(string Property, string Enum)>();
            foreach (var prop in entity.Properties) {
                if (!lookup.Types.TryGetValue(prop.Type.TypeName, out var resolved)
                    || resolved is not EnumType enumType)
                    continue;
                if (enumType.MemberNames.Count != stageNames.Count)
                    continue;
                if (!enumType.MemberNames.All(stageNames.Contains))
                    continue;
                candidates.Add((prop.Name, enumType.Name));
            }
            if (candidates.Count > 1) {
                throw new InvalidOperationException(
                    $"Entity '{entity.Name}' has multiple lifecycle-enum candidate properties " +
                    $"({string.Join(", ", candidates.Select(c => $"'{c.Property}'"))}); cannot infer the lifecycle property.");
            }
            if (candidates.Count == 1) {
                lifecyclePropertyName = candidates[0].Property;
                lifecycleEnumTypeName = candidates[0].Enum;
            }
        }

        // ── Enum-typed property map (property name → enum type name) ──
        Dictionary<string, string>? enumPropertyNames = null;
        foreach (var prop in entity.Properties) {
            if (lookup.Types.TryGetValue(prop.Type.TypeName, out var resolved)
                && resolved is EnumType) {
                (enumPropertyNames ??= new Dictionary<string, string>(StringComparer.Ordinal))[prop.Name] = prop.Type.TypeName;
            }
        }

        return new EntityStructureMetadata(
            isRoot, hasNaturalKey, keyPropName, keyClrType,
            hasStages, stageEnumTypeName, stageByName, constructorParameters,
            enumPropertyNames, entryAssignedPropertyNames,
            lifecyclePropertyName, lifecycleEnumTypeName
        );
    }

    /// <summary>
    /// Names of entity properties assigned by the FIRST stage's entry effects. The
    /// exported constructor runs those effects after setting CurrentStage, so these
    /// props are body-initialized — never ctor params (a param would be dead + written
    /// twice, e.g. StartedAt). Published on <see cref="EntityStructureMetadata.EntryAssignedPropertyNames"/>
    /// so the exporter's ctor emission and this signature stay in lockstep.
    /// </summary>
    internal static IReadOnlySet<string> ComputeEntryAssignedPropertyNames(Entity entity) {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (entity.Stages.Count > 0) {
            foreach (var effect in entity.Stages[0].OnEntryEffects) {
                if (effect is AssignEffect ae && ae.Target is PropertyAccess pa)
                    names.Add(pa.Name);
            }
        }
        return names;
    }

    /// <summary>
    /// True when the entity has exactly one stage that is not observable: no
    /// entry/exit effects, no stage policies, and no effect transitions into it.
    /// Such a stage is constant, so the printer omits the stage enum and
    /// <c>CurrentStage</c>; stage-scoped actions become entity-level.
    /// </summary>
    internal static bool IsImplicitStage(Entity entity, Domain? domain = null) {
        if (entity.Stages.Count != 1)
            return false;
        var stage = entity.Stages[0];
        if (stage.OnEntryEffects.Count > 0
            || stage.OnExitEffects.Count > 0
            || stage.Policies.Count > 0
            || stage.Subscriptions.Count > 0)
            return false;
        if (domain is not null && IsWatchedStage(entity, stage.Name, domain))
            return false;
        return !TransitionsTo(entity, stage.Name);
    }

    /// <summary>True when any subscription in the domain watches <paramref name="stageName"/> on <paramref name="entity"/>.</summary>
    private static bool IsWatchedStage(Entity entity, string stageName, Domain domain) {
        foreach (var source in domain.Types.OfType<Entity>()) {
            var subscriptions = source.Subscriptions
                .Concat(source.Stages.SelectMany(s => s.Subscriptions));
            foreach (var subscription in subscriptions) {
                if (!subscription.StageNames.Contains(stageName, StringComparer.Ordinal))
                    continue;
                var target = source.Navigations.FirstOrDefault(n =>
                    string.Equals(n.Name, subscription.RelationshipName, StringComparison.Ordinal));
                if (target is not null
                    && string.Equals(target.Target.TypeName, entity.Name, StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }

    private static bool TransitionsTo(Entity entity, string stageName) {
        static bool In(IEnumerable<Effect> effects, string target) =>
            effects.Any(effect => effect switch {
                StageTransitionEffect transition =>
                    string.Equals(transition.TargetStage.StageName, target, StringComparison.Ordinal),
                CompositeEffect composite => In(composite.Effects, target),
                ConditionalEffect conditional =>
                    In(conditional.ThenEffects, target)
                    || (conditional.ElseEffects is not null && In(conditional.ElseEffects, target)),
                _ => false
            });

        return entity.Actions.Any(a => In(a.Effects, stageName))
            || entity.Subscriptions.Any(s => In(s.Effects, stageName))
            || entity.Stages.Any(stage =>
                stage.Actions.Any(a => In(a.Effects, stageName))
                || In(stage.OnEntryEffects, stageName)
                || In(stage.OnExitEffects, stageName)
                || stage.Subscriptions.Any(s => In(s.Effects, stageName)));
    }

    private static IReadOnlyList<ConstructorParameterOrder> ComputeConstructorParameterOrder(
        Entity entity, DomainTypeLookupMetadata lookup) {
        var parameters = new List<ConstructorParameterOrder>();

        // Props assigned by the FIRST stage's entry effects are body-initialized in
        // the exported ctor (which runs those effects after setting CurrentStage) —
        // they are NOT ctor params. Shared rule published as
        // EntityStructureMetadata.EntryAssignedPropertyNames (single source of truth).
        var entryAssignedProps = ComputeEntryAssignedPropertyNames(entity);

        foreach (var prop in entity.Properties.OrderBy(p => p.Name)) {
            if (prop.Constraints.Any(c => c is DefaultValueConstraint)) continue;
            if (entryAssignedProps.Contains(prop.Name)) continue;
            parameters.Add(new ConstructorParameterOrder(prop.Name, prop.Type, IsNavigation: false, IsBackReference: false));
        }

        foreach (var rel in entity.Navigations) {
            var isMany = rel.Cardinality is RelationshipCardinality.OneToMany
                         or RelationshipCardinality.ManyToMany;
            if (string.Equals(rel.Target.TypeName, entity.Name, StringComparison.Ordinal)) {
                parameters.Add(new ConstructorParameterOrder(rel.Name, rel.Target, IsNavigation: true, IsBackReference: true, IsCollection: isMany));
                continue;
            }

            parameters.Add(new ConstructorParameterOrder(rel.Name, rel.Target, IsNavigation: true, IsBackReference: false, IsCollection: isMany));
        }

        return parameters;
    }

    /// <summary>
    /// Determines if an entity has a required entity reference in its
    /// constructor params (either as a property or an outgoing singular nav).
    /// </summary>
    private static bool HasRequiredEntityRef(Entity entity, DomainTypeLookupMetadata lookup) {
        if (entity.Properties.Any(p =>
            !p.Constraints.Any(c => c is DefaultValueConstraint) &&
            lookup.Types.TryGetValue(p.Type.TypeName, out var type) && type is Entity))
            return true;

        foreach (var rel in entity.Navigations) {
            if (rel.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany)
                continue;
            // An `owned` nav declares this entity as the owner of the target, so it is
            // not a reference to an owner of this entity.
            if (rel.SourceOwnsTarget)
                continue;
            if (string.Equals(rel.Target.TypeName, entity.Name, StringComparison.Ordinal))
                continue;
            return true;
        }

        return false;
    }

    /// <summary>
    /// True when another entity declares an <c>owned</c> nav to this one — the target
    /// of an owned relationship is a child, never a root.
    /// </summary>
    private static bool IsOwnedTarget(Entity entity, Domain domain) {
        foreach (var other in domain.Types.OfType<Entity>()) {
            if (string.Equals(other.Name, entity.Name, StringComparison.Ordinal))
                continue;
            foreach (var rel in other.Navigations) {
                if (rel.SourceOwnsTarget
                    && string.Equals(rel.Target.TypeName, entity.Name, StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }
}