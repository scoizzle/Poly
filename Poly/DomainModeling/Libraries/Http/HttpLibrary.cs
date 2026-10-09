using Poly.DomainModeling.Analysis;

namespace Poly.DomainModeling.Libraries.Http;

/// <summary>
/// Host door: <c>uses http</c> publishes <see cref="HttpSurfaceMetadata"/>.
/// The compiler session open path registers the Minimal API artifact producer
/// (Load-time, not bag invent).
/// </summary>
public sealed class HttpLibrary : IDomainLibrary {
    public string Id => "http";

    public void Register(SessionBuilder builder) {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddAnalyzer(new HttpSurfacePass());
    }
}
