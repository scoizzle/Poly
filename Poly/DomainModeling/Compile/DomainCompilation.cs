using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;
using Poly.DomainModeling.Runtime;

using Action = Poly.DomainModeling.Ontology.Action;
using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using PrimitiveType = Poly.DomainModeling.Ontology.PrimitiveType;
using Subtract = Poly.DomainModeling.Ontology.Subtract;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// Compiles a .poly unit: peek extension ids, seed if the source listed none,
/// resolve tables, stamp ids onto the resulting <see cref="Domain"/>.
/// </summary>
public static class DomainCompilation {
    /// <summary>Reads <c>uses</c> ids from the domain header. Empty if none.</summary>
    public static IReadOnlyList<string> PeekExtensions(string poly) {
        ArgumentNullException.ThrowIfNull(poly);
        var reader = new DslTokenReader(poly);
        if (reader.Peek().Kind != DslTokenKind.Domain)
            return [];
        reader.Consume(1);
        if (reader.Peek().Kind != DslTokenKind.Identifier)
            return [];
        reader.Consume(1);
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Peek().Kind == DslTokenKind.Uses) {
            reader.Consume(1);
            if (reader.Peek().Kind != DslTokenKind.Identifier)
                break;
            var id = reader.Peek().Text;
            if (!seen.Add(id))
                throw new FormatException($"Domain lists extension '{id}' more than once.");
            ids.Add(id);
            reader.Consume(1);
        }
        return ids;
    }

    /// <summary>
    /// If the change list already adds extensions, leave it. Otherwise prepend
    /// <paramref name="seed"/> as additive extension facts.
    /// </summary>
    public static IReadOnlyList<DomainChange> WithSeed(
        IReadOnlyList<DomainChange> changes,
        IReadOnlyList<string> seed) {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(seed);
        if (changes.OfType<AddDomainExtensionChange>().Any())
            return changes;
        return [.. seed.Select(id => new AddDomainExtensionChange(id)), .. changes];
    }
}