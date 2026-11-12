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

        ReportKeyDesignations(context, domain);
    }

    /// <summary>
    /// #405: an entity's <see cref="Entity.KeyProperties"/> is a designation over its own members, so
    /// names are scoped to that entity (two entities may each key on <c>Code</c>). A malformed
    /// designation fails closed: a repeated member is <see cref="DomainModelDiagnosticCodes.StructuralDuplicate"/>,
    /// a navigation-key cycle is <see cref="DomainModelDiagnosticCodes.StructuralCycle"/>, and any other
    /// violation of the admission rules is <see cref="DomainModelDiagnosticCodes.InvalidKeyDesignation"/>.
    /// </summary>
    private static void ReportKeyDesignations(AnalysisContext context, Domain domain) {
        var entities = domain.Types.OfType<Entity>()
            .GroupBy(static e => e.Name, StringComparer.Ordinal)
            .ToDictionary(static g => g.Key, static g => g.First(), StringComparer.Ordinal);

        foreach (var entity in domain.Types.OfType<Entity>()) {
            if (KeyOf(entity).Count == 0)
                continue;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            // Reused to mean "assigned by any effect", not only constructor ordering.
            var assigned = EntityStructureAnalyzer.ComputeAssignOnlyPropertyNames(entity);
            foreach (var member in KeyOf(entity)) {
                if (string.IsNullOrWhiteSpace(member)) {
                    ReportInvalidKey(context, entity, $"Entity '{entity.Name}' key names a blank member.");
                    continue;
                }
                if (!seen.Add(member)) {
                    context.ReportError(
                        entity,
                        $"Duplicate member name '{member}' in key of entity '{entity.Name}'.",
                        DomainModelDiagnosticCodes.StructuralDuplicate);
                    continue;
                }

                var property = entity.Properties.FirstOrDefault(p => string.Equals(p.Name, member, StringComparison.Ordinal));
                if (property is not null) {
                    if (!EntityStructureAnalyzer.IsRequired(property))
                        ReportInvalidKey(context, entity,
                            $"Entity '{entity.Name}' key member '{member}' is optional; a key member must be required.");
                    else if (assigned.Contains(member))
                        ReportInvalidKey(context, entity,
                            $"Entity '{entity.Name}' key member '{member}' is assigned by an effect; a key member must be immutable.");
                    continue;
                }

                var nav = entity.Navigations.FirstOrDefault(n => string.Equals(n.Name, member, StringComparison.Ordinal));
                if (nav is null) {
                    ReportInvalidKey(context, entity,
                        $"Entity '{entity.Name}' key names '{member}', which is not a property or navigation of the entity.");
                    continue;
                }
                if (nav.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany) {
                    ReportInvalidKey(context, entity,
                        $"Entity '{entity.Name}' key member '{member}' is a collection navigation; a key navigation must be singular.");
                    continue;
                }
                if (!entities.TryGetValue(nav.Target.TypeName, out var target)) {
                    ReportInvalidKey(context, entity,
                        $"Entity '{entity.Name}' key navigation '{member}' targets '{nav.Target.TypeName}', which is not an entity.");
                    continue;
                }
                if (KeyOf(target).Count == 0)
                    ReportInvalidKey(context, entity,
                        $"Entity '{entity.Name}' key navigation '{member}' targets '{target.Name}', which has no key.");
            }

            if (ReachesViaKeyNavigation(entity, entity, entities, new HashSet<string>(StringComparer.Ordinal))) {
                context.ReportError(
                    entity,
                    $"Entity '{entity.Name}' key resolves through singular navigations back to itself; a key cannot be cyclic.",
                    DomainModelDiagnosticCodes.StructuralCycle);
            }
        }
    }

    /// <summary>Null-tolerant read of the key designation, matching <see cref="EntityStructureMetadata.Id"/>.</summary>
    private static IReadOnlyList<string> KeyOf(Entity entity) => entity.KeyProperties ?? [];

    private static void ReportInvalidKey(AnalysisContext context, Entity entity, string message) =>
        context.ReportError(entity, message, DomainModelDiagnosticCodes.InvalidKeyDesignation);

    /// <summary>True when following key-member singular navigations from <paramref name="from"/> reaches <paramref name="goal"/>.</summary>
    private static bool ReachesViaKeyNavigation(
        Entity goal, Entity from, IReadOnlyDictionary<string, Entity> entities, HashSet<string> visited) {
        foreach (var member in KeyOf(from)) {
            if (from.Properties.Any(p => string.Equals(p.Name, member, StringComparison.Ordinal)))
                continue;
            var nav = from.Navigations.FirstOrDefault(n => string.Equals(n.Name, member, StringComparison.Ordinal));
            if (nav is null || nav.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany)
                continue;
            if (!entities.TryGetValue(nav.Target.TypeName, out var target))
                continue;
            if (ReferenceEquals(target, goal))
                return true;
            if (visited.Add(target.Name) && ReachesViaKeyNavigation(goal, target, entities, visited))
                return true;
        }
        return false;
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
    /// N4 + S-01: names lowering emits at module scope must not be user types. The rows come
    /// from <see cref="ReservedNames"/> — <c>DomainResult</c>, <c>ConstraintFailureException</c>
    /// (scaffolding types) and <c>System</c> (the imported namespace) — plus
    /// <c>{Entity}Stage</c> for every entity that has stages. Emit splits by entity tree, so the
    /// collision is a duplicate top-level C# type name (CS0101).
    /// </summary>
    private static void ReportReservedGeneratedNames(AnalysisContext context, Domain domain) {
        foreach (var type in domain.Types) {
            foreach (var row in ReservedNames.All) {
                if (row.Kind is not (ReservedKind.GeneratedType or ReservedKind.Namespace))
                    continue;
                if (!string.Equals(type.Name, row.Name, StringComparison.Ordinal))
                    continue;
                context.ReportError(
                    type,
                    $"Type '{type.Name}' {row.Reason}; rename it.",
                    DomainModelDiagnosticCodes.ReservedGeneratedName);
            }
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

    /// <summary>
    /// S-01 (#528 #519): the members of one entity share one generated C# type, so their
    /// generated identifiers are one namespace. Reports only what the legacy per-list rules do
    /// not: a name two lists both declare (<see cref="DomainModelDiagnosticCodes.StructuralDuplicate"/>,
    /// except action↔policy and policy↔policy, which <see cref="ReportPolicyNameClashes"/> owns),
    /// two names that derive one identifier (<see cref="DomainModelDiagnosticCodes.GeneratedIdentifierCollision"/>),
    /// and an identity the exporter already generates (<see cref="DomainModelDiagnosticCodes.ReservedMemberName"/>).
    /// One node reports once per pass, in that priority order.
    /// </summary>
    private static void ReportMemberScopes(AnalysisContext context, Entity entity) {
        var scope = NameScopes.ForEntity(entity);
        if (scope.Count == 0)
            return;

        // Reference identity: two distinct nodes may hold equal values.
        var reported = new List<DomainMember>();
        bool AlreadyReported(DomainMember node) => reported.Any(r => ReferenceEquals(r, node));

        foreach (var group in scope.GroupBy(static entry => entry.Name, StringComparer.Ordinal)) {
            if (group.Select(static entry => entry.Kind).Distinct().Count() < 2)
                continue;
            if (group.Any(static entry => entry.Kind == MemberKind.Policy)
                && group.Any(static entry => entry.Kind == MemberKind.Action))
                continue;
            var first = group.First();
            foreach (var entry in group.Skip(1)) {
                if (AlreadyReported(entry.Node))
                    continue;
                reported.Add(entry.Node);
                context.ReportError(
                    entry.Node,
                    $"Name collision: {DescribeKind(entry.Kind)} and {DescribeKind(first.Kind)} are both " +
                    $"named '{entry.Name}' on entity '{entity.Name}'.",
                    DomainModelDiagnosticCodes.StructuralDuplicate);
            }
        }

        foreach (var group in scope.GroupBy(static entry => entry.MemberIdentity, StringComparer.Ordinal))
            ReportIdentityCollision(context, entity, group, "C# member",
                static entry => entry.MemberIdentity, reported, AlreadyReported);
        foreach (var group in scope
            .Where(static entry => entry.ParameterIdentity is not null)
            .GroupBy(static entry => entry.ParameterIdentity!, StringComparer.Ordinal))
            ReportIdentityCollision(context, entity, group, "constructor parameter",
                static entry => entry.ParameterIdentity!, reported, AlreadyReported);

        foreach (var entry in scope) {
            if (AlreadyReported(entry.Node))
                continue;
            if (!ReservedNames.TryReserved(entry.MemberIdentity, entity, out var row))
                continue;
            reported.Add(entry.Node);
            context.ReportError(
                entry.Node,
                row.Kind == ReservedKind.Language
                    ? $"Member '{entry.MemberIdentity}' on entity '{entity.Name}' would take the enclosing " +
                      "type's name (CS0542); rename it."
                    : $"Member '{entry.MemberIdentity}' on entity '{entity.Name}' collides with the C# member " +
                      $"'{row.Name}' that Emit generates; rename it.",
                DomainModelDiagnosticCodes.ReservedMemberName);
        }
    }

    /// <summary>DMSTR008: two distinct member names derive one generated identifier.</summary>
    private static void ReportIdentityCollision(
        AnalysisContext context, Entity entity, IEnumerable<ScopeEntry> group, string surface,
        Func<ScopeEntry, string> identityOf, List<DomainMember> reported,
        Func<DomainMember, bool> alreadyReported) {
        var entries = group.ToList();
        if (entries.Select(static entry => entry.Name).Distinct(StringComparer.Ordinal).Count() < 2)
            return;
        var first = entries[0];
        var identity = identityOf(first);
        foreach (var entry in entries.Skip(1)) {
            if (alreadyReported(entry.Node))
                continue;
            reported.Add(entry.Node);
            context.ReportError(
                entry.Node,
                $"Members '{first.Name}' and '{entry.Name}' on entity '{entity.Name}' generate the same " +
                $"{surface} '{identity}'; rename one.",
                DomainModelDiagnosticCodes.GeneratedIdentifierCollision);
        }
    }

    private static string DescribeKind(MemberKind kind) => kind switch {
        MemberKind.Property => "a property",
        MemberKind.Navigation => "a navigation",
        MemberKind.Action => "an action",
        _ => "a policy",
    };

    private static void AnalyzeEntity(AnalysisContext context, Entity entity) {
        ReportDuplicateNames(context, entity.Properties, "entity", entity.Name);
        ReportDuplicateNames(context, entity.Actions, "entity", entity.Name);
        ReportDuplicateNames(context, entity.Policies, "entity", entity.Name);
        ReportDuplicateNames(context, entity.Stages, "entity", entity.Name);
        ReportDuplicateNames(context, entity.Navigations, "entity", entity.Name);

        // Same-name actions across stages are supported (resolved by current stage) —
        // do not reject. Policy names are one flat method namespace on the entity.
        ReportPolicyNameClashes(context, entity);

        // S-01: one generated C# namespace per entity — cross-list names, one identifier, reservations.
        ReportMemberScopes(context, entity);
    }

    /// <summary>
    /// Policy methods are unqualified on the entity type, next to the action methods.
    /// A policy named like an action is a collision; a second definition of the same
    /// name is a redefinition. A stage or action policy whose expression is the same
    /// instance as an earlier definition is a use (<c>require</c>), not a definition.
    /// A <c>not_</c> policy is that use negated. Any other policy defines the name.
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

        bool IsUse(Policy policy) =>
            policy.Name.StartsWith("not_", StringComparison.Ordinal)
            || (defined.TryGetValue(policy.Name, out var existing)
                && ReferenceEquals(existing.Expression, policy.Expression));

        foreach (var policy in entity.Policies)
            Define(policy);
        foreach (var stage in entity.Stages)
            foreach (var policy in stage.Policies)
                if (!IsUse(policy))
                    Define(policy);
        foreach (var action in actions)
            foreach (var policy in action.Policies)
                if (!IsUse(policy))
                    Define(policy);
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