using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Queries;

internal enum ElementKind {
    Domain,
    Entity,
    Property,
    Navigation,
    Enum,
    EnumMember,
    ValueType,
    Policy,
    Stage,
    EntityAction,
    StageAction,
    Function,
}

internal enum NotFoundReason {
    Unknown,
    NotAddressable,
}

/// <summary>The outcome of resolving an <see cref="ElementPath"/>: a ref, a not-found, or an ambiguity.</summary>
internal abstract record ElementResolution;

/// <summary>One resolved element. <see cref="Node"/> is null only for an enum member.</summary>
internal sealed record ElementRef(
    ElementKind Kind,
    string? Entity,
    string? Stage,
    string Name,
    string Canonical,
    string AnalysisPath,
    Poly.Ast.Node? Node) : ElementResolution;

internal sealed record ElementNotFound(
    NotFoundReason Reason,
    IReadOnlyList<string> Candidates,
    ElementRef? Partial,
    string Message) : ElementResolution;

internal sealed record ElementAmbiguous(IReadOnlyList<ElementRef> Matches) : ElementResolution;

/// <summary>Resolves an <see cref="ElementPath"/> against a <see cref="Domain"/>. Pure: no analysis.</summary>
internal static class ElementPathResolver {
    public static ElementResolution Resolve(Domain domain, ElementPath path) =>
        new ElementNotFound(NotFoundReason.Unknown, [], null, "");
}