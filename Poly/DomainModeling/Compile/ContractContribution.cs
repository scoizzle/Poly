using Poly.Ast.Nodes;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology.Contract;

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
/// A contribution with bodies is a library whose implementation Lower splices at a bind.
/// Each body is a factory, called once per splice site: node id is VM metadata identity, so every
/// splice must get fresh nodes. Load and Resolve never run a body.
/// </remarks>
public sealed record ContractContribution(
    string LibraryId,
    ImportedContract Surface,
    IReadOnlyDictionary<string, Func<Block>> Bodies) {
    /// <summary>
    /// The domain changes that seed <see cref="Surface"/> onto a domain declaring
    /// <c>uses &lt;LibraryId&gt;</c>. When the source wrote a surface pin, it is recorded
    /// beside the surface; the surface's own version is the package's.
    /// </summary>
    /// <summary>
    /// Host targets this package's implementation is for, by id (<c>posix</c>, <c>windows</c>).
    /// Empty means the package declares no platform constraint. Implementation metadata, not
    /// surface: it is deliberately not on <see cref="ImportedContract"/> and not in the surface
    /// fingerprint, because swapping a package for another target must not move the pin.
    /// </summary>
    public IReadOnlyList<string> Targets { get; init; } = [];

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