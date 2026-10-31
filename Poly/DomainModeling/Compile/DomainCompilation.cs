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
    /// <paramref name="seed"/> as additive extension facts.
    /// </summary>
    public static IReadOnlyList<DomainChange> WithSeed(
        IReadOnlyList<DomainChange> changes,
        IReadOnlyList<string> seed) {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(seed);
        if (changes.OfType<AddDomainExtensionChange>().Any())
            return changes;
        return [.. changes, .. seed.Select(id => new AddDomainExtensionChange(id))];
    }
}