using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;

using AccessModifier = Poly.Introspection.AccessModifier;
using PrimType = Poly.Introspection.PrimitiveType;
using Syntactic = Poly.Ast.Nodes;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Shared domain → Syntax projection layer.
/// Produces language-agnostic <see cref="TypeDefinitionNode"/> trees
/// from an analyzed <see cref="Domain"/>. Entity types, stage enums,
/// DomainResult scaffolding, and lowered policies.
///
/// Target-specific idiom decoration (DomainResult<T>, private set,
/// #nullable enable, static Create) is applied downstream by the C# target pack.
/// </summary>
public static class DomainProgramProjection {
    /// <summary>
    /// Projects the domain into language-agnostic Syntax type definitions
    /// for downstream consumers where analysis is required.
    /// </summary>
    public static IReadOnlyList<TypeDefinitionNode> ToSyntax(
        Domain domain, AnalysisResult analysis) {
        ArgumentNullException.ThrowIfNull(analysis);
        return ToSyntax(domain, (INodeMetadataProvider)analysis);
    }

    /// <summary>
    /// Projects the domain into language-agnostic Syntax type definitions.
    /// Currently delegates to static methods on <see cref="DomainToCSharpExporter"/>
    /// which will be migrated here incrementally.
    /// </summary>
    public static IReadOnlyList<TypeDefinitionNode> ToSyntax(
        Domain domain, INodeMetadataProvider metadata) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(metadata);
        var domainRelationships = metadata.GetAllRelationships(domain);
        var entities = domain.Types.OfType<Entity>().ToList();
        var entityLookup = entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        var result = new List<TypeDefinitionNode>();

        // ── Collect all subscriptions ─────────────────────────────
        var subscriptionsByTarget = new Dictionary<string, List<DomainToCSharpExporter.SubscriptionInfo>>(
            StringComparer.Ordinal);
        var subscriptionsBySubscriber = new Dictionary<string, List<DomainToCSharpExporter.SubscriptionInfo>>(
            StringComparer.Ordinal);

        foreach (var entity in entities) {
            var subList = new List<DomainToCSharpExporter.SubscriptionInfo>();

            // Read the analysis-published dispatch plans — the SAME metadata the runtime
            // consumes — instead of re-walking StageSubscription (no per-site derivation).
            // Fail-closed: an entity with subscriptions whose plan is absent means the
            // contract analyzer did not publish (missing relationship contracts) — throw,
            // never silently drop the subscriptions.
            var entityPlan = metadata.GetMetadata<SubscriptionDispatchPlanMetadata>(entity);
            if (entityPlan is null && (entity.Subscriptions.Count > 0
                || entity.Stages.Any(s => s.Subscriptions.Count > 0))) {
                throw new InvalidOperationException(
                    $"Subscription dispatch plan metadata is missing for entity '{entity.Name}'.");
            }

            if (entityPlan is not null)
                DomainToCSharpExporter.CollectSubscriptionInfo(entityPlan, entity, null, entityLookup, subList, subscriptionsByTarget);

            foreach (var stage in entity.Stages) {
                var stagePlan = metadata.GetMetadata<SubscriptionDispatchPlanMetadata>(stage);
                if (stagePlan is null && stage.Subscriptions.Count > 0) {
                    throw new InvalidOperationException(
                        $"Subscription dispatch plan metadata is missing for stage '{stage.Name}' on entity '{entity.Name}'.");
                }
                if (stagePlan is not null)
                    DomainToCSharpExporter.CollectSubscriptionInfo(stagePlan, entity, stage.Name, entityLookup, subList, subscriptionsByTarget);
            }

            if (subList.Count > 0)
                subscriptionsBySubscriber[entity.Name] = subList;
        }

        // ── Build enum type definitions ────────────────────────
        foreach (var enumType in domain.Types.OfType<EnumType>()) {
            var enumFields = new List<FieldDefinitionNode>();
            for (int i = 0; i < enumType.MemberNames.Count; i++) {
                enumFields.Add(new FieldDefinitionNode(
                    enumType.MemberNames[i],
                    new PrimitiveTypeReference(PrimType.Int32),
                    DefaultValue: new Constant((int)i),
                    AccessModifier: AccessModifier.Public
                ));
            }
            result.Add(new TypeDefinitionNode(
                enumType.Name,
                Fields: enumFields,
                Semantics: Syntactic.TypeDefinitionSemantics.MutableReference
            ));
        }

        foreach (var valueType in domain.Types.OfType<ValueType>())
            result.Add(DomainToCSharpExporter.BuildValueTypeTypeDef(valueType, domain, metadata));
        foreach (var contract in domain.ImportedContracts) {
            foreach (var valueType in contract.Types)
                result.Add(DomainToCSharpExporter.BuildValueTypeTypeDef(valueType, domain, metadata));
        }

        // ── Build DomainResult infrastructure types ─────────────
        result.Add(DomainToCSharpExporter.BuildDomainResultTypeDef());
        result.Add(DomainToCSharpExporter.BuildDomainResultGenericTypeDef());

        // ── Emit fail-closed adapters for bound contracts (pack-3c-3) ──
        // A bind is a call in export: each contract with at least one bound endpoint gets
        // an {Contract}Adapters class whose bound-endpoint methods throw
        // NotImplementedException until an in-process adapter is registered. The binding
        // is never dropped.
        foreach (var contract in domain.ImportedContracts) {
            var boundEndpoints = domain.ContractBindings
                .Where(b => string.Equals(b.ContractName, contract.Name, StringComparison.Ordinal))
                .Select(b => contract.Endpoints.FirstOrDefault(e =>
                    string.Equals(e.Name, b.EndpointName, StringComparison.Ordinal)))
                .Where(e => e is not null)
                .Cast<ContractEndpoint>()
                .DistinctBy(e => e.Name)
                .ToList();
            if (boundEndpoints.Count == 0) continue;
            result.Add(DomainToCSharpExporter.BuildContractAdapterTypeDef(contract, boundEndpoints));
        }

        // ── Build entity type definitions ─────────────────────────
        var handlerNames = DomainToCSharpExporter.BuildHandlerNames(subscriptionsBySubscriber);
        foreach (var entity in entities) {
            var targetSubs = subscriptionsByTarget.GetValueOrDefault(entity.Name);
            var subscriberSubs = subscriptionsBySubscriber.GetValueOrDefault(entity.Name);

            result.AddRange(DomainToCSharpExporter.BuildTypeDefsForEntity(
                entity, domain, domainRelationships, entityLookup, metadata,
                targetSubs, subscriberSubs, handlerNames));
        }

        return result;
    }
}