using Poly.DomainModeling;
using Poly.DomainModeling.Packs;

namespace Poly.Packs.MySql;

/// <summary>
/// MySQL persistence library: vendor type maps via the same host load seam.
/// </summary>
public sealed class MySqlLibrary : IDomainLibrary {
    public string Id => "mysql";

    public void Register(DomainHostBuilder builder) {
        ArgumentNullException.ThrowIfNull(builder);
        MySqlDefaults.ApplyTypeMaps(builder.TypeMaps);
    }
}