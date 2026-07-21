using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Effects;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Analyzes a <see cref="Domain"/> and produces a <see cref="TransportModel"/>
/// — action surface (parameters, return types, policies, stage transitions)
/// and cross-entity effect topology (create-in, invoke, subscriptions).
///
/// Call <see cref="Analyze"/> to compute the transport model, or use
/// <see cref="InfrastructureAnalyzer"/> which coordinates both
/// storage and transport analysis.
///
/// When an <see cref="AnalysisResult"/> is available (from domain evolution),
/// pass it to leverage pre-computed metadata like
/// <see cref="ActionCapabilityMetadata"/> (which already provides transition
/// targets and effect types) and <see cref="DomainTypeLookupMetadata"/>.
/// </summary>
public sealed class TransportAnalyzer {
    private readonly Domain _domain;
    private readonly List<Entity> _entities;
    private readonly List<Relationship> _relationships;
    private readonly Dictionary<string, Entity> _entityLookup;
    private readonly AnalysisResult? _analysis;

    public TransportAnalyzer(Domain domain, AnalysisResult? analysis = null) {
        _domain = domain;
        _analysis = analysis;

        // Use pre-computed metadata from analysis pipeline when available
        var lookup = analysis?.GetMetadata<DomainTypeLookupMetadata>(default);
        if (lookup is not null) {
            _entities = lookup.Entities.ToList();
            _entityLookup = lookup.Types
                .Where(kvp => kvp.Value is Entity)
                .ToDictionary(kvp => kvp.Key, kvp => (Entity)kvp.Value, StringComparer.Ordinal);
        }
        else {
            _entities = domain.Types.OfType<Entity>().ToList();
            _entityLookup = _entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        }

        _relationships = domain.Relationships.ToList();
    }

    /// <summary>Computes the transport model for the domain.</summary>
    public TransportModel Analyze() {
        // Pass 1: scan effect topology across all entities
        var topology = ScanEffectTopology();

        // Pass 2: build transport entities with action metadata
        var transportEntities = new List<TransportEntity>();
        foreach (var entity in _entities) {
            transportEntities.Add(BuildTransportEntity(entity, topology));
        }

        return new TransportModel(_domain.Name, transportEntities, topology);
    }

    // ── Effect topology scanning ──────────────────────────────

    private EffectTopology ScanEffectTopology() {
        var createInRels = new List<CreateInRelation>();
        var crossInvokes = new List<CrossEntityInvoke>();
        var subscriptions = new List<SubscriptionRelation>();

        var allActions = new List<(Entity Entity, DomainModeling.Action Action)>();
        foreach (var entity in _entities) {
            foreach (var action in entity.Actions)
                allActions.Add((entity, action));
            foreach (var stage in entity.Stages)
                foreach (var action in stage.Actions)
                    allActions.Add((entity, action));
        }

        foreach (var (entity, action) in allActions) {
            ScanActionEffects(entity, action, createInRels, crossInvokes);
        }

        // Subscriptions
        foreach (var entity in _entities) {
            foreach (var sub in entity.Subscriptions) {
                foreach (var stageName in sub.StageNames) {
                    subscriptions.Add(new SubscriptionRelation(
                        entity.Name, sub.RelationshipName, stageName));
                }
            }
            foreach (var stage in entity.Stages) {
                foreach (var sub in stage.Subscriptions) {
                    foreach (var stageName in sub.StageNames) {
                        subscriptions.Add(new SubscriptionRelation(
                            entity.Name, sub.RelationshipName, stageName));
                    }
                }
            }
        }

        return new EffectTopology(createInRels, crossInvokes, subscriptions);
    }

    private void ScanActionEffects(Entity entity, DomainModeling.Action action,
        List<CreateInRelation> createInRels, List<CrossEntityInvoke> crossInvokes) {
        foreach (var effect in action.Effects) {
            WalkEffects(effect, e => {
                switch (e) {
                    case CreateEntityInRelationshipEffect cir:
                        var createdRel = _relationships.FirstOrDefault(r =>
                            string.Equals(r.Name, cir.RelationshipName, StringComparison.Ordinal));
                        if (createdRel is not null)
                            createInRels.Add(new CreateInRelation(
                                entity.Name, action.Name,
                                cir.RelationshipName, createdRel.Target.TypeName));
                        break;

                    case CreateEntityInstance cei when cei.RelationshipName is not null:
                        createInRels.Add(new CreateInRelation(
                            entity.Name, action.Name,
                            cei.RelationshipName, cei.Type.TypeName));
                        break;

                    case InvokeActionEffect iae when iae.TargetRelationship is not null:
                        crossInvokes.Add(new CrossEntityInvoke(
                            entity.Name, action.Name,
                            iae.TargetRelationship, iae.ActionName));
                        break;
                }
            });
        }
    }

    private void WalkEffects(Effect effect, Action<Effect> visitor) {
        visitor(effect);
        if (effect is CompositeEffect ce) {
            foreach (var child in ce.Effects) WalkEffects(child, visitor);
        }
        if (effect is ConditionalEffect cond) {
            foreach (var e in cond.ThenEffects) WalkEffects(e, visitor);
            if (cond.ElseEffects is not null)
                foreach (var e in cond.ElseEffects) WalkEffects(e, visitor);
        }
    }

    // ── Transport entity building ────────────────────────────

    private TransportEntity BuildTransportEntity(Entity entity, EffectTopology topology) {
        var transport = new TransportEntity(entity.Name);

        // Determine transport parent from create-in topology — the entity
        // that creates instances of this entity is its transport context.
        var createInForEntity = topology.CreateInRelations
            .FirstOrDefault(c => string.Equals(c.CreatedEntity, entity.Name, StringComparison.Ordinal));
        transport.TransportParentName = createInForEntity?.CreatorEntity;

        // Entity-level actions
        foreach (var action in entity.Actions) {
            transport.AddAction(BuildTransportAction(entity, action, stageName: null));
        }

        // Stage-scoped actions
        foreach (var stage in entity.Stages) {
            foreach (var action in stage.Actions) {
                transport.AddAction(BuildTransportAction(entity, action, stage.Name));
            }
        }

        return transport;
    }

    private TransportAction BuildTransportAction(Entity entity, DomainModeling.Action action, string? stageName) {
        var isVoid = action.Result.Members.Count == 0;
        var resultTypeName = isVoid ? null : action.Result.Members[0].Type.TypeName;

        // Use EffectivePoliciesMetadata from analysis pipeline when available —
        // this correctly includes inherited entity-level and stage-level policies,
        // not just the action's direct policies.
        var policies = new List<string>();
        var effectivePolicies = _analysis?.GetMetadata<EffectivePoliciesMetadata>(action);
        if (effectivePolicies is not null) {
            foreach (var p in effectivePolicies.Policies)
                policies.Add(p.Name);
        }
        else {
            foreach (var p in action.Policies)
                policies.Add(p.Name);
        }

        // Parameter analysis: pre-computed ResolvedTypeReferenceMetadata avoids
        // manual _entityLookup scanning for entity-ref detection.
        var parameters = action.Parameters.Select(p => {
            var isEntityRef = IsEntityRefParam(p, action);
            var isRequired = p.Constraints.Any(c => c is RequiredConstraint);
            return new TransportParameter(
                p.Name,
                p.Type.TypeName,
                isEntityRef ? p.Type.TypeName : GetClrTypeName(p.Type.TypeName),
                isRequired,
                isEntityRef
            );
        }).ToList();

        // Use pre-computed transition targets from ActionCapabilityMetadata
        // when available — avoids re-walking the effect tree.
        var transitions = new List<StageTransitionTarget>();
        var capability = _analysis?.GetMetadata<ActionCapabilityMetadata>(action);
        if (capability is not null) {
            foreach (var stage in capability.View.TransitionTargets) {
                transitions.Add(new StageTransitionTarget(stage.Name));
            }
        }
        else {
            // Fallback: walk effects for StageTransitionEffect
            foreach (var effect in action.Effects) {
                WalkEffects(effect, e => {
                    if (e is StageTransitionEffect ste)
                        transitions.Add(new StageTransitionTarget(ste.TargetStage.StageName));
                });
            }
        }

        return new TransportAction(
            entity.Name,
            stageName,
            action.Name,
            parameters,
            isVoid,
            resultTypeName,
            policies,
            transitions
        );
    }

    /// <summary>
    /// Determines if a parameter references another entity type.
    /// Uses <see cref="ResolvedTypeReferenceMetadata"/> from the analysis pipeline
    /// when available, which is more reliable than manual string-based lookup
    /// (it accounts for aliases and type resolution).
    /// Falls back to the original <see cref="_entityLookup"/> dictionary.
    /// </summary>
    private bool IsEntityRefParam(Property param, DomainModeling.Action action) {
        // Fast path: use ResolvedTypeReferenceMetadata
        var resolved = _analysis?.GetMetadata<ResolvedTypeReferenceMetadata>(param.Type);
        if (resolved is not null)
            return resolved.Type is Entity;

        // Fast path: use ResolvedTypeReferenceMetadata directly on the action's
        // TypeMemberReference if the param type ref was resolved
        var paramRef = _analysis?.GetMetadata<ResolvedTypeReferenceMetadata>(action);
        // Fallback: string-based entity lookup
        return _entityLookup.ContainsKey(param.Type.TypeName);
    }

    private static string GetClrTypeName(string domainType) => domainType switch {
        "Text" or "String" => "string",
        "Number" or "Int" or "Int64" => "long",
        "Int32" => "int",
        "Boolean" or "Bool" => "bool",
        "DateTime" or "Timestamp" => "DateTime",
        "Date" or "DateOnly" => "DateOnly",
        "Time" or "TimeOnly" => "TimeOnly",
        "Duration" or "TimeSpan" => "TimeSpan",
        "Decimal" => "decimal",
        "Float" or "Double" => "double",
        "Guid" or "Uuid" => "Guid",
        _ => "string",
    };
}