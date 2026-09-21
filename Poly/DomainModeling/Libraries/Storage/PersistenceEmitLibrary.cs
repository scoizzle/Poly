using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;

namespace Poly.DomainModeling.Libraries.Storage;

/// <summary>
/// Generic persistence emit flag (<c>uses persistence</c>) plus storage mapping.
/// Vendor libraries (<c>sqlite</c>, …) register the same overlay; do not load both.
/// </summary>
public sealed class PersistenceEmitLibrary : IDomainLibrary {
    public string Id => "persistence";

    public void Register(SessionBuilder builder) {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddAnalyzer(new StoragePass(builder.TypeMaps, builder.StorageConventions));
        builder.AddAnalyzer(new PersistenceSurfacePass());
    }
}