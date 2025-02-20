using Microsoft.AspNetCore.SignalR.Client;

namespace Poly.Management.Configuration;

public interface IDistributedConfigurationClientConnectionManager
{
    HubConnection HubConnection { get; }
}
