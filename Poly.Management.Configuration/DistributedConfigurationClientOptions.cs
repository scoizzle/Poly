namespace Poly.Management.Configuration;

public record DistributedConfigurationClientOptions(
    string HubUrl = "",
    TimeSpan? KeepAliveInterval = default,
    TimeSpan? ServerTimeout = default
);
