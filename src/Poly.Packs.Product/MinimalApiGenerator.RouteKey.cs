using Poly.Ast;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;

namespace Poly.Packs.Product;

// MinimalApiGenerator (RouteKey) — the one place the generated host reads an entity's route key.
public sealed partial class MinimalApiGenerator {
    /// <summary>An entity's route key: the route parameter name, its CLR type, and the natural-key
    /// property (null for a shadow key).</summary>
    internal readonly record struct RouteKey(string Name, string ClrType, Property? Property);

    /// <summary>The only reader of an entity's key for route templates, the load-by-key lookup, the
    /// <c>Location</c> header and the <c>.http</c> sample. F-13 changes only this body.</summary>
    internal static RouteKey RouteKeyFor(StorageEntity store) =>
        new(store.KeyName, store.KeyClrType, store.KeyProperty);

    /// <summary>The <c>Location</c> key segment for a created row: the key's text, culture-invariant
    /// (date and time keys in ISO 8601, so the route binds it back), escaped as one path segment.</summary>
    private static Node RouteKeySegment(RouteKey key, Node keyValue) {
        Node text = key.ClrType switch {
            "string" => keyValue,
            "DateTime" or "DateOnly" or "TimeOnly" => new Invoke(
                new Member(keyValue, "ToString"), new Constant("O"), InvariantCulture()),
            _ => new Invoke(new Member(new TypeReference("Convert"), "ToString"), keyValue, InvariantCulture()),
        };
        return new Invoke(new Member(new TypeReference("Uri"), "EscapeDataString"), text);
    }

    private static Node InvariantCulture() =>
        new Member(new TypeReference("System.Globalization.CultureInfo"), "InvariantCulture");
}