using Poly.Ast;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Ontology;

using EnumType = Poly.DomainModeling.Ontology.EnumType;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Queries;

internal enum ElementKind {
    Domain,
    Entity,
    Property,
    Navigation,
    Enum,
    EnumMember,
    ValueType,
    Policy,
    Stage,
    EntityAction,
    StageAction,
    Function,
}

internal enum NotFoundReason {
    Unknown,
    NotAddressable,
}

/// <summary>The outcome of resolving an <see cref="ElementPath"/>: a ref, a not-found, or an ambiguity.</summary>
internal abstract record ElementResolution;

/// <summary>
/// One resolved element. <see cref="Canonical"/> is the dotted path that resolves back to it
/// (<c>Entity.Stage.Action</c> for a stage action); <see cref="AnalysisPath"/> is the
/// <c>Domain/Entity[/Stage]/Member</c> path findings carry. <see cref="Node"/> is null only for an enum member.
/// </summary>
internal sealed record ElementRef(
    ElementKind Kind,
    string? Entity,
    string? Stage,
    string Name,
    string Canonical,
    string AnalysisPath,
    Node? Node) : ElementResolution;

/// <summary>No element: <see cref="NotFoundReason.Unknown"/> or a shape the grammar does not address. <see cref="Partial"/> is the deepest element that did resolve.</summary>
internal sealed record ElementNotFound(
    NotFoundReason Reason,
    IReadOnlyList<string> Candidates,
    ElementRef? Partial,
    string Message) : ElementResolution;

/// <summary>More than one element answers the path; every one is in <see cref="Matches"/>.</summary>
internal sealed record ElementAmbiguous(IReadOnlyList<ElementRef> Matches) : ElementResolution;

/// <summary>
/// Resolves an <see cref="ElementPath"/> against a <see cref="Domain"/>. Pure: no analysis, no side effects.
/// Descends by the kind of the previous segment, case-sensitively (Ordinal). A leading domain name is read
/// both as written and stripped; a name that resolves only one way takes that way, both ways is ambiguous.
/// </summary>
internal static class ElementPathResolver {
    private const int MaxCandidates = 5;
    private const int MaxScoredSegment = 64;

    public static ElementResolution Resolve(Domain domain, ElementPath path) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(path);

        var segments = path.Segments;
        if (segments.Count == 0)
            return new ElementNotFound(NotFoundReason.Unknown, [], null, "Element path is empty.");
        if (segments.Count == 1 || !string.Equals(segments[0], domain.Name, StringComparison.Ordinal))
            return Descend(domain, segments, exact: false);

        // A leading domain name makes the path the exact analysis-path form: no bare shorthand.
        var asWritten = Descend(domain, segments, exact: false);
        var stripped = Descend(domain, [.. segments.Skip(1)], exact: true);
        return Merge(asWritten, stripped);
    }

    private static ElementResolution Merge(ElementResolution asWritten, ElementResolution stripped) {
        var writtenResolves = asWritten is ElementRef or ElementAmbiguous;
        var strippedResolves = stripped is ElementRef or ElementAmbiguous;
        if (writtenResolves && !strippedResolves)
            return asWritten;
        if (strippedResolves && !writtenResolves)
            return stripped;
        if (!writtenResolves)
            return Depth(stripped) >= Depth(asWritten) ? stripped : asWritten;

        var matches = Matches(asWritten).Concat(Matches(stripped))
            .DistinctBy(static m => m.AnalysisPath, StringComparer.Ordinal)
            .ToList();
        return matches.Count == 1 ? matches[0] : new ElementAmbiguous(matches);
    }

    private static IEnumerable<ElementRef> Matches(ElementResolution resolution) => resolution switch {
        ElementRef r => [r],
        ElementAmbiguous a => a.Matches,
        _ => [],
    };

    private static int Depth(ElementResolution resolution) =>
        resolution is ElementNotFound { Partial: { Kind: not ElementKind.Domain } partial } ? partial.Canonical.Count(static c => c == '.') + 1 : 0;

    private static ElementResolution Descend(Domain domain, IReadOnlyList<string> segments, bool exact) {
        var current = TopLevel(domain, segments[0]);
        for (var i = 1; i < segments.Count; i++) {
            if (current is not ElementRef parent)
                return current;
            current = Child(domain, parent, segments[i], exact);
        }

        return current;
    }

    // ── One segment ─────────────────────────────────────────────

    private static ElementResolution TopLevel(Domain domain, string name) {
        var matches = new List<ElementRef>();
        foreach (var type in domain.Types.Where(t => string.Equals(t.Name, name, StringComparison.Ordinal))) {
            switch (type) {
                case Entity e:
                    matches.Add(Make(domain, ElementKind.Entity, null, null, e.Name, e.Name, e));
                    break;
                case EnumType e:
                    matches.Add(Make(domain, ElementKind.Enum, null, null, e.Name, e.Name, e));
                    break;
                case ValueType v:
                    matches.Add(Make(domain, ElementKind.ValueType, null, null, v.Name, v.Name, v));
                    break;
                default:
                    return new ElementNotFound(NotFoundReason.NotAddressable, [], null,
                        $"'{name}' is a {type.GetType().Name} and cannot be inspected by path.");
            }
        }

        foreach (var function in domain.Functions.Where(f => string.Equals(f.Name, name, StringComparison.Ordinal)))
            matches.Add(Make(domain, ElementKind.Function, null, null, function.Name, function.Name, function));

        if (matches.Count == 0 && string.Equals(name, domain.Name, StringComparison.Ordinal))
            return Make(domain, ElementKind.Domain, null, null, domain.Name, domain.Name, domain);
        if (matches.Count == 1)
            return matches[0];
        if (matches.Count > 1)
            return new ElementAmbiguous(matches);

        var pool = domain.Types.Where(static t => t is Entity or EnumType or ValueType).Select(static t => t.Name)
            .Concat(domain.Functions.Select(static f => f.Name))
            .Select(static n => (Label: n, Path: n))
            .ToList();
        var candidates = Candidates(name, pool, []);
        return new ElementNotFound(NotFoundReason.Unknown, candidates, null,
            $"'{name}' not found: no entity, enum, value type or function has that name.{DidYouMean(candidates)}");
    }

    // ── Descending one segment below a resolved element ─────────

    private static ElementResolution Child(Domain domain, ElementRef parent, string name, bool exact) => parent.Kind switch {
        ElementKind.Entity => ChildOfEntity(domain, (Entity)parent.Node!, parent, name, exact),
        ElementKind.Stage => ChildOfStage(domain, DeclaringEntity(domain, parent), (Stage)parent.Node!, parent, name),
        ElementKind.Enum => ChildOfEnum(domain, (EnumType)parent.Node!, parent, name),
        ElementKind.EntityAction or ElementKind.StageAction => ChildOfAction(domain, DeclaringEntity(domain, parent), (Ontology.Action)parent.Node!, parent, name),
        _ => NotAddressable(parent, name),
    };

    private static Entity DeclaringEntity(Domain domain, ElementRef stage) =>
        domain.Types.OfType<Entity>().First(e => string.Equals(e.Name, stage.Entity, StringComparison.Ordinal));

    private static ElementNotFound NotAddressable(ElementRef parent, string name) =>
        new(NotFoundReason.NotAddressable, [], parent,
            $"'{parent.Canonical}.{name}' is not addressable: nothing below a {Describe(parent.Kind)} ('{parent.Canonical}') can be selected.");

    private static ElementResolution ChildOfEntity(Domain domain, Entity entity, ElementRef parent, string name, bool exact) {
        var members = new List<ElementRef>();
        foreach (var property in entity.Properties.Where(p => Is(p.Name, name)))
            members.Add(Make(domain, ElementKind.Property, entity.Name, null, name, $"{entity.Name}.{name}", property));
        foreach (var navigation in entity.Navigations.Where(n => Is(n.Name, name)))
            members.Add(Make(domain, ElementKind.Navigation, entity.Name, null, name, $"{entity.Name}.{name}", navigation));
        foreach (var action in entity.Actions.Where(a => Is(a.Name, name)))
            members.Add(Make(domain, ElementKind.EntityAction, entity.Name, null, name, $"{entity.Name}.{name}", action));
        foreach (var stage in entity.Stages.Where(s => Is(s.Name, name)))
            members.Add(Make(domain, ElementKind.Stage, entity.Name, stage.Name, name, $"{entity.Name}.{name}", stage));
        foreach (var policy in NameScopes.PolicyDefinitions(entity).Where(p => Is(p.Name, name)))
            members.Add(PolicyRef(domain, entity, policy));

        // An entity-level action and a same-named stage action are two elements: the bare path is
        // ambiguous. The exact (domain-prefixed) form selects only the entity-level one.
        if (!exact && members.Any(static m => m.Kind == ElementKind.EntityAction)) {
            members.AddRange(entity.Stages
                .SelectMany(stage => stage.Actions.Where(a => Is(a.Name, name)).Select(action => (stage, action)))
                .Select(pair => Make(domain, ElementKind.StageAction, entity.Name, pair.stage.Name, name,
                    $"{entity.Name}.{pair.stage.Name}.{name}", pair.action)));
        }

        if (members.Count == 1)
            return members[0];
        if (members.Count > 1)
            return new ElementAmbiguous(members);

        var stageActions = entity.Stages
            .SelectMany(stage => stage.Actions.Where(a => Is(a.Name, name)).Select(action => (stage, action)))
            .Select(pair => Make(domain, ElementKind.StageAction, entity.Name, pair.stage.Name, name,
                $"{entity.Name}.{pair.stage.Name}.{name}", pair.action))
            .ToList();
        if (stageActions.Count == 1)
            return stageActions[0];
        if (stageActions.Count > 1)
            return new ElementAmbiguous(stageActions);

        var owned = EntityMemberNames(entity).Select(n => (Label: n, Path: $"{entity.Name}.{n}")).ToList();
        var elsewhere = entity.Stages
            .SelectMany(stage => stage.Actions.Select(a => (Label: a.Name, Path: $"{entity.Name}.{stage.Name}.{a.Name}")))
            .ToList();
        var candidates = Candidates(name, owned, elsewhere);
        return new ElementNotFound(NotFoundReason.Unknown, candidates, parent,
            $"'{name}' not found on entity '{entity.Name}' (tried property, navigation, stage, action, policy).{DidYouMean(candidates)}");
    }

    private static ElementResolution ChildOfStage(Domain domain, Entity entity, Stage stage, ElementRef parent, string name) {
        var definitions = NameScopes.PolicyDefinitions(entity).ToList();
        var members = new List<ElementRef>();
        foreach (var action in stage.Actions.Where(a => Is(a.Name, name)))
            members.Add(Make(domain, ElementKind.StageAction, entity.Name, stage.Name, name,
                $"{entity.Name}.{stage.Name}.{name}", action));
        foreach (var policy in stage.Policies.Where(p => Is(p.Name, name) && definitions.Any(d => ReferenceEquals(d, p))))
            members.Add(PolicyRef(domain, entity, policy));

        if (members.Count == 1)
            return members[0];
        if (members.Count > 1)
            return new ElementAmbiguous(members);

        var owned = stage.Actions.Select(a => a.Name)
            .Concat(stage.Policies.Where(p => definitions.Any(d => ReferenceEquals(d, p))).Select(p => p.Name))
            .Select(n => (Label: n, Path: $"{entity.Name}.{stage.Name}.{n}"))
            .ToList();
        var candidates = Candidates(name, owned, []);
        return new ElementNotFound(NotFoundReason.Unknown, candidates, parent,
            $"'{name}' not found on stage '{entity.Name}.{stage.Name}' (tried action, policy).{DidYouMean(candidates)}");
    }

    private static ElementResolution ChildOfAction(Domain domain, Entity entity, Ontology.Action action, ElementRef parent, string name) {
        var definitions = NameScopes.PolicyDefinitions(entity).ToList();
        var matches = action.Policies
            .Where(p => Is(p.Name, name) && definitions.Any(d => ReferenceEquals(d, p)))
            .Select(p => PolicyRef(domain, entity, p))
            .ToList();
        return matches.Count switch {
            0 => NotAddressable(parent, name),
            1 => matches[0],
            _ => new ElementAmbiguous(matches),
        };
    }

    private static ElementResolution ChildOfEnum(Domain domain, EnumType enumType, ElementRef parent, string name) {
        if (enumType.MemberNames.Contains(name, StringComparer.Ordinal)) {
            return new ElementRef(ElementKind.EnumMember, null, null, name, $"{enumType.Name}.{name}",
                $"{domain.Name}/{enumType.Name}/{name}", null);
        }

        var owned = enumType.MemberNames.Select(n => (Label: n, Path: $"{enumType.Name}.{n}")).ToList();
        var candidates = Candidates(name, owned, []);
        return new ElementNotFound(NotFoundReason.Unknown, candidates, parent,
            $"Member '{name}' not found on enum '{enumType.Name}' (members: {string.Join(", ", enumType.MemberNames)}).{DidYouMean(candidates)}");
    }

    // ── Helpers ─────────────────────────────────────────────────

    private static bool Is(string left, string right) => string.Equals(left, right, StringComparison.Ordinal);

    private static IEnumerable<string> EntityMemberNames(Entity entity) =>
        entity.Properties.Select(static p => p.Name)
            .Concat(entity.Navigations.Select(static n => n.Name))
            .Concat(entity.Actions.Select(static a => a.Name))
            .Concat(entity.Stages.Select(static s => s.Name))
            .Concat(NameScopes.PolicyDefinitions(entity).Select(static p => p.Name))
            .Distinct(StringComparer.Ordinal);

    /// <summary>
    /// A policy's canonical path follows where it is defined: <c>Entity.P</c> on the entity or an action,
    /// <c>Entity.Stage.P</c> on a stage. A policy defined on an action is addressed bare.
    /// </summary>
    private static ElementRef PolicyRef(Domain domain, Entity entity, Policy policy) {
        var stage = entity.Stages.FirstOrDefault(s => s.Policies.Any(p => ReferenceEquals(p, policy)));
        var onEntity = entity.Policies.Any(p => ReferenceEquals(p, policy));
        return !onEntity && stage is not null
            ? Make(domain, ElementKind.Policy, entity.Name, stage.Name, policy.Name, $"{entity.Name}.{stage.Name}.{policy.Name}", policy)
            : Make(domain, ElementKind.Policy, entity.Name, null, policy.Name, $"{entity.Name}.{policy.Name}", policy);
    }

    private static ElementRef Make(Domain domain, ElementKind kind, string? entity, string? stage, string name, string canonical, Node node) =>
        new(kind, entity, stage, name, canonical, DomainElementPath.Resolve(domain, node), node);

    private static string Describe(ElementKind kind) => kind switch {
        ElementKind.ValueType => "value type",
        ElementKind.EnumMember => "enum member",
        ElementKind.EntityAction => "entity action",
        ElementKind.StageAction => "stage action",
        _ => kind.ToString().ToLowerInvariant(),
    };

    private static string DidYouMean(IReadOnlyList<string> candidates) =>
        candidates.Count == 0 ? "" : $" Did you mean: {string.Join(", ", candidates)}?";

    /// <summary>
    /// At most five, deterministic: same-owner members before the rest, then case-insensitive equal, then a
    /// prefix, then an edit distance of at most max(2, length / 3); ordered by distance and then ordinal.
    /// A segment over 64 characters is not edit-distance scored.
    /// </summary>
    private static List<string> Candidates(
        string name,
        IReadOnlyList<(string Label, string Path)> owned,
        IReadOnlyList<(string Label, string Path)> elsewhere) {
        var scored = new List<(int Pool, int Tier, int Distance, string Path)>();

        void Score(int pool, IReadOnlyList<(string Label, string Path)> source) {
            foreach (var (label, path) in source) {
                if (string.Equals(label, name, StringComparison.OrdinalIgnoreCase))
                    scored.Add((pool, 0, 0, path));
                else if (label.StartsWith(name, StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                    scored.Add((pool, 1, 0, path));
                else if (name.Length <= MaxScoredSegment) {
                    var distance = EditDistance(name, label);
                    if (distance <= Math.Max(2, name.Length / 3))
                        scored.Add((pool, 2, distance, path));
                }
            }
        }

        Score(0, owned);
        Score(1, elsewhere);
        return [.. scored
            .OrderBy(static s => s.Pool)
            .ThenBy(static s => s.Tier)
            .ThenBy(static s => s.Distance)
            .ThenBy(static s => s.Path, StringComparer.Ordinal)
            .Select(static s => s.Path)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxCandidates)];
    }

    private static int EditDistance(string left, string right) {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var j = 0; j <= right.Length; j++)
            previous[j] = j;
        for (var i = 1; i <= left.Length; i++) {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++) {
                var cost = char.ToLowerInvariant(left[i - 1]) == char.ToLowerInvariant(right[j - 1]) ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}