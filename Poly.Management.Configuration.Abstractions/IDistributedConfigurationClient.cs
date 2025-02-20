using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Poly.Management.Configuration.Abstractions.Requests;

namespace Poly.Management.Configuration.Abstractions;


public interface IDistributedConfigurationClient
{
    public ValueTask<IConfigurationSection> GetConfigurationSection(GetConfigurationSectionRequest request, CancellationToken cancellationToken = default);
    public ValueTask<IEnumerable<IConfigurationSection>> GetConfigurationSectionChildren(GetConfigurationSectionChildrenRequest request, CancellationToken cancellationToken = default);
    public ValueTask SetConfigurationSection(SetConfigurationSectionRequest request, CancellationToken cancellationToken = default);
}
