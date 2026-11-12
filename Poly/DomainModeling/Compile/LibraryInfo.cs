using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// Read-only facts about one catalog library, answered by a one-time
/// <see cref="IDomainLibrary.Register"/> probe on a throwaway builder.
/// <see cref="PrimitiveSeeds"/> keep the library's own order;
/// <see cref="HostTargets"/> and <see cref="ContractNames"/> are distinct and
/// ordinal-sorted.
/// </summary>
public sealed record LibraryInfo(
    string Id,
    IReadOnlyList<(string Name, PrimitiveSemantics Semantics)> PrimitiveSeeds,
    IReadOnlyList<string> HostTargets,
    IReadOnlyList<string> ContractNames);

/// <summary>
/// One library's cached probe: its facts plus the constraint compilers its
/// <see cref="IDomainLibrary.Register"/> installed. The catalog keys the probe,
/// so <see cref="ProvidersOf"/> reports the extension id and never the
/// compiler's own <see cref="IConstraintCompiler.LibraryId"/>.
/// </summary>
internal sealed record LibraryProbe(
    LibraryInfo Info,
    IReadOnlyList<IConstraintCompiler> Compilers);