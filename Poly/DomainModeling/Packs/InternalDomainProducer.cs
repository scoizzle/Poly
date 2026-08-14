namespace Poly.DomainModeling.Packs;

/// <summary>
/// Projects a loaded <see cref="Domain"/> into an <see cref="ImportedContract"/> — the
/// poly-to-poly producer. A parent domain can use another loaded domain the same way it
/// uses Stripe: the contract carries only the published surface, never the child's
/// entities, stages, or relationships (no merge).
///
/// v1 projection rules:
/// <list type="bullet">
/// <item><description>Contract name is derived from the source domain name (first letter
/// uppercased: <c>billing</c> → <c>Billing</c>); <see cref="ImportedContract.SourceIdentifier"/>
/// is the source domain name; version defaults to <c>v1</c> (the session slice that fills a
/// hand-authored <c>contract internal</c> preserves the hand-authored version).</description></item>
/// <item><description>Value types on <see cref="Domain.Types"/> are copied as the contract's ACL
/// <see cref="ImportedContract.Types"/>. Entities are never projected.</description></item>
/// <item><description>All actions (entity-level <see cref="Entity.Actions"/> and
/// <see cref="Stage.Actions"/>) become singleton <c>outbound operation</c> endpoints whose
/// payload is the action's single request parameter type. Instance-targeted bind is a later
/// slice — v1 expresses only what <see cref="ContractBinding"/> can already carry. An action
/// without exactly one parameter cannot be expressed as a v1 singleton payload, so it fails
/// closed rather than emitting a lying contract.</description></item>
/// </list>
/// </summary>
public sealed class InternalDomainProducer : IContractProducer {
    /// <summary>Default version for producer-created contracts.</summary>
    public const string DefaultVersion = "v1";

    /// <summary>
    /// Fills a declared (possibly empty/partial) <c>contract internal</c> with the published
    /// surface of <paramref name="source"/> while preserving the hand-authored body. The
    /// declared contract's identity (name, source kind/identifier, version) is kept; only
    /// its <see cref="ImportedContract.Types"/> and <see cref="ImportedContract.Endpoints"/>
    /// are grown. Hand-authored members win by name — the producer fills gaps, it never
    /// duplicates an authored type or endpoint. Duplicate names that result from a genuine
    /// clash (e.g. a parent value type with the same name) are left for
    /// <c>ContractIntegrationAnalyzer</c> to reject, per the shared clash/leak rules.
    /// </summary>
    public ImportedContract Fill(ImportedContract declared, Domain source) {
        ArgumentNullException.ThrowIfNull(declared);
        var produced = Produce(source);

        return declared with {
            Types = MergeUniqueBy(declared.Types, produced.Types, static t => t.Name),
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

        var types = source.Types.OfType<ValueType>().ToList();

        var endpoints = source.Types.OfType<Entity>()
            .SelectMany(e => e.Actions.Concat(e.Stages.SelectMany(s => s.Actions)))
            .Select(ProjectSingletonOperation)
            .ToList();
        FailOnDuplicateEndpointNames(endpoints);

        return new ImportedContract(
            ContractNameFrom(source.Name),
            ContractSourceKind.InternalDomain,
            source.Name,
            DefaultVersion,
            endpoints) {
            Types = types
        };
    }

    private static string ContractNameFrom(string domainName) {
        if (string.IsNullOrWhiteSpace(domainName))
            throw new ArgumentException("Source domain name must be non-empty.", nameof(domainName));
        return char.ToUpperInvariant(domainName[0]) + domainName[1..];
    }

    private static ContractEndpoint ProjectSingletonOperation(Action action) {
        if (action.Parameters.Count != 1) {
            throw new InvalidOperationException(
                $"Cannot project action '{action.Name}' as a v1 singleton operation: it has " +
                $"{action.Parameters.Count} parameters. v1 projects only entry-shaped actions " +
                "with exactly one request payload; instance-targeted bind is a later slice.");
        }

        return new ContractEndpoint(
            action.Name,
            ContractEndpointKind.Operation,
            ContractEndpointDirection.Outbound,
            new DomainTypeReference(action.Parameters[0].Type.TypeName));
    }

    private static void FailOnDuplicateEndpointNames(IReadOnlyList<ContractEndpoint> endpoints) {
        var duplicate = endpoints
            .GroupBy(e => e.Name, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) {
            throw new InvalidOperationException(
                $"Cannot project domain actions as singleton operations: endpoint name " +
                $"'{duplicate.Key}' is produced by more than one action. v1 requires unique action names.");
        }
    }
}