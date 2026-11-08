using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology.Contract;

using SyntaxNode = Poly.Ast.Node;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// One library's contract contribution: the exported surface and, when the library has
/// one, the implementation AST per endpoint. <see cref="SessionBuilder.AddContract"/>
/// creates it while <c>IDomainLibrary.Register</c> runs, so <see cref="LibraryId"/> is the
/// library that owns the surface.
/// </summary>
/// <remarks>
/// A surface-only contribution (empty <see cref="Bodies"/>) is an export package: a sibling
/// domain's public contract, loaded by <c>uses &lt;id&gt;</c> and compiled away at a bind.
/// A contribution with bodies is a library whose implementation Lower may splice later.
/// The body is stored here; Load and Resolve never run it.
/// </remarks>
public sealed record ContractContribution(
    string LibraryId,
    ImportedContract Surface,
    IReadOnlyDictionary<string, IReadOnlyList<SyntaxNode>> Bodies) {
    /// <summary>
    /// The domain changes that seed <see cref="Surface"/> onto a domain declaring
    /// <c>uses &lt;LibraryId&gt;</c>. When the source wrote a surface pin, it is recorded
    /// beside the surface; the surface's own version is the package's.
    /// </summary>
    public IReadOnlyList<DomainChange> ToChanges(string? pin) {
        var changes = new List<DomainChange> {
            new AddImportedContractChange(
                Surface.Name,
                Surface.SourceKind,
                Surface.SourceIdentifier,
                Surface.Version) {
                UsesId = LibraryId,
                Pin = pin,
            },
        };
        foreach (var valueType in Surface.Types)
            changes.Add(new AddContractValueTypeChange(Surface.Name, valueType));
        foreach (var endpoint in Surface.Endpoints)
            changes.Add(new AddContractEndpointChange(Surface.Name, endpoint));
        return changes;
    }
}