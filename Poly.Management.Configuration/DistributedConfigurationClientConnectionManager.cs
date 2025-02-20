using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;

namespace Poly.Management.Configuration;

public class DistributedConfigurationClientConnectionManager : IDistributedConfigurationClientConnectionManager
{
    private readonly IOptionsMonitor<DistributedConfigurationClientOptions> _optionsMonitor;

    private DistributedConfigurationClientOptions _options;
    private Lazy<HubConnection> _hubConnection;

    public DistributedConfigurationClientConnectionManager(
        IOptionsMonitor<DistributedConfigurationClientOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
        _options = optionsMonitor.CurrentValue;
        _hubConnection = new Lazy<HubConnection>(HubConnectionFactoryFunction);

        optionsMonitor.OnChange((options, _) =>
        {
            _options = options;

            if (_hubConnection?.IsValueCreated is true)
                _hubConnection.Value.DisposeAsync();

            _hubConnection = new Lazy<HubConnection>(HubConnectionFactoryFunction);
        });
    }

    public HubConnection HubConnection => _hubConnection.Value;

    private HubConnection HubConnectionFactoryFunction() => BuildNewConnection(_options);

    private HubConnection BuildNewConnection(DistributedConfigurationClientOptions options) =>
        new HubConnectionBuilder()
            .WithUrl(url: options.HubUrl)
            .WithAutomaticReconnect()
            .WithServerTimeout(timeout: options.ServerTimeout ?? TimeSpan.FromSeconds(30))
            .WithKeepAliveInterval(interval: options.KeepAliveInterval ?? TimeSpan.FromSeconds(15))
            .WithStatefulReconnect()
            .Build();
}
