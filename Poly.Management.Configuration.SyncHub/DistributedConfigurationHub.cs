using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Poly.Management.Configuration.Abstractions;
using Poly.Management.Configuration.Abstractions.Requests;

namespace Poly.Management.Configuration.SyncHub;

public class DistributedConfigurationHub() : Hub<IDistributedConfigurationClient>
{
    public ValueTask<IConfigurationSection> GetConfigurationSection(GetConfigurationSectionRequest request, CancellationToken cancellationToken = default)
    {

    }

    public ValueTask<IConfigurationSection> GetConfigurationSectionChildren(GetConfigurationSectionChildrenRequest request, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public ValueTask<IConfigurationSection> SetConfigurationSection(SetConfigurationSectionRequest request, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
