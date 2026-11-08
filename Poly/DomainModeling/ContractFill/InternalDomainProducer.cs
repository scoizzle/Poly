using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;

using Action = Poly.DomainModeling.Ontology.Action;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.ContractFill;

/// <summary>
/// Projects a loaded <see cref="Domain"/> into an <see cref="ImportedContract"/> — the
/// poly-to-poly producer. A parent domain can use another loaded domain the same way it
/// uses Stripe: the contract carries only the published surface, never the child's
/// entities, stages, or relationships (no merge).
///
/// Projection rules (#418, spike #413 decisions 10, 13, 20, 21):
/// <list type="bullet">
/// <item><description>Only <c>public</c> members are exported. Unmarked members are absent.
/// A public action on an internal entity is not projected; analysis reports
/// <c>DMCON003</c> and refuses <c>Lower</c>.</description></item>
/// <item><description>A public entity contributes its name only
/// (<see cref="ImportedContract.Entities"/>): no properties, stages, or identity handle.</description></item>
/// <item><description>A public action becomes one <c>outbound operation</c> endpoint. Actions with
/// the same source name are one endpoint per signature, with a stable
/// <c>Name__PayloadType</c> suffix and the original name kept as
/// <see cref="ContractEndpoint.SourceName"/>. An entity action and same-named stage actions
/// are one action; the group is public when any occurrence is. A v1 endpoint has exactly one
/// payload parameter, so a public zero/multi-parameter action fails closed.</description></item>
/// <item><description>Value types and enums are exported when a public signature reaches them,
/// transitively through exported value-type properties. An internal referenced type is skipped;
/// <c>DMCON002</c> already refuses that domain at <c>Lower</c>.</description></item>
/// <item><description>Public action preconditions (<c>require</c> policies, and a stage action's
/// stage policies) travel on the endpoint. Those constraints are surface meaning and enter the
/// <see cref="ImportedContract.Fingerprint"/>; effects never do.</description></item>
/// </list>
/// </summary>
public sealed class InternalDomainProducer {
    /// <summary>Default version for producer-created contracts.</summary>
    public const string DefaultVersion = "v1";

    /// <summary>
    /// Fills a declared (possibly empty/partial) <c>contract internal</c> with the published
    /// surface of <paramref name="source"/> while preserving the hand-authored body. The
    /// declared contract's identity (name, source kind/identifier, version) is kept; only
    /// its <see cref="ImportedContract.Types"/>, <see cref="ImportedContract.Enums"/>,
    /// <see cref="ImportedContract.Entities"/>, and <see cref="ImportedContract.Endpoints"/>
    /// are grown. Hand-authored members win by name — the producer fills gaps, it never
    /// duplicates an authored entry. Duplicate names that result from a genuine clash (e.g. a
    /// parent value type with the same name) are left for <c>ContractIntegrationAnalyzer</c>
    /// to reject, per the shared clash/leak rules.
    /// </summary>
    public ImportedContract Fill(ImportedContract declared, Domain source) {
        ArgumentNullException.ThrowIfNull(declared);
        var produced = Produce(source);

        return declared with {
            Types = MergeUniqueBy(declared.Types, produced.Types, static t => t.Name),
            Enums = MergeUniqueBy(declared.Enums, produced.Enums, static e => e.Name),
            Entities = MergeUniqueBy(declared.Entities, produced.Entities, static n => n),
            Endpoints = MergeUniqueBy(declared.Endpoints, produced.Endpoints, static e => e.Name)
        };
    }

    private static IReadOnlyList<T> MergeUniqueBy<T>(
        IReadOnlyList<T> declared,
        IReadOnlyList<T> produced,
        Func<T, string> nameOf) {
        var names = declared.Select(nameOf).ToHashSet(StringComparer.Ordinal);
        return [.. declared, .. produced.Where(p => names.Add(nameOf(p)))];
    }

    public ImportedContract Produce(Domain source) {
        ArgumentNullException.ThrowIfNull(source);

        var endpoints = ProjectEndpoints(source);
        var (types, enums) = CollectSurfaceTypes(source, endpoints);
        var entities = source.Types.OfType<Entity>()
            .Where(e => e.Visibility == Visibility.Public)
            .Select(e => e.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        return new ImportedContract(
            ContractNameFrom(source.Name),
            ContractSourceKind.InternalDomain,
            source.Name,
            DefaultVersion,
            endpoints) {
            Types = types,
            Enums = enums,
            Entities = entities
        };
    }

    private static string ContractNameFrom(string domainName) {
        if (string.IsNullOrWhiteSpace(domainName))
            throw new ArgumentException("Source domain name must be non-empty.", nameof(domainName));
        return char.ToUpperInvariant(domainName[0]) + domainName[1..];
    }

    private static IReadOnlyList<ContractEndpoint> ProjectEndpoints(Domain source) {
        var endpoints = new List<ContractEndpoint>();
        var printer = new DomainDslPrinter();
        foreach (var entity in source.Types.OfType<Entity>()) {
            if (entity.Visibility != Visibility.Public)
                continue;

            var groups = ActionGroups(entity)
                .Where(group => group.Any(o => o.Action.Visibility == Visibility.Public))
                .ToList();
            var exportedNameCounts = groups
                .GroupBy(group => group[0].Action.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

            foreach (var group in groups) {
                var publicOccurrences = group
                    .Where(o => o.Action.Visibility == Visibility.Public)
                    .ToList();
                var representative = group.FirstOrDefault(o => o.Stage is null)?.Action
                    ?? publicOccurrences[0].Action;

                if (representative.Parameters.Count != 1) {
                    throw new InvalidOperationException(
                        $"Cannot project action '{representative.Name}' on entity '{entity.Name}' " +
                        $"as a v1 endpoint: it has {representative.Parameters.Count} parameters. " +
                        "v1 projects only entry-shaped actions with exactly one request payload; " +
                        "instance-targeted bind is a later slice.");
                }

                var payloadType = representative.Parameters[0].Type.TypeName;
                var name = exportedNameCounts[representative.Name] == 1
                    ? representative.Name
                    : $"{representative.Name}__{payloadType}";

                var preconditions = publicOccurrences
                    .SelectMany(o => ExportPreconditions(o.Action, entity, o.Stage))
                    .GroupBy(p => (p.Name, Expression: printer.PrintExpressionText(p.Expression)))
                    .Select(g => g.First())
                    .OrderBy(p => p.Name, StringComparer.Ordinal)
                    .ToList();

                endpoints.Add(new ContractEndpoint(
                    name,
                    ContractEndpointKind.Operation,
                    ContractEndpointDirection.Outbound,
                    new DomainTypeReference(payloadType)) {
                    SourceName = representative.Name,
                    Preconditions = preconditions
                });
            }
        }

        FailOnDuplicateEndpointNames(endpoints);
        return endpoints;
    }

    /// <summary>
    /// Groups one entity's actions by <c>(name, parameter signature)</c>. An entity action and
    /// its same-named stage shells share a key (the shell copies the parameters), so they are
    /// one action; a stale shell with different parameters behaves like an overload.
    /// </summary>
    private static IEnumerable<List<ActionOccurrence>> ActionGroups(Entity entity) {
        var order = new List<(string Name, string Signature)>();
        var groups = new Dictionary<(string Name, string Signature), List<ActionOccurrence>>();

        void Add(Action action, Stage? stage) {
            var key = (action.Name, Signature(action));
            if (!groups.TryGetValue(key, out var occurrences)) {
                occurrences = [];
                groups[key] = occurrences;
                order.Add(key);
            }
            occurrences.Add(new ActionOccurrence(action, stage));
        }

        foreach (var action in entity.Actions)
            Add(action, stage: null);
        foreach (var stage in entity.Stages) {
            foreach (var action in stage.Actions)
                Add(action, stage);
        }

        return order.Select(key => groups[key]);
    }

    private static string Signature(Action action) =>
        string.Join(",", action.Parameters.Select(p => p.Type.TypeName));

    /// <summary>
    /// The endpoint's public precondition set. <see cref="EffectInvariantAnalyzer.CollectPreconditions"/>
    /// is the effect-invariant abstract environment, and it deliberately drops <c>not_*</c>
    /// policies; a <c>require not Positive</c> gate is still runtime surface, so the export set
    /// adds the action's negated policies back. Dedupe is by name plus canonical printed
    /// expression (never record equality: a node's <c>Id</c> participates, and each
    /// <c>require not</c> allocates a fresh <c>Not</c>).
    /// </summary>
    private static IEnumerable<Policy> ExportPreconditions(Action action, Entity entity, Stage? stage) =>
        EffectInvariantAnalyzer.CollectPreconditions(action, entity, stage)
            .Concat(action.Policies.Where(p => p.Name.StartsWith("not_", StringComparison.Ordinal)));

    /// <summary>
    /// Walks the surface from the endpoint payloads through exported value-type properties.
    /// A value type is exported only when it is public; an enum has no visibility mark, so it
    /// is exported whenever the surface reaches it. Primitives and contract ACL types stay
    /// name-only references (the ACL type belongs to its contract).
    /// </summary>
    private static (IReadOnlyList<ValueType> Types, IReadOnlyList<EnumType> Enums) CollectSurfaceTypes(
        Domain source,
        IReadOnlyList<ContractEndpoint> endpoints) {
        var publicValueTypes = source.Types.OfType<ValueType>()
            .Where(t => t.Visibility == Visibility.Public)
            .ToDictionary(t => t.Name, StringComparer.Ordinal);
        var enumTypes = source.Types.OfType<EnumType>()
            .ToDictionary(t => t.Name, StringComparer.Ordinal);

        var types = new Dictionary<string, ValueType>(StringComparer.Ordinal);
        var enums = new Dictionary<string, EnumType>(StringComparer.Ordinal);
        var pending = new Queue<string>(endpoints.Select(e => e.PayloadType.TypeName));
        var seen = new HashSet<string>(StringComparer.Ordinal);

        while (pending.Count > 0) {
            var name = pending.Dequeue();
            if (!seen.Add(name))
                continue;

            if (publicValueTypes.TryGetValue(name, out var valueType)) {
                types[name] = valueType;
                foreach (var property in valueType.Properties)
                    pending.Enqueue(property.Type.TypeName);
            }
            else if (enumTypes.TryGetValue(name, out var enumType)) {
                enums[name] = enumType;
            }
        }

        return (
            types.Values.OrderBy(t => t.Name, StringComparer.Ordinal).ToList(),
            enums.Values.OrderBy(e => e.Name, StringComparer.Ordinal).ToList());
    }

    private static void FailOnDuplicateEndpointNames(IReadOnlyList<ContractEndpoint> endpoints) {
        var duplicate = endpoints
            .GroupBy(e => e.Name, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) {
            throw new InvalidOperationException(
                $"Cannot project the public surface: endpoint name '{duplicate.Key}' is produced " +
                "by more than one public action. Endpoints are bare names; only same-entity " +
                "overload signatures get a suffix.");
        }
    }

    private sealed record ActionOccurrence(Action Action, Stage? Stage);
}