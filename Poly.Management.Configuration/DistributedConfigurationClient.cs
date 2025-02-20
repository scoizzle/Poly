using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Poly.Management.Configuration.Abstractions;
using Poly.Management.Configuration.Abstractions.Requests;

namespace Poly.Management.Configuration;

public class DistributedConfigurationClient(
    IDistributedConfigurationClientConnectionManager connectionManager,
    ILogger<DistributedConfigurationClient> log) : IDistributedConfigurationClient
{
    public async ValueTask<IConfigurationSection> GetConfigurationSection(GetConfigurationSectionRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            return await connectionManager.HubConnection.InvokeAsync<ConfigurationSection>(
                methodName: nameof(GetConfigurationSection),
                arg1: request,
                cancellationToken);
        }
        catch (Exception ex)
        {
            log.FailedToGetConfigurationSection(ex, request);
            throw;
        }
    }

    public async ValueTask<IEnumerable<IConfigurationSection>> GetConfigurationSectionChildren(GetConfigurationSectionChildrenRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            return await connectionManager.HubConnection.InvokeAsync<IEnumerable<ConfigurationSection>>(
                methodName: nameof(GetConfigurationSectionChildren),
                arg1: request,
                cancellationToken);
        }
        catch (Exception ex)
        {
            log.FailedToGetConfigurationSectionChildren(ex, request);
            throw;
        }
    }

    public async ValueTask SetConfigurationSection(SetConfigurationSectionRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            await connectionManager.HubConnection.InvokeAsync(
                methodName: nameof(SetConfigurationSection),
                arg1: request,
                cancellationToken);
        }
        catch (Exception ex)
        {
            log.FailedToSetConfigurationSection(ex, request);
            throw;
        }
    }
}
