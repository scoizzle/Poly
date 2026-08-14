using Poly.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Parsing;

namespace Poly.DomainModeling.Packs;

/// <summary>
/// Surfaces a library registers into during <see cref="IDomainLibrary.Register"/>.
/// Every registration lands on the same <see cref="DomainHostBuilder"/> that
/// <c>Build()</c> freezes as a <see cref="DomainHost"/>.
/// </summary>
public sealed class HostSurfaces {
    private readonly DomainHostBuilder _host;

    internal HostSurfaces(DomainHostBuilder host) {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    public AnnotationRegistry Annotations => _host.Annotations;

    public ExpressionFormRegistry ExpressionForms => _host.ExpressionForms;

    public TypeMappingRegistry TypeMaps => _host.TypeMaps;

    public void AddStorageConvention(IStorageConvention convention) =>
        _host.AddStorageConvention(convention);

    public void AddAnalysisPass(INodeAnalyzer pass) =>
        _host.AddAnalysisPass(pass);
}