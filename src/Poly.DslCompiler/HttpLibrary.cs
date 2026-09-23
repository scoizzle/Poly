using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;

namespace Poly.DslCompiler;

/// <summary>
/// Host door: <c>uses http</c> publishes <see cref="HttpSurfaceMetadata"/>.
/// The Minimal API artifact producer is registered by the compiler session open
/// path with the selected <see cref="DbmsPack"/> (Load-time, not bag invent).
/// </summary>
public sealed class HttpLibrary : IDomainLibrary {
    public string Id => "http";

    public void Register(SessionBuilder builder) {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddAnalyzer(new HttpSurfacePass());
    }
}
