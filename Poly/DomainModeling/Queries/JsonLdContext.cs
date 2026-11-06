using System.Text.Json.Nodes;

namespace Poly.DomainModeling.Queries;

/// <summary>
/// Context term definitions shared by the RDF projections. Type names claim their bare
/// key; a property or member name that repeats, or that collides with a type name or a
/// namespace prefix, is qualified as <c>Owner.Name</c> so the context never silently
/// picks a winner.
/// </summary>
internal static class JsonLdContext {
    private static readonly string[] Prefixes = ["rdf", "rdfs", "xsd", "sh"];

    public static void AddTerms(
        JsonObject context,
        IEnumerable<(string Name, string Iri)> typeNames,
        IEnumerable<(string Name, string Owner, string Iri)> memberNames) {
        // Prefix declarations are not negotiable: a type or member named rdf/rdfs/xsd/sh
        // must never replace them, or every compact IRI in the graph silently changes.
        var claimed = new HashSet<string>(Prefixes, StringComparer.Ordinal);
        foreach (var type in typeNames.OrderBy(t => t.Name, StringComparer.Ordinal)) {
            if (!claimed.Contains(type.Name)) {
                context[type.Name] = type.Iri;
                claimed.Add(type.Name);
            }
        }

        foreach (var group in memberNames
            .GroupBy(n => n.Name, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)) {
            if (!claimed.Contains(group.Key) && group.Count() == 1) {
                context[group.Key] = group.Single().Iri;
                continue;
            }
            foreach (var entry in group.OrderBy(e => e.Owner, StringComparer.Ordinal))
                context[$"{entry.Owner}.{entry.Name}"] = entry.Iri;
        }
    }
}