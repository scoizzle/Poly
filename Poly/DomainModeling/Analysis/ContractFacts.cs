using Poly.Analysis;
using Poly.DomainModeling.Ontology.Contract;

namespace Poly.DomainModeling.Analysis;

/// <summary>How a <c>uses</c> pin is compared to the loaded surface.</summary>
public enum ContractPinKind {
    /// <summary>The source wrote no pin: the in-workspace case.</summary>
    None,
    /// <summary>An exact surface version string (<c>2024-11-20</c>).</summary>
    Version,
    /// <summary>An exact surface digest (64 lowercase hex).</summary>
    Fingerprint,
}

/// <summary>Outcome of one bind, as recorded on the <see cref="ContractBinding"/> node.</summary>
public enum ContractBindingStatus {
    /// <summary>The contract and endpoint resolved; the surface is usable.</summary>
    Bound,
    /// <summary>The contract, action, or parameter did not resolve.</summary>
    Unresolved,
    /// <summary>The loaded contract has no endpoint with the authored name.</summary>
    NotOnSurface,
    /// <summary>No candidate endpoint accepts the bound parameter's type.</summary>
    Incompatible,
    /// <summary>Several accepting endpoints, none more constrained than the others.</summary>
    Ambiguous,
    /// <summary>The endpoint resolved, but the surface pin does not match.</summary>
    PinMismatch,
}

/// <summary>
/// #420 (epic #427, spike #413 decision 1): the surface an imported contract resolved to,
/// recorded by analysis. <see cref="Fingerprint"/> is the host's write-back point in the
/// in-workspace case, where the source omitted the pin. This is a fact, not a source rewrite
/// and not part of the digest.
/// </summary>
public sealed record ContractSurfaceMetadata(
    string? LibraryId,
    string Contract,
    string SourceIdentifier,
    string Version,
    string Fingerprint,
    string? Pin,
    ContractPinKind PinKind,
    bool PinMatched) : IAnalysisMetadata;

/// <summary>
/// #420: one bind's recorded fact. <see cref="Endpoint"/> is the resolved exported identity
/// (<c>Charge__Money</c>), which may differ from <see cref="AuthoredEndpoint"/> when the bind
/// named the original action. Lower splices or erases that identity; it does not re-resolve.
/// </summary>
public sealed record ContractBindingMetadata(
    string? LibraryId,
    string Contract,
    string? SourceIdentifier,
    string? Version,
    string? Fingerprint,
    string AuthoredEndpoint,
    string? Endpoint,
    string Action,
    string Parameter,
    ContractBindingStatus Status) : IAnalysisMetadata;

/// <summary>
/// #425 (decision 21): the three-way outcome of one boundary obligation. <see cref="Proven"/>
/// and <see cref="Violated"/> are decided by the constraint merge; <see cref="Unknown"/> is
/// cannot-decide, never a silent pass and never a violation (#442).
/// </summary>
public enum ContractBoundaryVerdict {
    /// <summary>The caller sits inside the obligation.</summary>
    Proven,
    /// <summary>The caller's values are disjoint from the obligation.</summary>
    Violated,
    /// <summary>The merge cannot decide, or the obligation is not readable from the argument.</summary>
    Unknown,
}

/// <summary>
/// #425: the direction a boundary note records. <see cref="Satisfies"/> means the caller is at
/// least as constrained as the endpoint; <see cref="Reverse"/> means it is looser (a tightened
/// precondition or a loosened postcondition, the breaking direction); <see cref="Incomparable"/>
/// means an obligation could not be read from the argument.
/// </summary>
public enum ContractCompatibility {
    Satisfies,
    Reverse,
    Incomparable,
}

/// <summary>
/// #425: the boundary note beside a bind's <see cref="ContractBindingMetadata"/>. Not part of
/// the surface fingerprint, not a pin input, and not a source rewrite. <see cref="Postcondition"/>
/// is <see cref="ContractBoundaryVerdict.Unknown"/> for every contributed body: Scot's
/// 2026-11-08 ruling keeps library bodies out of this slice.
/// </summary>
public sealed record ContractBoundaryMetadata(
    ContractBoundaryVerdict Precondition,
    ContractBoundaryVerdict Postcondition,
    ContractCompatibility Compatibility) : IAnalysisMetadata;

/// <summary>
/// Exact pin matching (decision 5): a 64-lowercase-hex pin is the machine fingerprint; any
/// other pin is the human surface version. No semver solver, and the pin is never the
/// implementation's identity.
/// </summary>
internal static class ContractPin {
    public static ContractPinKind KindOf(string? pin) {
        if (string.IsNullOrEmpty(pin))
            return ContractPinKind.None;
        var isFingerprint = pin.Length == 64
            && pin.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
        return isFingerprint ? ContractPinKind.Fingerprint : ContractPinKind.Version;
    }

    public static bool Matches(ImportedContract contract, string pin) =>
        KindOf(pin) switch {
            ContractPinKind.Fingerprint => string.Equals(pin, contract.Fingerprint, StringComparison.Ordinal),
            ContractPinKind.Version => string.Equals(pin, contract.Version, StringComparison.Ordinal),
            _ => true,
        };
}