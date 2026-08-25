using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;

namespace Poly.DomainModeling.Libraries.Storage;

/// <summary>
/// Generic persistence emit flag (<c>uses persistence</c>). Vendor libraries
/// (<c>sqlite</c>, …) register the same pass; do not load both.
/// </summary>
public sealed class PersistenceEmitLibrary : IDomainLibrary {
    public string Id => "persistence";

    public void Register(SessionBuilder builder) {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddAnalyzer(new PersistenceSurfacePass());
    }
}