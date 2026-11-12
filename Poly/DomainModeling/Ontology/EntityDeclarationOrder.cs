namespace Poly.DomainModeling.Ontology;

/// <summary>
/// One entity member in declaration order; <see cref="Node"/> is exactly one of
/// <see cref="Property"/>, <see cref="Relationship"/>, <see cref="Stage"/>,
/// <see cref="Action"/> or <see cref="Policy"/>.
/// </summary>
internal readonly record struct OrderedMember(EntityMemberKind Kind, Node Node);

/// <summary>
/// The one declaration-order reader (Q12). Every property and navigation projection, the
/// printer and the compiler read entity-member order here; do not re-sort. Order is never a
/// determinism sort: determinism comes from the model's own order. The Stages and Actions tables in
/// <c>DomainReportExporter</c> and <c>DomainHtmlExporter</c> read <c>entity.Stages</c> and
/// <c>entity.Actions</c> directly: each is a single-kind list, so list order is its declaration order.
/// <para>
/// <see cref="Members"/> merges <see cref="Entity.MemberOrder"/> with the member lists. It walks
/// <c>MemberOrder</c>, takes the first unclaimed match per <c>(Kind, Name)</c> and drops an entry that
/// resolves to no member of that kind, then appends every member still unplaced in list order
/// (properties, navigations, stages, actions, policies). The merge is monotone: adding a member
/// never reorders existing ones, removing one drops only that member, and an empty
/// <c>MemberOrder</c> degrades to per-list order. A property that is removed and added again returns to
/// its old <c>MemberOrder</c> position. The fallback is by design, not fail-closed —
/// builder evolution and programmatic models are legitimate inputs.
/// </para>
/// </summary>
internal static class EntityDeclarationOrder {
    /// <summary>All five member kinds in declaration order.</summary>
    public static IReadOnlyList<OrderedMember> Members(Entity entity) {
        ArgumentNullException.ThrowIfNull(entity);

        var claimed = new HashSet<Node>(ReferenceEqualityComparer.Instance);
        var members = new List<OrderedMember>(
            entity.Properties.Count + entity.Navigations.Count + entity.Stages.Count
            + entity.Actions.Count + entity.Policies.Count);

        // First match per (Kind, Name); an entry that resolves to nothing places nothing.
        foreach (var entry in entity.MemberOrder) {
            var node = Candidates(entity, entry.Kind)
                .FirstOrDefault(n => string.Equals(NameOf(n), entry.Name, StringComparison.Ordinal)
                    && !claimed.Contains(n));
            if (node is null)
                continue;
            claimed.Add(node);
            members.Add(new OrderedMember(entry.Kind, node));
        }

        // Unplaced members follow in list order, so adding or removing one never moves a placed one.
        foreach (var kind in Enum.GetValues<EntityMemberKind>())
            foreach (var node in Candidates(entity, kind))
                if (claimed.Add(node))
                    members.Add(new OrderedMember(kind, node));

        return members;
    }

    /// <summary>The entity's properties in declaration order.</summary>
    public static IReadOnlyList<Property> Properties(Entity entity) =>
        [.. Members(entity).Where(m => m.Kind == EntityMemberKind.Property).Select(m => (Property)m.Node)];

    /// <summary>The entity's navigations in declaration order.</summary>
    public static IReadOnlyList<Relationship> Navigations(Entity entity) =>
        [.. Members(entity).Where(m => m.Kind == EntityMemberKind.Navigation).Select(m => (Relationship)m.Node)];

    private static IEnumerable<Node> Candidates(Entity entity, EntityMemberKind kind) => kind switch {
        EntityMemberKind.Property => entity.Properties,
        EntityMemberKind.Navigation => entity.Navigations,
        EntityMemberKind.Stage => entity.Stages,
        EntityMemberKind.Action => entity.Actions,
        EntityMemberKind.Policy => entity.Policies,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static string NameOf(Node node) => node switch {
        Property p => p.Name,
        Relationship r => r.Name,
        Stage s => s.Name,
        Action a => a.Name,
        Policy p => p.Name,
        _ => throw new InvalidOperationException($"{node.GetType().Name} is not an entity member."),
    };
}