using Poly.DomainModeling.Language;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// The declaration order of a <c>.poly</c> source text, read from the source itself so a
/// projection's order is checked against what was written, not against another model read.
/// Built on <see cref="DslOutline"/>, which has three gaps this helper inherits: it lists only
/// top-level declarations and the members of entities (enum members and value-type properties
/// are not visible, so those are asserted as literals); it classifies a member as a navigation
/// only when the third token is <c>many</c>, <c>one</c> or <c>owned</c>, so a bare
/// <c>parent: Zebra</c> reports <c>property</c>; and it surfaces subscriptions and unnamed members
/// that are not entity members, which <see cref="Members"/> filters out. Compare to a projection
/// by name only, since projections carry no kinds.
/// </summary>
public sealed record DeclarationOrder(
    IReadOnlyList<string> Types,
    IReadOnlyDictionary<string, IReadOnlyList<(string Kind, string Name)>> Outline) {
    private static readonly string[] EntityMemberKinds = ["property", "navigation", "stage", "action", "policy"];

    /// <summary>Reads the order from <paramref name="poly"/>; no analysis is run.</summary>
    public static DeclarationOrder Of(string poly) {
        var roots = DslOutline.Build(poly).Roots
            .Where(r => r.Kind is "entity" or "enum" or "value")
            .ToList();
        return new DeclarationOrder(
            [.. roots.Select(r => r.Name)],
            roots.Where(r => r.Kind == "entity").ToDictionary(
                r => r.Name,
                r => (IReadOnlyList<(string Kind, string Name)>)[.. r.Children.Select(c => (c.Kind, c.Name))],
                StringComparer.Ordinal));
    }

    /// <summary>Every child the outline reports for <paramref name="entity"/>, entity members or not.</summary>
    public IReadOnlyList<(string Kind, string Name)> Raw(string entity) => Outline[entity];

    /// <summary>The five entity-member kinds, interleaved as written.</summary>
    public IReadOnlyList<string> Members(string entity) =>
        [.. Outline[entity].Where(m => EntityMemberKinds.Contains(m.Kind)).Select(m => m.Name)];

    /// <summary>Members the outline classifies as properties.</summary>
    public IReadOnlyList<string> Properties(string entity) =>
        [.. Outline[entity].Where(m => m.Kind == "property").Select(m => m.Name)];

    /// <summary>Members the outline classifies as navigations (<c>many</c>/<c>one</c>/<c>owned</c> only).</summary>
    public IReadOnlyList<string> Navigations(string entity) =>
        [.. Outline[entity].Where(m => m.Kind == "navigation").Select(m => m.Name)];
}