using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// Compiles a .poly unit: resolve the parsed document against its loaded session,
/// seed authored extension ids when the source listed none, and stamp those ids
/// onto the resulting <see cref="Domain"/>. Temporal is not a seed; Load supplies it.
/// </summary>
public static class DomainCompilation {
    /// <summary>Applies the document's library meaning and returns its changes.</summary>
    public static List<DomainChange> Resolve(DslSyntaxTree tree, DomainSession session) =>
        new DomainResolver(tree, session).Resolve();

    /// <summary>
    /// If the change list already adds extensions, leave it. Otherwise append
    /// <paramref name="seed"/> as additive extension facts. When <paramref name="session"/>
    /// is supplied, a seed library that contributed a contract surface seeds it too,
    /// exactly as an authored <c>uses</c> line would.
    /// </summary>
    public static IReadOnlyList<DomainChange> WithSeed(
        IReadOnlyList<DomainChange> changes,
        IReadOnlyList<string> seed,
        DomainSession? session = null) {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(seed);
        if (changes.OfType<AddDomainExtensionChange>().Any())
            return changes;
        var seeded = new List<DomainChange>();
        foreach (var id in seed) {
            seeded.Add(new AddDomainExtensionChange(id));
            if (session?.FindContractContribution(id) is { } contribution)
                seeded.AddRange(contribution.ToChanges(pin: null));
        }
        return [.. changes, .. seeded];
    }
}