using Poly.Analysis;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Effects;

using AccessModifier = Poly.Introspection.AccessModifier;
using PrimType = Poly.Introspection.PrimitiveType;
using Syntactic = Poly.Ast.Nodes;

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
        var domainRelationships = domain.Relationships.ToList();
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

            foreach (var sub in entity.Subscriptions)
                DomainToCSharpExporter.CollectSubscriptionInfo(sub, entity, null, domainRelationships, entityLookup, subList, subscriptionsByTarget, metadata as AnalysisResult);

            foreach (var stage in entity.Stages)
                foreach (var sub in stage.Subscriptions)
                    DomainToCSharpExporter.CollectSubscriptionInfo(sub, entity, stage.Name, domainRelationships, entityLookup, subList, subscriptionsByTarget, metadata as AnalysisResult);

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

        // ── Build DomainResult infrastructure types ─────────────
        result.Add(DomainToCSharpExporter.BuildDomainResultTypeDef());
        result.Add(DomainToCSharpExporter.BuildDomainResultGenericTypeDef());

        // ── Build entity type definitions ─────────────────────────
        foreach (var entity in entities) {
            var targetSubs = subscriptionsByTarget.GetValueOrDefault(entity.Name);
            var subscriberSubs = subscriptionsBySubscriber.GetValueOrDefault(entity.Name);

            result.AddRange(DomainToCSharpExporter.BuildTypeDefsForEntity(
                entity, domain, domainRelationships, entityLookup, metadata,
                targetSubs, subscriberSubs));
        }

        return result;
    }
}