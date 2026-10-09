using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;

namespace Poly.DomainModeling.Libraries.Http;

/// <summary>
/// Host door: <c>uses http</c> publishes <see cref="HttpSurfaceMetadata"/>.
/// When constructed with an <see cref="IArtifactContributor"/>, Register adds it
/// (Load-time, not bag invent).
/// </summary>
public sealed class HttpLibrary : IDomainLibrary {
    private readonly IArtifactContributor? _contributor;

    public HttpLibrary(IArtifactContributor? contributor = null) {
        _contributor = contributor;
    }

    public string Id => "http";

    public void Register(SessionBuilder builder) {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddAnalyzer(new HttpSurfacePass());
        if (_contributor is not null)
            builder.AddArtifactContributor(_contributor);
    }
}
