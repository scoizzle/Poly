using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;

using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Libraries.Platform;

/// <summary>
/// <c>uses posix</c>: the POSIX header as a platform contract. The surface is a handful of
/// header prototypes (<c>Open</c>, <c>Read</c>, <c>Write</c>, <c>Close</c>) and the payload
/// types they name — not a generated SDK. The package declares the one host target it is for,
/// so a session compiled for another target refuses it (spike #413 decisions 3 and 7).
/// </summary>
/// <remarks>
/// Surface-only in this slice: bodies are not read at Load, and the Lower-splice door (#421)
/// owns the syscall AST. Until that body exists, a bind lowers through the existing external
/// adapter stub; this slice proves load and target refusal, not splice.
/// </remarks>
public sealed class PosixLibrary : IDomainLibrary {
    public const string LibraryId = "posix";

    /// <summary>The single host target this package is for. Matched ordinally to the host's.</summary>
    public const string TargetId = "posix";

    public const string SurfaceVersion = "posix-2024";

    public string Id => LibraryId;

    public void Register(SessionBuilder builder) {
        ArgumentNullException.ThrowIfNull(builder);
        // Built here, not in a static initializer: the product catalogs are static, and a
        // malformed surface must fail the load that asked for it, not every catalog user.
        builder.AddContract(Surface(), targets: [TargetId]);
    }

    private static ImportedContract Surface() => new(
        "Posix",
        ContractSourceKind.ExternalProvider,
        LibraryId,
        SurfaceVersion,
        [
            new ContractEndpoint(
                "Open",
                ContractEndpointKind.Operation,
                ContractEndpointDirection.Outbound,
                new DomainTypeReference("OpenRequest")),
            new ContractEndpoint(
                "Read",
                ContractEndpointKind.Operation,
                ContractEndpointDirection.Outbound,
                new DomainTypeReference("ReadRequest")),
            new ContractEndpoint(
                "Write",
                ContractEndpointKind.Operation,
                ContractEndpointDirection.Outbound,
                new DomainTypeReference("WriteRequest")),
            new ContractEndpoint(
                "Close",
                ContractEndpointKind.Operation,
                ContractEndpointDirection.Outbound,
                new DomainTypeReference("CloseRequest")),
        ]) {
        Types = [
            new ValueType("OpenRequest", [
                new Property("Path", new DomainTypeReference("Text"), []),
                new Property("Flags", new DomainTypeReference("Number"), []),
            ], []),
            new ValueType("ReadRequest", [
                new Property("Descriptor", new DomainTypeReference("Number"), []),
                new Property("Count", new DomainTypeReference("Number"), []),
            ], []),
            new ValueType("WriteRequest", [
                new Property("Descriptor", new DomainTypeReference("Number"), []),
                new Property("Data", new DomainTypeReference("Text"), []),
                new Property("Count", new DomainTypeReference("Number"), []),
            ], []),
            new ValueType("CloseRequest", [
                new Property("Descriptor", new DomainTypeReference("Number"), []),
            ], []),
        ],
    };
}