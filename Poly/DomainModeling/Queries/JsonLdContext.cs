using System.Text.Json.Nodes;

namespace Poly.DomainModeling.Queries;

/// <summary>
/// Context term definitions shared by the RDF projections. Type names claim their bare
/// key; a property or member name that repeats, or that collides with a type name or a
/// namespace prefix, is qualified as <c>Owner.Name</c> so the context never silently
/// picks a winner. Terms are emitted in the caller's document order; nothing here re-sorts.
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
        foreach (var type in typeNames) {
            if (!claimed.Contains(type.Name)) {
                context[type.Name] = type.Iri;
                claimed.Add(type.Name);
            }
        }

        // One counting pass by name, then one pass in encounter order: a name used once and not
        // claimed keeps its bare key; any other is qualified by its owner.
        var members = memberNames.ToList();
        var uses = members
            .GroupBy(n => n.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var member in members) {
            if (!claimed.Contains(member.Name) && uses[member.Name] == 1)
                context[member.Name] = member.Iri;
            else
                context[$"{member.Owner}.{member.Name}"] = member.Iri;
        }
    }
}