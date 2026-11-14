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
    /// <c>Location</c> header and the <c>.http</c> sample. F-13 changes only this body.
    /// <c>StorageEntity.RouteKeys</c> (the declared <c>key(...)</c>) is not read by the host: the route key
    /// is the first <c>unique</c> property until F-13.</summary>
    internal static RouteKey RouteKeyFor(StorageEntity store) =>
        new(store.KeyName, store.KeyClrType, store.KeyProperty);

    /// <summary>The <c>Location</c> key segment for a created row: the key's text, culture-invariant,
    /// escaped as one path segment. A Date key is ISO 8601 (<c>2026-01-05</c>), which the route binds back.
    /// DateTime and TimeOnly keys keep the plain invariant text: they are untested until #923 lets a
    /// DateTime/Time host compile. An unset optional key is the empty segment, as before the escape.</summary>
    private static Node RouteKeySegment(RouteKey key, Node keyValue) {
        var optional = key.Property?.Constraints.OfType<OptionalConstraint>().Any() == true;
        Node text = key.ClrType switch {
            "string" => optional ? new Coalesce(keyValue, new Constant("")) : keyValue,
            "DateOnly" when optional => new Conditional(
                new Member(keyValue, "HasValue"),
                new Invoke(new Member(new Member(keyValue, "Value"), "ToString"), new Constant("O"), InvariantCulture()),
                new Constant("")),
            "DateOnly" => new Invoke(new Member(keyValue, "ToString"), new Constant("O"), InvariantCulture()),
            _ => new Invoke(new Member(new TypeReference("System.Convert"), "ToString"), keyValue, InvariantCulture()),
        };
        return new Invoke(new Member(new TypeReference("System.Uri"), "EscapeDataString"), text);
    }

    private static Node InvariantCulture() =>
        new Member(new TypeReference("System.Globalization.CultureInfo"), "InvariantCulture");
}