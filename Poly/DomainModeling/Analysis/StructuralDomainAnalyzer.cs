using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

using Action = Poly.DomainModeling.Ontology.Action;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Analysis;

/// <summary>Lint-only: tree-shape diagnostics; writes no metadata others read.</summary>
internal sealed class StructuralDomainAnalyzer : INodeAnalyzer {
    public const string Id = "DomainStructuralDomainAnalyzer";
    public string PassName => Id;
    public void Analyze(AnalysisContext context, Node node) {
        switch (node) {
            case Domain domain:
                AnalyzeDomain(context, domain);
                break;
            case Entity entity:
                AnalyzeEntity(context, entity);
                break;
            case Stage stage:
                AnalyzeStage(context, stage);
                break;

            case Action action:
                AnalyzeAction(context, action);
                break;
            case Relationship relationship:
                AnalyzeRelationship(context, relationship);
                break;
            case DomainExpression:
                // Expressions are walked via AnalyzeChildren. No special structural rules yet.
                break;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnalyzeDomain(AnalysisContext context, Domain domain) {
        // Type names remain domain-global (entities, enums, primitives).
        ReportDuplicateNames(context, domain.Types.Cast<DomainMember>(), "domain", domain.Name);

        // Relationship names must not collide with type names (DSL/export ambiguity).
        var typeNames = domain.Types.Cast<DomainMember>()
            .GroupBy(static t => t.Name, StringComparer.Ordinal)
            .Select(static g => g.Key)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var nav in domain.Types.OfType<Entity>().SelectMany(e => e.Navigations)) {
            if (typeNames.Contains(nav.Name)) {
                context.ReportError(
                    nav,
                    $"Relationship name '{nav.Name}' collides with a type of the same name.",
                    DomainModelDiagnosticCodes.StructuralDuplicate);
            }
            // P7-4: `any`/`all`/`none`/`count` are consumed as quantifier keywords in
            // expression reads — a nav by that name would be silently unreadable.
            if (IsReservedExpressionWord(nav.Name)) {
                context.ReportError(
                    nav,
                    $"Relationship name '{nav.Name}' is reserved (used by expression quantifiers) " +
                    "and cannot be read in policies, subscriptions, or invoke bindings. " +
                    "Rename the relationship.",
                    DomainModelDiagnosticCodes.StructuralDuplicate);
            }
        }

        // S11: a collection nav whose generated member name (PascalCase) equals its
        // target type name shadows that type once the create-in factory exists.
        ReportNavigationNameShadowing(context, domain);

        // N4: names lowering emits at module scope must not be user types.
        // Emit/RegisterTrees puts each entity with its `{Entity}Stage` enum in one tree
        // and DomainResult in the scaffolding tree; a user type of that name compiles as CS0101.
        ReportReservedGeneratedNames(context, domain);

        ReportStoredValueTypeProperties(context, domain);
    }

    /// <summary>
    /// A domain <c>value</c> type is a contract payload / action parameter — it has no
    /// identity or storage representation. A property typed as one projects as an
    /// unmappable EF column (the generated context builds; the host throws on first
    /// use) and breaks the VM's create-in path. Reject it at analysis: the stored shape
    /// is a nested entity, or the value travels as an action parameter.
    /// </summary>
    private static void ReportStoredValueTypeProperties(AnalysisContext context, Domain domain) {
        var valueTypeNames = domain.Types.OfType<ValueType>()
            .Select(static t => t.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (valueTypeNames.Count == 0)
            return;

        foreach (var entity in domain.Types.OfType<Entity>()) {
            foreach (var prop in entity.Properties) {
                if (!valueTypeNames.Contains(prop.Type.TypeName))
                    continue;
                context.ReportError(
                    prop,
                    $"Entity '{entity.Name}' property '{prop.Name}' is typed as value type '{prop.Type.TypeName}'. " +
                    "A value type is a contract payload or action parameter and has no storage representation. " +
                    $"Declare a nested entity for stored state, or pass '{prop.Type.TypeName}' as an action parameter.",
                    DomainModelDiagnosticCodes.StoredValueTypeProperty);
            }
        }
    }

    /// <summary>
    /// S11: lowering names a nav's C# member <c>PascalCase(nav.Name)</c>; the create-in
    /// factory emits <c>{TargetType}.Create(...)</c> in an expression position inside the
    /// owning class, so when member and target type share a name the call can bind to the
    /// member and the host fails (CS1061, or a confusing other-overload CS7036).
    ///
    /// Two measured qualifiers keep this off working domains:
    /// <list type="bullet">
    /// <item>A <b>singular</b> nav is exempt: its property type is the target type itself
    /// (<c>Folio? Folio</c>), so C#'s "Color Color" rule resolves <c>Folio.Create</c> to
    /// the type. Maintained probes rely on this (<c>Reservation.folio</c>, <c>Contact.account</c>,
    /// <c>Customer.address</c>). A <b>collection</b> nav's property type is
    /// <c>IReadOnlyList&lt;T&gt;</c>, so the rule does not apply and the member wins.</item>
    /// <item>No factory, no break: without a create-in for the nav the class never
    /// references the target type by simple name in an expression position, and this
    /// shape is everywhere in maintained probes.</item>
    /// </list>
    /// </summary>
    private static void ReportNavigationNameShadowing(AnalysisContext context, Domain domain) {
        foreach (var entity in domain.Types.OfType<Entity>()) {
            foreach (var nav in entity.Navigations) {
                if (nav.Cardinality is not (RelationshipCardinality.OneToMany
                    or RelationshipCardinality.ManyToMany))
                    continue;
                var targetTypeName = nav.Target.TypeName;
                // The exact-name form is already the `StructuralDuplicate` rule above.
                if (string.Equals(nav.Name, targetTypeName, StringComparison.Ordinal))
                    continue;
                var generatedMemberName = DomainTypeMapping.ToPascalCase(nav.Name);
                if (!string.Equals(generatedMemberName, targetTypeName, StringComparison.Ordinal))
                    continue;
                if (!EffectHelpers.CreatesIn(entity, nav.Name))
                    continue;
                context.ReportError(
                    nav,
                    $"Navigation '{nav.Name}' on entity '{entity.Name}' generates member '{generatedMemberName}', " +
                    $"which shadows entity type '{targetTypeName}': the create-in factory's " +
                    $"'{targetTypeName}.Create(...)' binds to the member, so the generated host does not compile. " +
                    $"Rename the navigation, or qualify the generated factory (e.g. 'global::{targetTypeName}.Create').",
                    DomainModelDiagnosticCodes.NavigationShadowsEntityType);
            }
        }
    }

    /// <summary>
    /// Reserved set: <c>DomainResult</c> (scaffolding) and <c>{Entity}Stage</c> for every
    /// entity that has stages. Card claim "Emit splits files by type name" is slightly
    /// off — Emit splits by entity tree (<c>entity.Name</c> / <c>{Entity}Stage</c>) plus
    /// one scaffolding tree; the collision is still a duplicate top-level C# type name.
    /// </summary>
    private static void ReportReservedGeneratedNames(AnalysisContext context, Domain domain) {
        const string scaffoldingResultName = "DomainResult";
        foreach (var type in domain.Types) {
            if (!string.Equals(type.Name, scaffoldingResultName, StringComparison.Ordinal))
                continue;
            context.ReportError(
                type,
                $"Type '{scaffoldingResultName}' collides with the scaffolding type generated by Emit; rename it.",
                DomainModelDiagnosticCodes.ReservedGeneratedName);
        }

        foreach (var entity in domain.Types.OfType<Entity>()) {
            if (entity.Stages.Count == 0)
                continue;
            var stageEnumName = $"{entity.Name}Stage";
            foreach (var type in domain.Types) {
                if (ReferenceEquals(type, entity))
                    continue;
                if (!string.Equals(type.Name, stageEnumName, StringComparison.Ordinal))
                    continue;
                context.ReportError(
                    type,
                    $"Type '{stageEnumName}' collides with the stage enum generated for entity '{entity.Name}'; rename it.",
                    DomainModelDiagnosticCodes.ReservedGeneratedName);
            }
        }
    }

    private static bool IsReservedExpressionWord(string name) =>
        string.Equals(name, "any", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "all", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "none", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "count", StringComparison.OrdinalIgnoreCase);

    private static void AnalyzeEntity(AnalysisContext context, Entity entity) {
        ReportDuplicateNames(context, entity.Properties, "entity", entity.Name);
        ReportDuplicateNames(context, entity.Actions, "entity", entity.Name);
        ReportDuplicateNames(context, entity.Policies, "entity", entity.Name);
        ReportDuplicateNames(context, entity.Stages, "entity", entity.Name);
        ReportDuplicateNames(context, entity.Navigations, "entity", entity.Name);

        // Same-name actions across stages are supported (resolved by current stage) —
        // do not reject. Policy names are one flat method namespace on the entity.
        ReportPolicyNameClashes(context, entity);
    }

    /// <summary>
    /// Policy methods are unqualified on the entity type, next to the action methods.
    /// A policy named like an action is a collision; a second definition of the same
    /// name is a redefinition. An action policy whose expression is the same instance
    /// as an earlier definition is a use (<c>require</c>), not a definition. Any other
    /// action policy, except a <c>not_</c> use, defines the name.
    /// </summary>
    private static void ReportPolicyNameClashes(AnalysisContext context, Entity entity) {
        var actions = entity.Actions.Concat(entity.Stages.SelectMany(s => s.Actions)).ToList();
        var actionNames = new HashSet<string>(actions.Select(a => a.Name), StringComparer.Ordinal);
        var defined = new Dictionary<string, Policy>(StringComparer.Ordinal);
        void Define(Policy policy) {
            if (actionNames.Contains(policy.Name)) {
                context.ReportError(
                    policy,
                    $"Name collision: an action and a policy are both named '{policy.Name}' on entity '{entity.Name}'.",
                    DomainModelDiagnosticCodes.StructuralDuplicate);
            }
            if (!defined.TryAdd(policy.Name, policy)) {
                context.ReportError(
                    policy,
                    $"Policy '{policy.Name}' is already defined on entity '{entity.Name}'.",
                    DomainModelDiagnosticCodes.StructuralDuplicate);
            }
        }

        foreach (var policy in entity.Policies)
            Define(policy);
        foreach (var stage in entity.Stages)
            foreach (var policy in stage.Policies)
                Define(policy);

        foreach (var action in actions) {
            foreach (var policy in action.Policies) {
                if (policy.Name.StartsWith("not_", StringComparison.Ordinal))
                    continue;
                if (defined.TryGetValue(policy.Name, out var existing)
                    && ReferenceEquals(existing.Expression, policy.Expression))
                    continue;
                Define(policy);
            }
        }
    }

    private static void AnalyzeStage(AnalysisContext context, Stage stage) {
        ReportDuplicateNames(context, stage.Actions, "stage", stage.Name);
        ReportDuplicateNames(context, stage.Policies, "stage", stage.Name);

        // OnEntry / OnExit effects are walked via Children.
        // Future structural rules can go here.
    }

    private static void AnalyzeAction(AnalysisContext context, Action action) {
        ReportDuplicateNames(context, action.Parameters, "action", action.Name);
        ReportDuplicateNames(context, action.Result.Members, "action result", action.Name);
        ReportDuplicateNames(context, action.Policies, "action", action.Name);
    }

    private static void AnalyzeRelationship(AnalysisContext context, Relationship relationship) {
        ReportDuplicateNames(context, relationship.Properties, "relationship", relationship.Name);
        ValidateOwnershipCardinality(context, relationship);
    }

    private static void ValidateOwnershipCardinality(AnalysisContext context, Relationship relationship) {
        if (!relationship.SourceOwnsTarget) return;

        if (relationship.Cardinality is RelationshipCardinality.ManyToOne or RelationshipCardinality.ManyToMany) {
            context.ReportError(
                relationship,
                $"Ownership relationship '{relationship.Name}' must be one-to-one or one-to-many.",
                DomainModelDiagnosticCodes.StructuralOwnership);
        }
    }

    private static void ReportDuplicateNames<TNode>(AnalysisContext context, IEnumerable<TNode> nodes, string ownerType, string ownerName)
        where TNode : DomainMember {
        foreach (var group in nodes.GroupBy(static node => node.Name, StringComparer.Ordinal).Where(static group => group.Count() > 1)) {
            foreach (var duplicate in group.Skip(1)) {
                context.ReportError(
                    duplicate,
                    $"Duplicate member name '{duplicate.Name}' in {ownerType} '{ownerName}'.",
                    DomainModelDiagnosticCodes.StructuralDuplicate);
            }
        }
    }

    private static void ReportDuplicateNames(AnalysisContext context, IEnumerable<InvocationResult.Member> nodes, string ownerType, string ownerName) {
        foreach (var group in nodes.GroupBy(static node => node.Name, StringComparer.Ordinal).Where(static group => group.Count() > 1)) {
            foreach (var duplicate in group.Skip(1)) {
                context.ReportError(
                    duplicate,
                    $"Duplicate member name '{duplicate.Name}' in {ownerType} '{ownerName}'.",
                    DomainModelDiagnosticCodes.StructuralDuplicate);
            }
        }
    }
}