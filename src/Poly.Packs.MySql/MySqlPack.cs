using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.ContractFill;
using Poly.DomainModeling.Libraries.Storage;

namespace Poly.Packs.MySql;

/// <summary>
/// MySQL persistence library: vendor type maps via the same host load seam.
/// </summary>
public sealed class MySqlLibrary : IDomainLibrary {
    public string Id => "mysql";

    public void Register(SessionBuilder builder) {
        ArgumentNullException.ThrowIfNull(builder);
        MySqlDefaults.ApplyTypeMaps(builder.TypeMaps);
    }
}