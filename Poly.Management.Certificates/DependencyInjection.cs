using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Poly.Management.Certificates.Monitoring;

public static class DependencyInjection
{
    public static IServiceCollection AddCertificateMonitoring(
        this IServiceCollection serviceCollection,
        Action<OptionsBuilder<CertificateMonitoringOptions>>? configure = default)
    {
        serviceCollection.AddSingleton(TimeProvider.System);

        return serviceCollection
            .AddCertificateMonitoringOptions(configure)
            .AddCertificateDiscoveryService()
            .AddCertificateMonitoringBackgroundWorker()
            .AddCertificateMonitoringHealthChecks();
    }

    public static IServiceCollection AddCertificateMonitoringOptions(
        this IServiceCollection serviceCollection,
        Action<OptionsBuilder<CertificateMonitoringOptions>>? configure = default)
    {
        var builder = serviceCollection
            .AddOptions<CertificateMonitoringOptions>()
            .BindConfiguration(configSectionPath: CertificateMonitoringOptions.ConfigurationPath);

        if (configure is not null)
            configure(builder);

        return serviceCollection;
    }

    public static IServiceCollection AddCertificateDiscoveryService(
        this IServiceCollection serviceCollection)
    {
        AddCertificateMonitoringOptions(serviceCollection);
        return serviceCollection
            .AddSingleton<ICertificateDiscoveryService, CertificateDiscoveryService>();
    }

    public static IServiceCollection AddCertificateMonitoringBackgroundWorker(
        this IServiceCollection serviceCollection)
    {
        AddCertificateDiscoveryService(serviceCollection);
        return serviceCollection
            .AddHostedService<CertificateMonitoringBackgroundWorker>();
    }

    public static IServiceCollection AddCertificateMonitoringHealthChecks(
        this IServiceCollection serviceCollection)
    {
        AddCertificateDiscoveryService(serviceCollection);
        serviceCollection
            .AddHealthChecks()
            .AddCheck<CertificateMonitoringHealthCheck>(name: CertificateMonitoringHealthCheck.FriendlyName);
        return serviceCollection;
    }
}
