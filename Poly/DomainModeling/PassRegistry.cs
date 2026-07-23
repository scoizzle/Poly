using Poly.Syntax.Analysis;

namespace Poly.DomainModeling;

/// <summary>
/// Registry for additional analysis passes contributed by packs.
/// Packs call <see cref="AddAnalyzer"/> during configuration
/// (e.g. in <c>AddSqliteDefaults</c>) to inject validation or
/// enrichment passes into the infrastructure pipeline.
///
/// Registered passes run AFTER the built-in passes in the pipeline.
/// If a pack needs to alter columns BEFORE StoragePass computes
/// column metadata, register the pack pass BEFORE requesting
/// StoragePass (e.g. by configuring the authoring context before
/// the DslCompiler creates it).
/// </summary>
public sealed class PassRegistry {
    private readonly List<INodeAnalyzer> _passes = new();

    /// <summary>Registers a pack-contributed analysis pass.</summary>
    public void AddAnalyzer(INodeAnalyzer pass) {
        ArgumentNullException.ThrowIfNull(pass);
        _passes.Add(pass);
    }

    /// <summary>Returns all registered passes for builder consumption.</summary>
    internal IReadOnlyList<INodeAnalyzer> Build() => _passes.AsReadOnly();
}